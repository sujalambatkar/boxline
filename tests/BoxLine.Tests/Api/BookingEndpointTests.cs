using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public partial class BookingEndpointTests(BoxLineApiFactory factory)
{
    [GeneratedRegex(@"^BL-\d{6}-\d{4}$")]
    private static partial Regex NumberPattern();

    private sealed record Refs(int Innsa, int Aejea, int Sgsin, int Type20, int Type40);

    private async Task<Refs> GetRefsAsync(HttpClient client)
    {
        var ports = (await client.GetFromJsonAsync<List<PortResponse>>("/api/ports"))!;
        var types = (await client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;
        return new Refs(
            ports.Single(p => p.UnLocode == "INNSA").Id,
            ports.Single(p => p.UnLocode == "AEJEA").Id,
            ports.Single(p => p.UnLocode == "SGSIN").Id,
            types.Single(t => t.Code == "20GP").Id,
            types.Single(t => t.Code == "40HC").Id);
    }

    private static CreateBookingRequest NewBooking(Refs refs, string cargo = "Cotton garments", int? shipperId = null) =>
        new(refs.Innsa, refs.Aejea, cargo, [new BookingLineRequest(refs.Type20, 2), new BookingLineRequest(refs.Type40, 1)], shipperId);

    private static async Task<BookingResponse> Read(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;

    private static async Task<BookingResponse> CreateAsync(Session shipper, CreateBookingRequest request)
    {
        var response = await shipper.Client.PostAsJsonAsync("/api/bookings", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await Read(response);
    }

    private static async Task<PagedResponse<BookingResponse>> ListAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResponse<BookingResponse>>($"/api/bookings{query}", TestJson.Options))!;

    // ---- creating -------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_MakesADraftOwnedByTheShipper_WithAGeneratedNumber()
    {
        var shipper = await factory.RegisterShipperAsync("Acme Exports");
        var refs = await GetRefsAsync(shipper.Client);

        var booking = await CreateAsync(shipper, NewBooking(refs, "  Cotton garments "));

        booking.BookingNumber.Should().MatchRegex(NumberPattern());
        booking.BookingNumber.Should().StartWith($"BL-{DateTime.UtcNow:yyyyMM}-");
        booking.Status.Should().Be(BookingStatus.Draft);
        booking.ShipperId.Should().Be(shipper.User.Id);
        booking.ShipperName.Should().Be("Acme Exports");
        booking.CargoDescription.Should().Be("Cotton garments");
        booking.Pol.UnLocode.Should().Be("INNSA");
        booking.Pod.UnLocode.Should().Be("AEJEA");
        booking.TotalContainers.Should().Be(3);
        booking.Lines.Select(l => l.ContainerTypeCode).Should().Equal("20GP", "40HC");
        booking.AllowedTransitions.Should().Equal(BookingStatus.Confirmed);
        booking.VoyageId.Should().BeNull();
    }

    [Fact]
    public async Task Create_NumbersContinueInSequence()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var first = await CreateAsync(shipper, NewBooking(refs));
        var second = await CreateAsync(shipper, NewBooking(refs));

        static int Sequence(string number) => int.Parse(number[^4..]);
        Sequence(second.BookingNumber).Should().Be(Sequence(first.BookingNumber) + 1);
    }

    [Fact]
    public async Task Create_GivesEveryParallelRequestItsOwnNumber()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => shipper.Client.PostAsJsonAsync("/api/bookings", NewBooking(refs))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var numbers = (await Task.WhenAll(responses.Select(Read))).Select(b => b.BookingNumber).ToList();
        numbers.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Create_IgnoresAShipperIdPassedByAShipper()
    {
        var alice = await factory.RegisterShipperAsync();
        var bob = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(alice.Client);

        var booking = await CreateAsync(alice, NewBooking(refs, shipperId: bob.User.Id));

        booking.ShipperId.Should().Be(alice.User.Id);
    }

    [Fact]
    public async Task Create_ByOps_NeedsAValidShipper()
    {
        var ops = await factory.CreateOpsClientAsync();
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(ops);

        var missing = await ops.PostAsJsonAsync("/api/bookings", NewBooking(refs));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("ShipperId");

        var notAShipper = await ops.PostAsJsonAsync("/api/bookings", NewBooking(refs, shipperId: 999_999));
        notAShipper.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await ops.PostAsJsonAsync("/api/bookings", NewBooking(refs, shipperId: shipper.User.Id));
        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Read(ok)).ShipperId.Should().Be(shipper.User.Id);
    }

    [Fact]
    public async Task Create_RejectsSamePortOfLoadingAndDischarge()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var response = await shipper.Client.PostAsJsonAsync("/api/bookings", NewBooking(refs) with { PodId = refs.Innsa });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["PodId"].Single().Should().Contain("must differ");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task Create_RejectsQuantitiesOutsideTheLimit(int quantity)
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var response = await shipper.Client.PostAsJsonAsync("/api/bookings",
            NewBooking(refs) with { Lines = [new BookingLineRequest(refs.Type20, quantity)] });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["Lines"].Single().Should().Contain("between 1 and 50");
    }

    [Fact]
    public async Task Create_AcceptsTheMaximumQuantity()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var booking = await CreateAsync(shipper, NewBooking(refs) with { Lines = [new BookingLineRequest(refs.Type20, 50)] });

        booking.TotalContainers.Should().Be(50);
    }

    [Fact]
    public async Task Create_RejectsMissingLinesDuplicateTypesAndBlankCargo()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var noLines = await shipper.Client.PostAsJsonAsync("/api/bookings", NewBooking(refs) with { Lines = [] });
        var duplicate = await shipper.Client.PostAsJsonAsync("/api/bookings",
            NewBooking(refs) with { Lines = [new BookingLineRequest(refs.Type20, 1), new BookingLineRequest(refs.Type20, 2)] });
        var blankCargo = await shipper.Client.PostAsJsonAsync("/api/bookings", NewBooking(refs, cargo: "   "));

        noLines.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        blankCargo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_RejectsUnknownPortsAndContainerTypes()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var badPort = await shipper.Client.PostAsJsonAsync("/api/bookings", NewBooking(refs) with { PolId = 999_999 });
        var badType = await shipper.Client.PostAsJsonAsync("/api/bookings",
            NewBooking(refs) with { Lines = [new BookingLineRequest(999_999, 1)] });

        (await badPort.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("PolId");
        (await badType.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("Lines");
    }

    // ---- workflow -------------------------------------------------------------------------------------

    [Fact]
    public async Task Workflow_DraftToConfirmedToCancelled_AndNothingElse()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);
        var draft = await CreateAsync(shipper, NewBooking(refs));
        var url = $"/api/bookings/{draft.Id}";

        // A draft cannot be cancelled directly, and cannot be confirmed twice.
        (await shipper.Client.PostAsync($"{url}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var confirmed = await Read(await shipper.Client.PostAsync($"{url}/confirm", null));
        confirmed.Status.Should().Be(BookingStatus.Confirmed);
        confirmed.AllowedTransitions.Should().Equal(BookingStatus.Cancelled);
        confirmed.UpdatedAt.Should().BeOnOrAfter(confirmed.CreatedAt);
        (await shipper.Client.PostAsync($"{url}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancelled = await Read(await shipper.Client.PostAsync($"{url}/cancel", null));
        cancelled.Status.Should().Be(BookingStatus.Cancelled);
        cancelled.AllowedTransitions.Should().BeEmpty();
        (await shipper.Client.PostAsync($"{url}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await shipper.Client.PostAsync($"{url}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ChangesADraft_ButNotAConfirmedBooking()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);
        var draft = await CreateAsync(shipper, NewBooking(refs));
        var url = $"/api/bookings/{draft.Id}";

        var update = new UpdateBookingRequest(refs.Innsa, refs.Sgsin, "Machine parts", [new BookingLineRequest(refs.Type40, 4)], draft.RowVersion);
        var updated = await Read(await shipper.Client.PutAsJsonAsync(url, update));

        updated.Pod.UnLocode.Should().Be("SGSIN");
        updated.CargoDescription.Should().Be("Machine parts");
        updated.Lines.Should().ContainSingle().Which.Should().Match<BookingLineResponse>(l => l.ContainerTypeCode == "40HC" && l.Quantity == 4);
        updated.RowVersion.Should().NotBe(draft.RowVersion);

        await shipper.Client.PostAsync($"{url}/confirm", null);
        var afterConfirm = await shipper.Client.PutAsJsonAsync(url, update with { RowVersion = null });
        afterConfirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_RejectsAStaleRowVersion()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);
        var draft = await CreateAsync(shipper, NewBooking(refs));
        var url = $"/api/bookings/{draft.Id}";
        var edit = new UpdateBookingRequest(refs.Innsa, refs.Aejea, "First edit", [new BookingLineRequest(refs.Type20, 1)], draft.RowVersion);

        (await shipper.Client.PutAsJsonAsync(url, edit)).StatusCode.Should().Be(HttpStatusCode.OK);
        var stale = await shipper.Client.PutAsJsonAsync(url, edit with { CargoDescription = "Second edit from an old copy" });

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("changed by someone else");
        (await Read(await shipper.Client.GetAsync(url))).CargoDescription.Should().Be("First edit");
    }

    [Fact]
    public async Task Update_RejectsAnInvalidRowVersion()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);
        var draft = await CreateAsync(shipper, NewBooking(refs));

        var response = await shipper.Client.PutAsJsonAsync($"/api/bookings/{draft.Id}",
            new UpdateBookingRequest(refs.Innsa, refs.Aejea, "x", [new BookingLineRequest(refs.Type20, 1)], "%%%not-base64"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- who can see what -----------------------------------------------------------------------------

    [Fact]
    public async Task AShipper_CannotSeeOrChangeAnotherShippersBooking()
    {
        var alice = await factory.RegisterShipperAsync();
        var bob = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(alice.Client);
        var aliceBooking = await CreateAsync(alice, NewBooking(refs));
        var url = $"/api/bookings/{aliceBooking.Id}";

        (await bob.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Client.PostAsync($"{url}/confirm", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Client.PostAsync($"{url}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await bob.Client.PutAsJsonAsync(url,
            new UpdateBookingRequest(refs.Innsa, refs.Aejea, "hijack", [new BookingLineRequest(refs.Type20, 1)]))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        (await ListAsync(bob.Client)).Items.Should().NotContain(b => b.Id == aliceBooking.Id);
        (await Read(await alice.Client.GetAsync(url))).Status.Should().Be(BookingStatus.Draft);
    }

    [Fact]
    public async Task AShipper_CannotWidenTheirListWithAShipperIdFilter()
    {
        var alice = await factory.RegisterShipperAsync();
        var bob = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(alice.Client);
        var aliceBooking = await CreateAsync(alice, NewBooking(refs));

        var list = await ListAsync(bob.Client, $"?shipperId={alice.User.Id}");

        list.Items.Should().NotContain(b => b.Id == aliceBooking.Id);
        list.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Ops_SeeAndManageEveryShippersBookings()
    {
        var alice = await factory.RegisterShipperAsync();
        var bob = await factory.RegisterShipperAsync();
        var ops = await factory.CreateOpsClientAsync();
        var refs = await GetRefsAsync(ops);
        var aliceBooking = await CreateAsync(alice, NewBooking(refs));
        var bobBooking = await CreateAsync(bob, NewBooking(refs));

        (await ops.GetAsync($"/api/bookings/{aliceBooking.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListAsync(ops, $"?shipperId={alice.User.Id}")).Items.Should().ContainSingle(b => b.Id == aliceBooking.Id);
        (await ListAsync(ops, $"?shipperId={bob.User.Id}")).Items.Should().ContainSingle(b => b.Id == bobBooking.Id);

        var confirmed = await Read(await ops.PostAsync($"/api/bookings/{aliceBooking.Id}/confirm", null));
        confirmed.Status.Should().Be(BookingStatus.Confirmed);
        confirmed.ShipperId.Should().Be(alice.User.Id);
    }

    [Fact]
    public async Task Get_ReturnsNotFoundAsAProblemDocument()
    {
        var shipper = await factory.RegisterShipperAsync();

        var response = await shipper.Client.GetAsync("/api/bookings/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // ---- listing --------------------------------------------------------------------------------------

    [Fact]
    public async Task List_FiltersByStatusPortsSearchAndDate_NewestFirst()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);

        var cotton = await CreateAsync(shipper, NewBooking(refs, "Cotton garments"));
        var steel = await CreateAsync(shipper, NewBooking(refs, "Steel coils") with { PodId = refs.Sgsin });
        var tea = await CreateAsync(shipper, NewBooking(refs, "Darjeeling tea") with { PolId = refs.Aejea, PodId = refs.Sgsin });
        await shipper.Client.PostAsync($"/api/bookings/{steel.Id}/confirm", null);

        var all = await ListAsync(shipper.Client);
        all.TotalCount.Should().Be(3);
        all.Items.Select(b => b.Id).Should().Equal(tea.Id, steel.Id, cotton.Id);

        (await ListAsync(shipper.Client, "?status=Confirmed")).Items.Should().ContainSingle().Which.Id.Should().Be(steel.Id);
        (await ListAsync(shipper.Client, "?status=Draft")).TotalCount.Should().Be(2);
        (await ListAsync(shipper.Client, $"?polId={refs.Aejea}")).Items.Should().ContainSingle().Which.Id.Should().Be(tea.Id);
        (await ListAsync(shipper.Client, $"?podId={refs.Sgsin}")).TotalCount.Should().Be(2);
        (await ListAsync(shipper.Client, "?search=steel")).Items.Should().ContainSingle().Which.Id.Should().Be(steel.Id);
        (await ListAsync(shipper.Client, $"?search={cotton.BookingNumber}")).Items.Should().ContainSingle().Which.Id.Should().Be(cotton.Id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        (await ListAsync(shipper.Client, $"?createdFrom={today:yyyy-MM-dd}&createdTo={today:yyyy-MM-dd}")).TotalCount.Should().Be(3);
        (await ListAsync(shipper.Client, $"?createdFrom={today.AddDays(1):yyyy-MM-dd}")).TotalCount.Should().Be(0);
        (await ListAsync(shipper.Client, $"?createdTo={today.AddDays(-1):yyyy-MM-dd}")).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_PagesOnTheServer()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await GetRefsAsync(shipper.Client);
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync(shipper, NewBooking(refs, $"Cargo {i}"));
        }

        var first = await ListAsync(shipper.Client, "?pageSize=2&page=1");
        var second = await ListAsync(shipper.Client, "?pageSize=2&page=2");

        first.TotalCount.Should().Be(3);
        first.Items.Should().HaveCount(2);
        second.Items.Should().ContainSingle();
        first.Items.Select(b => b.Id).Should().NotIntersectWith(second.Items.Select(b => b.Id));
    }

    [Fact]
    public async Task List_CapsThePageSize()
    {
        var shipper = await factory.RegisterShipperAsync();

        var list = await ListAsync(shipper.Client, "?pageSize=100000&page=0");

        list.PageSize.Should().Be(100);
        list.Page.Should().Be(1);
    }
}
