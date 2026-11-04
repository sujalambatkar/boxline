using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class DetentionEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly SlabRequest[] Slabs = [new(4, 6, 10m), new(7, null, 25m)];

    /// <summary>Container type with its own tariff: 3 free days, days 4-6 at 10 and day 7 onwards at 25.</summary>
    private async Task<ContainerTypeResponse> TypeWithTariffAsync()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id, 3, Slabs);
        return type;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateTime At(DateOnly day, int hour = 10) => day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

    /// <summary>Records a full cycle ending with the container back at the depot, with gate out full on <paramref name="gateOut"/>.</summary>
    private async Task RecordCycleAsync(string container, DateOnly gateOut, DateOnly? returned, string? booking = null)
    {
        var steps = new (MovementEventType Type, DateTime At, string? Booking)[]
        {
            (MovementEventType.GateOutEmpty, At(gateOut.AddDays(-8)), null),
            (MovementEventType.Stuffed, At(gateOut.AddDays(-7)), booking),
            (MovementEventType.GateInFull, At(gateOut.AddDays(-6)), booking),
            (MovementEventType.LoadedOnVessel, At(gateOut.AddDays(-5)), booking),
            (MovementEventType.Discharged, At(gateOut.AddDays(-1)), booking),
            (MovementEventType.GateOutFull, At(gateOut), booking),
            (MovementEventType.DeliveredToConsignee, At(gateOut, 16), booking),
        };

        foreach (var step in steps)
        {
            (await TestData.RecordAsync(ops, container, step.Type, step.At, step.Booking)).StatusCode.Should().Be(HttpStatusCode.Created, step.Type.ToString());
        }

        if (returned is { } back)
        {
            (await TestData.RecordAsync(ops, container, MovementEventType.EmptyReturned, At(back, 15))).StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    private async Task<DetentionResponse> ForContainerAsync(HttpClient client, string container, string query = "")
    {
        var response = await client.GetAsync($"/api/detention/containers/{container}{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DetentionResponse>(TestJson.Options))!;
    }

    // ---- the what-if calculator -------------------------------------------------------------------

    [Fact]
    public async Task Calculate_ReturnsAFullBreakdown()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id);

        // 16 calendar days from Monday 2026-11-02: 5 free, then 5 days at 40, 5 at 80 and 1 at 120.
        var response = await ops.PostAsJsonAsync("/api/detention/calculate",
            new CalculateRequest(type.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 17)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<DetentionResponse>(TestJson.Options))!;
        result.ContainerType.Should().Be(type.Code);
        result.Currency.Should().Be("USD");
        result.TotalDays.Should().Be(16);
        result.CountedDays.Should().Be(16);
        result.WeekendDaysSkipped.Should().Be(0);
        result.FreeDaysUsed.Should().Be(5);
        result.ChargeableDays.Should().Be(11);
        result.IsOngoing.Should().BeFalse();
        result.Slabs.Should().Equal(
            new SlabChargeResponse(6, 10, 5, 40m, 200m),
            new SlabChargeResponse(11, 15, 5, 80m, 400m),
            new SlabChargeResponse(16, null, 1, 120m, 120m));
        result.TotalAmountUsd.Should().Be(720m);
        result.Tariff.FreeDays.Should().Be(5);
    }

    [Fact]
    public async Task Calculate_SkipsWeekendsOnRequest()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id);
        var request = new CalculateRequest(type.Id, new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 16));

        var including = (await (await ops.PostAsJsonAsync("/api/detention/calculate", request)).Content.ReadFromJsonAsync<DetentionResponse>(TestJson.Options))!;
        var skipping = (await (await ops.PostAsJsonAsync("/api/detention/calculate", request with { CountWeekendsAsChargeable = false }))
            .Content.ReadFromJsonAsync<DetentionResponse>(TestJson.Options))!;

        including.TotalAmountUsd.Should().Be(280m);
        skipping.TotalAmountUsd.Should().Be(80m);
        skipping.CountedDays.Should().Be(7);
        skipping.WeekendDaysSkipped.Should().Be(4);
        skipping.CountWeekendsAsChargeable.Should().BeFalse();
    }

    [Fact]
    public async Task Calculate_ValidatesTheRequest()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id, validFrom: new DateOnly(2026, 6, 1), validTo: new DateOnly(2026, 6, 30));
        var withoutTariff = await TestData.NewContainerTypeAsync(ops);

        var backwards = await ops.PostAsJsonAsync("/api/detention/calculate", new CalculateRequest(type.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 9)));
        var unknownType = await ops.PostAsJsonAsync("/api/detention/calculate", new CalculateRequest(999_999, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)));
        var noTariff = await ops.PostAsJsonAsync("/api/detention/calculate", new CalculateRequest(withoutTariff.Id, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 12)));
        var outsideValidity = await ops.PostAsJsonAsync("/api/detention/calculate", new CalculateRequest(type.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5)));

        backwards.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await backwards.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["ReturnDate"].Single().Should().Contain("cannot be before the out date");
        unknownType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noTariff.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await noTariff.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Be($"No detention tariff is valid for {withoutTariff.Code} on 2026-06-10.");
        outsideValidity.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calculate_IsOpenToShippers_ButNotAnonymousCallers()
    {
        var shipper = await factory.RegisterShipperAsync();
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id);
        var request = new CalculateRequest(type.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 12));

        (await shipper.Client.PostAsJsonAsync("/api/detention/calculate", request)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.CreateClient().PostAsJsonAsync("/api/detention/calculate", request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- priced from a container's movement history -----------------------------------------------

    [Fact]
    public async Task ForContainer_PricesFromGateOutFullToEmptyReturned()
    {
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        var gateOut = Today.AddDays(-12);
        var returned = Today.AddDays(-3);
        await RecordCycleAsync(container, gateOut, returned);

        var result = await ForContainerAsync(ops, container);

        result.ContainerNumber.Should().Be(container);
        result.StartedBy.Should().Be(MovementEventType.GateOutFull);
        result.OutDate.Should().Be(gateOut);
        result.ReturnDate.Should().Be(returned);
        result.IsOngoing.Should().BeFalse();
        result.TotalDays.Should().Be(10);
        result.FreeDaysUsed.Should().Be(3);
        // Days 4-6 at 10 and days 7-10 at 25.
        result.Slabs.Should().Equal(new SlabChargeResponse(4, 6, 3, 10m, 30m), new SlabChargeResponse(7, null, 4, 25m, 100m));
        result.TotalAmountUsd.Should().Be(130m);
    }

    [Fact]
    public async Task ForContainer_CanSkipWeekends_AndAgreesWithTheIndependentFormula()
    {
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        var gateOut = Today.AddDays(-20);
        var returned = Today.AddDays(-2);
        await RecordCycleAsync(container, gateOut, returned);

        var including = await ForContainerAsync(ops, container);
        var skipping = await ForContainerAsync(ops, container, "?countWeekendsAsChargeable=false");

        including.TotalAmountUsd.Should().Be(TestData.ExpectedAmount(gateOut, returned, 3, Slabs, weekends: true));
        skipping.TotalAmountUsd.Should().Be(TestData.ExpectedAmount(gateOut, returned, 3, Slabs, weekends: false));
        skipping.WeekendDaysSkipped.Should().BeGreaterThan(0);
        skipping.TotalAmountUsd.Should().BeLessThan(including.TotalAmountUsd);
    }

    [Fact]
    public async Task ForContainer_RunsUpToTodayWhileTheContainerIsStillOut()
    {
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        var gateOut = Today.AddDays(-9);
        await RecordCycleAsync(container, gateOut, returned: null);

        var result = await ForContainerAsync(ops, container);

        result.IsOngoing.Should().BeTrue();
        result.ReturnDate.Should().Be(Today);
        result.TotalDays.Should().Be(10);
        result.TotalAmountUsd.Should().Be(130m);
    }

    [Fact]
    public async Task ForContainer_UsesTheLatestCycle()
    {
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        await RecordCycleAsync(container, Today.AddDays(-40), Today.AddDays(-30));
        var secondOut = Today.AddDays(-6);
        // Second cycle: empty return was recorded after the first; the container goes out again.
        var steps = new[]
        {
            (MovementEventType.GateOutEmpty, Today.AddDays(-14)),
            (MovementEventType.Stuffed, Today.AddDays(-13)),
            (MovementEventType.GateInFull, Today.AddDays(-12)),
            (MovementEventType.LoadedOnVessel, Today.AddDays(-11)),
            (MovementEventType.Discharged, Today.AddDays(-7)),
            (MovementEventType.GateOutFull, secondOut),
        };
        foreach (var (type2, day) in steps)
        {
            (await TestData.RecordAsync(ops, container, type2, At(day))).StatusCode.Should().Be(HttpStatusCode.Created, type2.ToString());
        }

        var result = await ForContainerAsync(ops, container);

        result.OutDate.Should().Be(secondOut);
        result.IsOngoing.Should().BeTrue("the second cycle has not been returned yet");
        result.TotalDays.Should().Be(7);
    }

    [Fact]
    public async Task ForContainer_HasNoClockUntilTheContainerLeavesTheTerminal()
    {
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        await TestData.RecordAsync(ops, container, MovementEventType.GateOutEmpty, At(Today.AddDays(-5)));

        var untouched = await ops.GetAsync($"/api/detention/containers/{await TestData.NewContainerAsync(ops, type.Id)}");
        var early = await ops.GetAsync($"/api/detention/containers/{container}");

        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await early.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("no detention clock has started");
        untouched.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ForContainer_ReportsAMissingTariffForTheContainersType()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var container = await TestData.NewContainerAsync(ops, type.Id);
        await RecordCycleAsync(container, Today.AddDays(-10), Today.AddDays(-2));

        var response = await ops.GetAsync($"/api/detention/containers/{container}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().StartWith($"No detention tariff is valid for {type.Code}");
    }

    [Fact]
    public async Task ForContainer_RejectsABadOrUnknownNumber()
    {
        (await ops.GetAsync("/api/detention/containers/CSQU3054380")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ops.GetAsync("/api/detention/containers/MSKU1234565")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.CreateClient().GetAsync("/api/detention/containers/CSQU3054383")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- who may price what -----------------------------------------------------------------------

    [Fact]
    public async Task AShipper_CanPriceTheirOwnContainer_OnlyForTheirOwnBookings()
    {
        var owner = await factory.RegisterShipperAsync();
        var stranger = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(owner);
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        var gateOut = Today.AddDays(-12);
        var returned = Today.AddDays(-3);
        // The return is recorded without a booking, as it usually is; the owner must still see the clock stop.
        await RecordCycleAsync(container, gateOut, returned, booking.BookingNumber);

        var mine = await ForContainerAsync(owner.Client, container);

        mine.BookingNumber.Should().Be(booking.BookingNumber);
        mine.IsOngoing.Should().BeFalse();
        mine.ReturnDate.Should().Be(returned);
        mine.TotalAmountUsd.Should().Be(130m);
        (await stranger.Client.GetAsync($"/api/detention/containers/{container}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheBookingNumber_ChoosesWhichBookingsGateOutStartsTheClock()
    {
        var first = await factory.RegisterShipperAsync();
        var second = await factory.RegisterShipperAsync();
        var firstBooking = await TestData.ConfirmedBookingAsync(first);
        var secondBooking = await TestData.ConfirmedBookingAsync(second);
        var type = await TypeWithTariffAsync();
        var container = await TestData.NewContainerAsync(ops, type.Id);
        await RecordCycleAsync(container, Today.AddDays(-40), Today.AddDays(-30), firstBooking.BookingNumber);
        var secondOut = Today.AddDays(-8);
        foreach (var (kind, day) in new[]
        {
            (MovementEventType.GateOutEmpty, Today.AddDays(-20)), (MovementEventType.Stuffed, Today.AddDays(-19)), (MovementEventType.GateInFull, Today.AddDays(-18)),
            (MovementEventType.LoadedOnVessel, Today.AddDays(-17)), (MovementEventType.Discharged, Today.AddDays(-9)), (MovementEventType.GateOutFull, secondOut),
        })
        {
            await TestData.RecordAsync(ops, container, kind, At(day), kind >= MovementEventType.Stuffed ? secondBooking.BookingNumber : null);
        }

        (await ForContainerAsync(ops, container)).OutDate.Should().Be(secondOut, "the latest cycle by default");
        (await ForContainerAsync(ops, container, $"?bookingNumber={firstBooking.BookingNumber}")).OutDate.Should().Be(Today.AddDays(-40));
        (await ForContainerAsync(first.Client, container)).OutDate.Should().Be(Today.AddDays(-40), "a shipper only ever sees their own booking's clock");
        (await ForContainerAsync(second.Client, container)).OutDate.Should().Be(secondOut);
        (await ops.GetAsync($"/api/detention/containers/{container}?bookingNumber=BL-209901-9999")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
