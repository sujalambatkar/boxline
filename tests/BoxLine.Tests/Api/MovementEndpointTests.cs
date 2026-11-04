using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class MovementEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<ValidationProblemDetails> Problem(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;

    [Fact]
    public async Task ARecordedCycle_GoesThroughAllEightEvents_AndThenANewCycleCanStart()
    {
        var container = await TestData.NewContainerAsync(ops);
        var start = TestData.Start();

        for (var i = 0; i < TestData.Cycle.Length; i++)
        {
            var response = await TestData.RecordAsync(ops, container, TestData.Cycle[i], start.AddHours(i));
            response.StatusCode.Should().Be(HttpStatusCode.Created, $"event {TestData.Cycle[i]} should be accepted");
        }

        (await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, start.AddHours(10)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var timeline = await TestData.TrackAsync(ops, container);
        timeline.Events.Should().HaveCount(9);
        timeline.Events.Select(e => e.EventType).Should().Equal(TestData.Cycle.Append(MovementEventType.GateOutEmpty));
    }

    [Fact]
    public async Task TheFirstEvent_MustBeGateOutEmpty()
    {
        var container = await TestData.NewContainerAsync(ops);

        var response = await TestData.RecordAsync(ops, container, MovementEventType.Stuffed, TestData.Start());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("Expected: GateOutEmpty");
    }

    [Fact]
    public async Task AnOutOfOrderEvent_IsRejected_AndNothingIsRecorded()
    {
        var container = await TestData.NewContainerAsync(ops);
        var start = TestData.Start();
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, start);

        var skipped = await TestData.RecordAsync(ops, container, MovementEventType.GateInFull, start.AddHours(1));
        var repeated = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, start.AddHours(1));

        skipped.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await skipped.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("Expected: Stuffed");
        repeated.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestData.TrackAsync(ops, container)).Events.Should().ContainSingle();
    }

    [Fact]
    public async Task ABackdatedEvent_IsRejected_ButOneAtTheSameInstantIsAccepted()
    {
        var container = await TestData.NewContainerAsync(ops);
        var start = TestData.Start();
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, start);

        var backdated = await TestData.RecordAsync(ops, container, MovementEventType.Stuffed, start.AddMinutes(-1));
        var sameInstant = await TestData.RecordAsync(ops, container, MovementEventType.Stuffed, start);

        backdated.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(backdated)).Errors["OccurredAt"].Single().Should().Contain("earlier than the previous event");
        sameInstant.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AnEventInTheFuture_IsRejected()
    {
        var container = await TestData.NewContainerAsync(ops);

        var response = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, DateTime.UtcNow.AddHours(1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(response)).Errors["OccurredAt"].Single().Should().Contain("future");
    }

    [Fact]
    public async Task ATimeWithAnOffset_IsStoredAsTheSameInstantInUtc()
    {
        var container = await TestData.NewContainerAsync(ops);
        var instant = DateTimeOffset.UtcNow.AddHours(-10);
        var withOffset = instant.ToOffset(TimeSpan.FromHours(5.5)).ToString("o");

        var response = await ops.PostAsJsonAsync("/api/movements", new
        {
            containerNumber = container,
            eventType = "GateOutEmpty",
            locationUnLocode = "INNSA",
            occurredAt = withOffset,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var stored = (await TestData.TrackAsync(ops, container)).Events.Single().OccurredAt;
        stored.Kind.Should().Be(DateTimeKind.Utc);
        stored.Should().BeCloseTo(instant.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("inn")]
    [InlineData("1NNSA")]
    public async Task ABadLocation_IsRejected(string location)
    {
        var container = await TestData.NewContainerAsync(ops);

        var response = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, TestData.Start(), location: location);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheContainerNumber_MustBeValid()
    {
        var invalid = await TestData.RecordAsync(ops, "CSQU3054380", MovementEventType.GateOutEmpty, TestData.Start());
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(invalid)).Errors["ContainerNumber"].Single().Should().Contain("Check digit is wrong");
    }

    [Fact]
    public async Task AWellFormedButUnregisteredContainer_ReturnsNotFound()
    {
        // MSKU1234565 is valid ISO 6346 and is not part of the seeded or test containers.
        var response = await TestData.RecordAsync(ops, "MSKU1234565", MovementEventType.GateOutEmpty, TestData.Start());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheBookingLink_MustBeAConfirmedBooking()
    {
        var shipper = await factory.RegisterShipperAsync();
        var draft = await TestData.ConfirmedBookingAsync(shipper, confirm: false);
        var confirmed = await TestData.ConfirmedBookingAsync(shipper);
        var cancelledSource = await TestData.ConfirmedBookingAsync(shipper);
        await shipper.Client.PostAsync($"/api/bookings/{cancelledSource.Id}/cancel", null);
        var container = await TestData.NewContainerAsync(ops);
        var at = TestData.Start();

        var onDraft = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, at, draft.BookingNumber);
        var onCancelled = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, at, cancelledSource.BookingNumber);
        var onUnknown = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, at, "BL-209901-9999");
        var onConfirmed = await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, at, confirmed.BookingNumber.ToLowerInvariant());

        onDraft.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Problem(onDraft)).Errors["BookingNumber"].Single().Should().Contain("confirmed booking");
        onCancelled.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        onUnknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        onConfirmed.StatusCode.Should().Be(HttpStatusCode.Created);
        (await onConfirmed.Content.ReadFromJsonAsync<MovementResponse>(TestJson.Options))!.BookingNumber.Should().Be(confirmed.BookingNumber);
    }

    [Fact]
    public async Task OnlyOps_CanRecordMovements()
    {
        var shipper = await factory.RegisterShipperAsync();
        var container = await TestData.NewContainerAsync(ops);

        (await TestData.RecordAsync(shipper.Client, container, MovementEventType.GateOutEmpty, TestData.Start()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestData.RecordAsync(factory.CreateClient(), container, MovementEventType.GateOutEmpty, TestData.Start()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SimultaneousIdenticalEvents_RecordExactlyOne()
    {
        var container = await TestData.NewContainerAsync(ops);
        var at = TestData.Start();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, at)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
        (await TestData.TrackAsync(ops, container)).Events.Should().ContainSingle();
    }

    [Fact]
    public async Task AContainerWithHistory_CannotBeDeleted()
    {
        var container = await TestData.NewContainerAsync(ops);
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, TestData.Start());
        var id = (await ops.GetFromJsonAsync<PagedResponse<ContainerResponse>>($"/api/containers?search={container}", TestJson.Options))!.Items.Single().Id;

        var response = await ops.DeleteAsync($"/api/containers/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("recorded movements");
    }
}
