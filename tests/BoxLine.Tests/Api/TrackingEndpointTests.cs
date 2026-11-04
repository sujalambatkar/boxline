using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class TrackingEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ops_SeeTheTimelineOldestFirst_WithTheNextAllowedEvent()
    {
        var container = await TestData.NewContainerAsync(ops);
        var start = TestData.Start();
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, start, location: "INNSA");
        await TestData.RecordAsync(ops, container, MovementEventType.Stuffed, start.AddHours(2), location: "INMUN");

        var tracking = await TestData.TrackAsync(ops, container);

        tracking.ContainerNumber.Should().Be(container);
        tracking.ContainerType.Should().NotBeEmpty();
        tracking.Events.Select(e => e.EventType).Should().Equal(MovementEventType.GateOutEmpty, MovementEventType.Stuffed);
        tracking.Events.Select(e => e.LocationUnLocode).Should().Equal("INNSA", "INMUN");
        tracking.Current!.EventType.Should().Be(MovementEventType.Stuffed);
        tracking.AllowedNextEvents.Should().Equal(MovementEventType.GateInFull);
    }

    [Fact]
    public async Task Ops_SeeAContainerWithNoHistory_AsReadyForGateOutEmpty()
    {
        var container = await TestData.NewContainerAsync(ops);

        var tracking = await TestData.TrackAsync(ops, container);

        tracking.Events.Should().BeEmpty();
        tracking.Current.Should().BeNull();
        tracking.AllowedNextEvents.Should().Equal(MovementEventType.GateOutEmpty);
    }

    [Fact]
    public async Task TheContainerNumber_CanBeTypedLoosely()
    {
        var container = await TestData.NewContainerAsync(ops);
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, TestData.Start());
        var loose = $"{container[..4].ToLowerInvariant()}-{container[4..]}";

        var tracking = await TestData.TrackAsync(ops, loose);

        tracking.ContainerNumber.Should().Be(container);
    }

    [Fact]
    public async Task BadAndUnknownNumbers_AreRejectedDifferently()
    {
        (await ops.GetAsync("/api/tracking/containers/CSQU3054380")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ops.GetAsync("/api/tracking/containers/MSKU1234565")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_CannotTrack()
    {
        (await factory.CreateClient().GetAsync("/api/tracking/containers/CSQU3054383")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await factory.CreateClient().GetAsync("/api/tracking/bookings/BL-202611-0001")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AShipper_SeesOnlyTheEventsOfTheirOwnBookings()
    {
        var alice = await factory.RegisterShipperAsync();
        var bob = await factory.RegisterShipperAsync();
        var carol = await factory.RegisterShipperAsync();
        var aliceBooking = await TestData.ConfirmedBookingAsync(alice);
        var bobBooking = await TestData.ConfirmedBookingAsync(bob);
        var container = await TestData.NewContainerAsync(ops);
        var t = TestData.Start();

        // Gate out empty (no booking), Alice's whole shipment, empty return (no booking), then Bob's first two events.
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, t);
        for (var i = 1; i <= 6; i++)
        {
            (await TestData.RecordAsync(ops, container, TestData.Cycle[i], t.AddHours(i), aliceBooking.BookingNumber))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await TestData.RecordAsync(ops, container, MovementEventType.EmptyReturned, t.AddHours(7));
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, t.AddHours(8), bobBooking.BookingNumber);
        await TestData.RecordAsync(ops, container, MovementEventType.Stuffed, t.AddHours(9), bobBooking.BookingNumber);

        var forAlice = await TestData.TrackAsync(alice.Client, container);
        var forBob = await TestData.TrackAsync(bob.Client, container);
        var forOps = await TestData.TrackAsync(ops, container);

        forAlice.Events.Should().HaveCount(6).And.OnlyContain(e => e.BookingNumber == aliceBooking.BookingNumber);
        forBob.Events.Should().HaveCount(2).And.OnlyContain(e => e.BookingNumber == bobBooking.BookingNumber);
        forOps.Events.Should().HaveCount(10);
        forAlice.AllowedNextEvents.Should().BeEmpty("only Ops may record events");

        (await carol.Client.GetAsync($"/api/tracking/containers/{container}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AShipper_CannotTellAnUnrelatedContainerFromAMissingOne()
    {
        var shipper = await factory.RegisterShipperAsync();
        var container = await TestData.NewContainerAsync(ops);
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, TestData.Start());

        var unrelated = await shipper.Client.GetAsync($"/api/tracking/containers/{container}");
        var missing = await shipper.Client.GetAsync("/api/tracking/containers/MSKU1234565");

        unrelated.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BookingTracking_GroupsEventsByContainer_ForTheOwnerAndOps()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var first = await TestData.NewContainerAsync(ops);
        var second = await TestData.NewContainerAsync(ops);
        var t = TestData.Start();
        await TestData.RecordAsync(ops, first, MovementEventType.GateOutEmpty, t, booking.BookingNumber);
        await TestData.RecordAsync(ops, first, MovementEventType.Stuffed, t.AddHours(1), booking.BookingNumber);
        await TestData.RecordAsync(ops, second, MovementEventType.GateOutEmpty, t, booking.BookingNumber);

        foreach (var client in new[] { shipper.Client, ops })
        {
            var tracking = (await client.GetFromJsonAsync<BookingTrackingResponse>(
                $"/api/tracking/bookings/{booking.BookingNumber.ToLowerInvariant()}", TestJson.Options))!;

            tracking.BookingNumber.Should().Be(booking.BookingNumber);
            tracking.Status.Should().Be(BookingStatus.Confirmed);
            tracking.Pol.UnLocode.Should().Be("INNSA");
            tracking.Pod.UnLocode.Should().Be("NLRTM");
            tracking.Containers.Select(c => c.ContainerNumber).Should().BeEquivalentTo([first, second]);
            tracking.Containers.Single(c => c.ContainerNumber == first).Events.Should().HaveCount(2);
            tracking.Containers.Single(c => c.ContainerNumber == second).Events.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task BookingTracking_IsHiddenFromOtherShippers_AndEmptyWithoutMovements()
    {
        var owner = await factory.RegisterShipperAsync();
        var other = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(owner);

        (await other.Client.GetAsync($"/api/tracking/bookings/{booking.BookingNumber}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ops.GetAsync("/api/tracking/bookings/BL-209901-9999")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var empty = (await owner.Client.GetFromJsonAsync<BookingTrackingResponse>(
            $"/api/tracking/bookings/{booking.BookingNumber}", TestJson.Options))!;
        empty.Containers.Should().BeEmpty();
    }
}
