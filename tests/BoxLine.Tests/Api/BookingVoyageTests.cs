using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

/// <summary>Linking bookings to voyages: the booking's route must be served by the voyage's rotation.</summary>
[Collection(ApiCollection.Name)]
public class BookingVoyageTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> VoyageError(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["VoyageId"].Single();
    }

    private static async Task<HttpResponseMessage> TryCreate(Session shipper, int pol, int pod, int voyageId)
    {
        var types = (await shipper.Client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;
        return await shipper.Client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(
            pol, pod, "Cargo", [new BookingLineRequest(types[0].Id, 1)], VoyageId: voyageId));
    }

    [Fact]
    public async Task ABooking_CanSailOnAVoyageThatServesItsRoute_AndShowsTheSailing()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 4);
        var vessel = await TestData.NewVesselAsync(ops, "MV Booked");
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), vessel.Id);

        // Loads at the second port and discharges at the fourth: only part of the rotation.
        var booking = await TestData.BookingAsync(shipper, ports[1].Id, ports[3].Id, voyage.Id);

        booking.VoyageId.Should().Be(voyage.Id);
        booking.Voyage.Should().NotBeNull();
        booking.Voyage!.VoyageNumber.Should().Be(voyage.VoyageNumber);
        booking.Voyage.VesselName.Should().Be("MV Booked");
        booking.Voyage.Departure.Should().Be(voyage.Calls[1].Etd!.Value);
        booking.Voyage.Arrival.Should().Be(voyage.Calls[3].Eta!.Value);

        var reread = (await shipper.Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{booking.Id}", TestJson.Options))!;
        reread.Voyage!.Departure.Should().Be(booking.Voyage.Departure);
        var listed = (await shipper.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>("/api/bookings", TestJson.Options))!;
        listed.Items.Single(b => b.Id == booking.Id).Voyage!.VoyageNumber.Should().Be(voyage.VoyageNumber);
    }

    [Fact]
    public async Task ABooking_WithoutAVoyage_HasNoSailing()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);

        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id);

        booking.VoyageId.Should().BeNull();
        booking.Voyage.Should().BeNull();
    }

    [Fact]
    public async Task APortOfLoadingAfterThePortOfDischarge_IsRejected()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 3);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        var reversed = await TryCreate(shipper, ports[2].Id, ports[0].Id, voyage.Id);
        var adjacentReversed = await TryCreate(shipper, ports[1].Id, ports[0].Id, voyage.Id);

        (await VoyageError(reversed)).Should().Be($"Voyage {voyage.VoyageNumber} does not sail from {ports[2].UnLocode} to {ports[0].UnLocode} in that order.");
        (await VoyageError(adjacentReversed)).Should().Contain("in that order");
    }

    [Fact]
    public async Task APortThatIsNotOnTheVoyage_IsRejected()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 4);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Take(3).Select(p => p.Id).ToList());

        (await VoyageError(await TryCreate(shipper, ports[0].Id, ports[3].Id, voyage.Id))).Should().Contain("does not sail from");
        (await VoyageError(await TryCreate(shipper, ports[3].Id, ports[1].Id, voyage.Id))).Should().Contain("does not sail from");
    }

    [Fact]
    public async Task AnUnknownVoyage_IsRejected()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);

        (await VoyageError(await TryCreate(shipper, ports[0].Id, ports[1].Id, 999_999))).Should().Be("Voyage does not exist.");
    }

    [Fact]
    public async Task ACancelledVoyage_TakesNoBookings()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());
        await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Cancelled);

        (await VoyageError(await TryCreate(shipper, ports[0].Id, ports[1].Id, voyage.Id))).Should().Contain("is not taking bookings");
    }

    [Fact]
    public async Task AVoyageThatHasLeftTheLoadingPort_TakesNoNewBookingsFromIt()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 3);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), startInDays: 1);
        await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Underway);
        await TestData.RecordActualsAsync(ops, voyage, 0, null, DateTime.UtcNow.AddHours(-3));

        (await VoyageError(await TryCreate(shipper, ports[0].Id, ports[2].Id, voyage.Id))).Should().Contain("is not taking bookings from");
        (await TryCreate(shipper, ports[1].Id, ports[2].Id, voyage.Id)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task EditingADraft_CanChangeOrRemoveTheVoyage_AndTheRouteMustStillBeServed()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 4);
        var first = await TestData.NewVoyageAsync(ops, ports.Take(3).Select(p => p.Id).ToList());
        var second = await TestData.NewVoyageAsync(ops, [ports[0].Id, ports[1].Id, ports[3].Id]);
        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id, first.Id);
        var types = (await shipper.Client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;
        UpdateBookingRequest Edit(int pod, int? voyage) => new(
            ports[0].Id, pod, "Cargo", [new BookingLineRequest(types[0].Id, 1)], booking.RowVersion, voyage);
        var url = $"/api/bookings/{booking.Id}";

        var moved = await shipper.Client.PutAsJsonAsync(url, Edit(ports[1].Id, second.Id));
        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterMove = (await moved.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;
        afterMove.VoyageId.Should().Be(second.Id);

        // Changing the discharge port to one the voyage does not call at.
        var brokenRoute = await shipper.Client.PutAsJsonAsync(url, Edit(ports[2].Id, second.Id) with { RowVersion = afterMove.RowVersion });
        (await VoyageError(brokenRoute)).Should().Contain("does not sail from");

        var removed = await shipper.Client.PutAsJsonAsync(url, Edit(ports[1].Id, null) with { RowVersion = afterMove.RowVersion });
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!.Voyage.Should().BeNull();
    }

    // ---- Ops assigning a voyage -------------------------------------------------------------------

    [Fact]
    public async Task Ops_CanAssignAndUnassignAVoyageOnAConfirmedBooking()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());
        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id, confirm: true);

        var assigned = await ops.PutAsJsonAsync($"/api/bookings/{booking.Id}/voyage", new AssignVoyageRequest(voyage.Id));
        assigned.StatusCode.Should().Be(HttpStatusCode.OK);
        var onVoyage = (await assigned.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;
        onVoyage.Voyage!.VoyageNumber.Should().Be(voyage.VoyageNumber);
        onVoyage.Status.Should().Be(BookingStatus.Confirmed);

        var removed = await ops.PutAsJsonAsync($"/api/bookings/{booking.Id}/voyage", new AssignVoyageRequest(null));
        (await removed.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!.Voyage.Should().BeNull();
    }

    [Fact]
    public async Task Ops_CannotAssignAVoyageThatDoesNotServeTheRoute_OrToACancelledBooking()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 3);
        var voyage = await TestData.NewVoyageAsync(ops, [ports[1].Id, ports[2].Id]);
        var wrongRoute = await TestData.BookingAsync(shipper, ports[0].Id, ports[2].Id, confirm: true);
        var cancelled = await TestData.BookingAsync(shipper, ports[1].Id, ports[2].Id, confirm: true);
        await shipper.Client.PostAsync($"/api/bookings/{cancelled.Id}/cancel", null);

        (await VoyageError(await ops.PutAsJsonAsync($"/api/bookings/{wrongRoute.Id}/voyage", new AssignVoyageRequest(voyage.Id))))
            .Should().Contain("does not sail from");
        (await ops.PutAsJsonAsync($"/api/bookings/{cancelled.Id}/voyage", new AssignVoyageRequest(voyage.Id)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ops.PutAsJsonAsync("/api/bookings/999999/voyage", new AssignVoyageRequest(voyage.Id)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AShipper_CannotUseTheAssignEndpoint()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());
        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id);

        (await shipper.Client.PutAsJsonAsync($"/api/bookings/{booking.Id}/voyage", new AssignVoyageRequest(voyage.Id)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
