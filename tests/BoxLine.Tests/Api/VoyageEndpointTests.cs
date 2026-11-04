using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class VoyageEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string[]> CallErrors(HttpResponseMessage response, string key = "Calls") =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors[key];

    private async Task<HttpResponseMessage> PostVoyage(int vesselId, string number, IEnumerable<CallRequest> calls) =>
        await ops.PostAsJsonAsync("/api/voyages", new VoyageRequest(vesselId, number, calls.ToList()));

    // ---- creating ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_MakesAScheduledVoyageWithItsOrderedRotation()
    {
        var ports = await TestData.NewPortsAsync(ops, 3);
        var vessel = await TestData.NewVesselAsync(ops, "MV Rotation");
        var unique = Guid.NewGuid().ToString("N")[..6];

        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), vessel.Id, number: $"  t-rot {unique}  ");

        voyage.VoyageNumber.Should().Be($"T-ROT {unique}".ToUpperInvariant());
        voyage.Status.Should().Be(VoyageStatus.Scheduled);
        voyage.AllowedTransitions.Should().Equal(VoyageStatus.Underway, VoyageStatus.Cancelled);
        voyage.Vessel.Name.Should().Be("MV Rotation");
        voyage.Vessel.ImoNumber.Should().Be(vessel.ImoNumber);
        voyage.Calls.Select(c => c.Port.UnLocode).Should().Equal(ports.Select(p => p.UnLocode));
        voyage.Calls.Select(c => c.Sequence).Should().Equal(1, 2, 3);
        voyage.Calls[0].Eta.Should().BeNull();
        voyage.Calls[2].Etd.Should().BeNull();
        voyage.Calls[1].Eta!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Create_RejectsADuplicateVoyageNumber()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var first = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        var response = await PostVoyage(first.Vessel.Id, first.VoyageNumber.ToLowerInvariant(), TestData.Rotation(ports.Select(p => p.Id).ToList()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    public static TheoryData<string, string> BadRotations() => new()
    {
        { "single", "at least two port calls" },
        { "duplicate-port", "can appear only once" },
        { "non-increasing-sequence", "must be greater than the previous call's" },
        { "zero-sequence", "sequence must be 1 or more" },
        { "etd-before-eta", "ETD cannot be before ETA" },
        { "arrives-before-departure", "arrival is before call 1 departs" },
        { "no-first-etd", "first port): ETD is required" },
        { "no-last-eta", "last port): ETA is required" },
        { "middle-missing-time", "both ETA and ETD are required" },
    };

    [Theory]
    [MemberData(nameof(BadRotations))]
    public async Task Create_RejectsAnInvalidRotation(string kind, string expectedMessage)
    {
        var ports = await TestData.NewPortsAsync(ops, 3);
        var vessel = await TestData.NewVesselAsync(ops);
        var good = TestData.Rotation(ports.Select(p => p.Id).ToList());

        List<CallRequest> calls = kind switch
        {
            "single" => [good[0]],
            "duplicate-port" => [good[0], good[1], good[2] with { PortId = good[0].PortId }],
            "non-increasing-sequence" => [good[0], good[1], good[2] with { Sequence = 2 }],
            "zero-sequence" => [good[0] with { Sequence = 0 }, good[1], good[2]],
            "etd-before-eta" => [good[0], good[1] with { Etd = good[1].Eta!.Value.AddHours(-1) }, good[2]],
            "arrives-before-departure" => [good[0], good[1] with { Eta = good[0].Etd!.Value.AddHours(-3), Etd = good[0].Etd!.Value.AddHours(-2) }, good[2]],
            "no-first-etd" => [good[0] with { Etd = null }, good[1], good[2]],
            "no-last-eta" => [good[0], good[1], good[2] with { Eta = null }],
            "middle-missing-time" => [good[0], good[1] with { Etd = null }, good[2]],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var response = await PostVoyage(vessel.Id, $"T{Guid.NewGuid():N}"[..12], calls);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CallErrors(response)).Should().Contain(m => m.Contains(expectedMessage));
    }

    [Fact]
    public async Task Create_RejectsUnknownVesselsPortsAndBadNumbers()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var vessel = await TestData.NewVesselAsync(ops);
        var good = TestData.Rotation(ports.Select(p => p.Id).ToList());

        var noVessel = await PostVoyage(999_999, "T-NOVESSEL", good);
        var noPort = await PostVoyage(vessel.Id, "T-NOPORT", [good[0], good[1] with { PortId = 999_999 }]);
        var blank = await PostVoyage(vessel.Id, "   ", good);
        var symbols = await PostVoyage(vessel.Id, "bad!number", good);

        (await CallErrors(noVessel, "VesselId")).Single().Should().Contain("does not exist");
        (await CallErrors(noPort)).Should().Contain("Port 999999 does not exist.");
        (await CallErrors(blank, "VoyageNumber")).Single().Should().Contain("required");
        (await CallErrors(symbols, "VoyageNumber")).Single().Should().Contain("letters, digits");
    }

    [Fact]
    public async Task Create_StoresATimeWithAnOffsetAsTheSameInstantInUtc()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var vessel = await TestData.NewVesselAsync(ops);
        var departure = DateTimeOffset.UtcNow.AddDays(3);

        var response = await ops.PostAsJsonAsync("/api/voyages", new
        {
            vesselId = vessel.Id,
            voyageNumber = $"T{Guid.NewGuid():N}"[..12],
            calls = new object[]
            {
                new { sequence = 1, portId = ports[0].Id, etd = departure.ToOffset(TimeSpan.FromHours(-8)).ToString("o") },
                new { sequence = 2, portId = ports[1].Id, eta = departure.AddDays(9).ToString("o") },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var voyage = (await response.Content.ReadFromJsonAsync<VoyageResponse>(TestJson.Options))!;
        voyage.Calls[0].Etd!.Value.Should().BeCloseTo(departure.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    // ---- editing ----------------------------------------------------------------------------------

    [Fact]
    public async Task Update_ReplacesTheRotation_EvenWhenSequencesAndPortsAreReused()
    {
        var ports = await TestData.NewPortsAsync(ops, 4);
        var voyage = await TestData.NewVoyageAsync(ops, [ports[0].Id, ports[1].Id, ports[2].Id]);

        // Same sequence numbers and two of the same ports: exercises the unique (voyage, sequence/port) indexes.
        var update = new VoyageRequest(voyage.Vessel.Id, voyage.VoyageNumber, TestData.Rotation([ports[1].Id, ports[0].Id, ports[3].Id], 8));
        var response = await ops.PutAsJsonAsync($"/api/voyages/{voyage.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<VoyageResponse>(TestJson.Options))!;
        updated.Calls.Select(c => c.Port.UnLocode).Should().Equal(ports[1].UnLocode, ports[0].UnLocode, ports[3].UnLocode);
        updated.Calls.Select(c => c.Sequence).Should().Equal(1, 2, 3);
        (await ops.GetFromJsonAsync<VoyageResponse>($"/api/voyages/{voyage.Id}", TestJson.Options))!.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_AcceptsKeepingItsOwnNumber_ButNotAnotherVoyagesNumber()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var first = await TestData.NewVoyageAsync(ops, ids);
        var second = await TestData.NewVoyageAsync(ops, ids);

        var same = await ops.PutAsJsonAsync($"/api/voyages/{first.Id}", new VoyageRequest(first.Vessel.Id, first.VoyageNumber, TestData.Rotation(ids)));
        var clash = await ops.PutAsJsonAsync($"/api/voyages/{first.Id}", new VoyageRequest(first.Vessel.Id, second.VoyageNumber, TestData.Rotation(ids)));

        same.StatusCode.Should().Be(HttpStatusCode.OK);
        clash.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_IsOnlyAllowedWhileScheduled()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var voyage = await TestData.NewVoyageAsync(ops, ids);
        await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Underway);

        var response = await ops.PutAsJsonAsync($"/api/voyages/{voyage.Id}", new VoyageRequest(voyage.Vessel.Id, voyage.VoyageNumber, TestData.Rotation(ids)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("Only scheduled voyages can be edited");
    }

    [Fact]
    public async Task Update_CannotDropARouteThatAnActiveBookingDependsOn()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 4);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());
        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[2].Id, voyage.Id);

        // Drops the discharge port of the booking.
        var breaking = new VoyageRequest(voyage.Vessel.Id, voyage.VoyageNumber, TestData.Rotation([ports[0].Id, ports[1].Id, ports[3].Id]));
        var blocked = await ops.PutAsJsonAsync($"/api/voyages/{voyage.Id}", breaking);
        // Keeps both of the booking's ports, in order.
        var harmless = new VoyageRequest(voyage.Vessel.Id, voyage.VoyageNumber, TestData.Rotation([ports[0].Id, ports[3].Id, ports[2].Id]));
        var allowed = await ops.PutAsJsonAsync($"/api/voyages/{voyage.Id}", harmless);

        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain(booking.BookingNumber);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- lifecycle --------------------------------------------------------------------------------

    [Fact]
    public async Task Status_FollowsTheLifecycle_AndCompletionNeedsTheLastArrival()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        (await ops.PostAsJsonAsync($"/api/voyages/{voyage.Id}/status", new VoyageStatusRequest(VoyageStatus.Completed)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var underway = await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Underway);
        underway.AllowedTransitions.Should().Equal(VoyageStatus.Completed, VoyageStatus.Cancelled);

        var early = await ops.PostAsJsonAsync($"/api/voyages/{voyage.Id}/status", new VoyageStatusRequest(VoyageStatus.Completed));
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await early.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("arrival at the last port");

        var started = DateTime.UtcNow.AddHours(-30);
        (await TestData.RecordActualsAsync(ops, underway, 0, null, started)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestData.RecordActualsAsync(ops, underway, 1, started.AddHours(20), null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var completed = await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Completed);
        completed.AllowedTransitions.Should().BeEmpty();
        (await ops.PostAsJsonAsync($"/api/voyages/{voyage.Id}/status", new VoyageStatusRequest(VoyageStatus.Underway)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancel_IsBlockedWhileActiveBookingsAreOnTheVoyage()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());
        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id, voyage.Id, confirm: true);

        var blocked = await ops.PostAsJsonAsync($"/api/voyages/{voyage.Id}/status", new VoyageStatusRequest(VoyageStatus.Cancelled));
        await shipper.Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null);
        var allowed = await ops.PostAsJsonAsync($"/api/voyages/{voyage.Id}/status", new VoyageStatusRequest(VoyageStatus.Cancelled));

        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain(booking.BookingNumber);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- actual times -----------------------------------------------------------------------------

    private async Task<VoyageResponse> UnderwayVoyageAsync(int ports = 3)
    {
        var created = await TestData.NewPortsAsync(ops, ports);
        var voyage = await TestData.NewVoyageAsync(ops, created.Select(p => p.Id).ToList());
        return await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Underway);
    }

    [Fact]
    public async Task Actuals_CanOnlyBeRecordedWhileUnderway()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var scheduled = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        var response = await TestData.RecordActualsAsync(ops, scheduled, 0, null, DateTime.UtcNow.AddHours(-1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("only be recorded while a voyage is underway");
    }

    [Fact]
    public async Task Actuals_AreRecordedInRotationOrder()
    {
        var voyage = await UnderwayVoyageAsync();
        var t0 = DateTime.UtcNow.AddHours(-60);

        var skipAhead = await TestData.RecordActualsAsync(ops, voyage, 1, t0.AddHours(10), null);
        skipAhead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CallErrors(skipAhead, "Actuals")).Should().ContainSingle().Which.Should().Contain("departure from the previous port");

        (await TestData.RecordActualsAsync(ops, voyage, 0, null, t0)).StatusCode.Should().Be(HttpStatusCode.OK);

        var departBeforeArriving = await TestData.RecordActualsAsync(ops, voyage, 1, null, t0.AddHours(20));
        departBeforeArriving.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CallErrors(departBeforeArriving, "Actuals")).Should().Contain(m => m.Contains("arrival before the departure"));

        var arriveBeforeLeaving = await TestData.RecordActualsAsync(ops, voyage, 1, t0.AddHours(-1), null);
        arriveBeforeLeaving.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CallErrors(arriveBeforeLeaving, "Actuals")).Should().Contain(m => m.Contains("before the previous port's departure"));

        var ok = await TestData.RecordActualsAsync(ops, voyage, 1, t0.AddHours(10), t0.AddHours(22));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var recorded = (await ok.Content.ReadFromJsonAsync<VoyageResponse>(TestJson.Options))!;
        recorded.Calls[1].Ata.Should().BeCloseTo(t0.AddHours(10), TimeSpan.FromSeconds(1));
        recorded.Calls[1].Atd.Should().BeCloseTo(t0.AddHours(22), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Actuals_RejectDepartureBeforeArrival_TheFuture_AndTheLastPortsDeparture()
    {
        var voyage = await UnderwayVoyageAsync(2);
        var t0 = DateTime.UtcNow.AddHours(-30);
        await TestData.RecordActualsAsync(ops, voyage, 0, null, t0);

        var backwards = await TestData.RecordActualsAsync(ops, voyage, 1, t0.AddHours(20), t0.AddHours(10));
        var future = await TestData.RecordActualsAsync(ops, voyage, 1, DateTime.UtcNow.AddHours(6), null);
        var lastDeparts = await TestData.RecordActualsAsync(ops, voyage, 1, t0.AddHours(10), t0.AddHours(12));
        var nothing = await TestData.RecordActualsAsync(ops, voyage, 1, null, null);

        (await CallErrors(backwards, "Actuals")).Should().Contain(m => m.Contains("Departure cannot be before arrival"));
        (await CallErrors(future, "Actuals")).Should().Contain(m => m.Contains("in the future"));
        (await CallErrors(lastDeparts, "Actuals")).Should().Contain(m => m.Contains("last port call has no departure"));
        nothing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Actuals_RejectACallThatIsNotOnTheVoyage()
    {
        var voyage = await UnderwayVoyageAsync(2);

        var response = await ops.PutAsJsonAsync($"/api/voyages/{voyage.Id}/calls/999999/actuals", new ActualsRequest(null, DateTime.UtcNow.AddHours(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- deleting and listing ---------------------------------------------------------------------

    [Fact]
    public async Task Delete_RemovesAVoyageThatNeverSailedAndHasNoBookings()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        (await ops.DeleteAsync($"/api/voyages/{voyage.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ops.GetAsync($"/api/voyages/{voyage.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_IsRefusedWithBookingsOrOnceTheVoyageHasSailed()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var withBooking = await TestData.NewVoyageAsync(ops, ids);
        await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id, withBooking.Id);
        var sailed = await TestData.NewVoyageAsync(ops, ids);
        await TestData.SetStatusAsync(ops, sailed.Id, VoyageStatus.Underway);

        (await ops.DeleteAsync($"/api/voyages/{withBooking.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ops.DeleteAsync($"/api/voyages/{sailed.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_FiltersByStatusVesselPortAndSearch_AndPages()
    {
        var ports = await TestData.NewPortsAsync(ops, 3);
        var ids = ports.Select(p => p.Id).ToList();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var vessel = await TestData.NewVesselAsync(ops, $"MV Filter {tag}");
        var a = await TestData.NewVoyageAsync(ops, ids, vessel.Id, startInDays: 5);
        var b = await TestData.NewVoyageAsync(ops, ids, vessel.Id, startInDays: 12);
        await TestData.SetStatusAsync(ops, b.Id, VoyageStatus.Underway);

        PagedResponse<VoyageResponse> Parse(string json) => System.Text.Json.JsonSerializer.Deserialize<PagedResponse<VoyageResponse>>(json, TestJson.Options)!;
        async Task<PagedResponse<VoyageResponse>> Get(string query) => Parse(await ops.GetStringAsync($"/api/voyages{query}"));

        (await Get($"?portId={ports[1].Id}")).Items.Select(v => v.Id).Should().Equal(b.Id, a.Id);
        (await Get($"?vesselId={vessel.Id}&status=Underway")).Items.Should().ContainSingle().Which.Id.Should().Be(b.Id);
        (await Get($"?vesselId={vessel.Id}&status=Scheduled")).Items.Should().ContainSingle().Which.Id.Should().Be(a.Id);
        (await Get($"?search={a.VoyageNumber.ToLowerInvariant()}")).Items.Should().ContainSingle().Which.Id.Should().Be(a.Id);
        (await Get($"?search=filter {tag}")).Items.Select(v => v.Id).Should().BeEquivalentTo([a.Id, b.Id]);

        var paged = await Get($"?portId={ports[1].Id}&pageSize=1&page=2");
        paged.TotalCount.Should().Be(2);
        paged.Items.Should().ContainSingle().Which.Id.Should().Be(a.Id);
    }

    [Fact]
    public async Task OnlyOps_CanManageVoyages()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        (await shipper.Client.GetAsync("/api/voyages")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.GetAsync($"/api/voyages/{voyage.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.PostAsJsonAsync($"/api/voyages/{voyage.Id}/status", new VoyageStatusRequest(VoyageStatus.Underway)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/voyages")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
