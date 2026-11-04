using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class TariffEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateOnly Start = new(2026, 1, 1);

    private async Task<HttpResponseMessage> PostAsync(int typeId, int free, IEnumerable<SlabRequest> slabs, DateOnly? from = null, DateOnly? to = null) =>
        await ops.PostAsJsonAsync("/api/tariffs", new TariffRequest(typeId, free, from ?? Start, to, slabs.ToList()));

    private static async Task<string[]> Errors(HttpResponseMessage response, string key) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors[key];

    // ---- creating ---------------------------------------------------------------------------------

    [Fact]
    public async Task Create_StoresTheTariff_WithSlabsInOrder()
    {
        var type = await TestData.NewContainerTypeAsync(ops);

        var response = await PostAsync(type.Id, 5, TestData.StandardSlabs.Reverse());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var tariff = (await response.Content.ReadFromJsonAsync<TariffResponse>(TestJson.Options))!;
        tariff.ContainerType.Should().Be(type.Code);
        tariff.FreeDays.Should().Be(5);
        tariff.ValidFrom.Should().Be(Start);
        tariff.ValidTo.Should().BeNull();
        tariff.Slabs.Select(s => (s.FromDay, s.ToDay, s.RatePerDayUsd)).Should().Equal((6, 10, 40m), (11, 15, 80m), (16, (int?)null, 120m));
    }

    public static TheoryData<string, string, string> InvalidTariffs() => new()
    {
        { "slabs-gap", "Slabs", "must start on day 11" },
        { "first-slab-too-late", "Slabs", "first slab must start on day 6" },
        { "no-open-end", "Slabs", "must be open-ended" },
        { "open-in-middle", "Slabs", "Only the last slab can be open-ended" },
        { "end-before-start", "Slabs", "end day cannot be before the start day" },
        { "zero-rate", "Slabs", "daily rate must be above 0" },
        { "three-decimals", "Slabs", "at most two decimals" },
        { "no-slabs", "Slabs", "Add at least one slab" },
        { "duplicate-start", "Slabs", "Two slabs start on day" },
        { "negative-free-days", "FreeDays", "between 0 and 60" },
        { "end-date-before-start", "ValidTo", "cannot be before the start date" },
    };

    [Theory]
    [MemberData(nameof(InvalidTariffs))]
    public async Task Create_RejectsAnInvalidTariff(string kind, string field, string expected)
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        int free = 5;
        DateOnly? to = null;
        List<SlabRequest> slabs = kind switch
        {
            "slabs-gap" => [new(6, 10, 40m), new(12, null, 80m)],
            "first-slab-too-late" => [new(7, 10, 40m), new(11, null, 80m)],
            "no-open-end" => [new(6, 10, 40m), new(11, 20, 80m)],
            "open-in-middle" => [new(6, null, 40m), new(11, null, 80m)],
            "end-before-start" => [new(6, 5, 40m), new(6, null, 80m)],
            "zero-rate" => [new(6, null, 0m)],
            "three-decimals" => [new(6, null, 12.345m)],
            "no-slabs" => [],
            "duplicate-start" => [new(6, 10, 40m), new(6, 10, 50m), new(11, null, 80m)],
            "negative-free-days" => [new(0, null, 10m)],
            "end-date-before-start" => [new(6, null, 10m)],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (kind == "negative-free-days")
        {
            free = -1;
        }

        if (kind == "end-date-before-start")
        {
            to = Start.AddDays(-1);
        }

        var response = await PostAsync(type.Id, free, slabs, to: to);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Errors(response, field)).Should().Contain(m => m.Contains(expected));
    }

    [Fact]
    public async Task Create_RejectsAnUnknownContainerType()
    {
        var response = await PostAsync(999_999, 5, TestData.StandardSlabs);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Errors(response, "ContainerTypeId")).Single().Should().Contain("does not exist");
    }

    // ---- validity periods may not overlap ---------------------------------------------------------

    [Fact]
    public async Task Create_RefusesOverlappingValidityPeriodsForTheSameType_ButAllowsAdjacentOnes()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var other = await TestData.NewContainerTypeAsync(ops);
        var june = (new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));
        await TestData.NewTariffAsync(ops, type.Id, validFrom: june.Item1, validTo: june.Item2);

        // Same period, a period inside it, one straddling each end, and one enclosing it.
        foreach (var (from, to) in new (DateOnly, DateOnly?)[]
        {
            (june.Item1, june.Item2),
            (new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 20)),
            (new DateOnly(2026, 5, 15), new DateOnly(2026, 6, 1)),
            (new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 15)),
            (new DateOnly(2026, 1, 1), null),
        })
        {
            (await PostAsync(type.Id, 5, TestData.StandardSlabs, from, to)).StatusCode.Should().Be(HttpStatusCode.Conflict, $"{from} to {to}");
        }

        // The day before and the day after are free, and other container types are unaffected.
        (await PostAsync(type.Id, 5, TestData.StandardSlabs, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync(type.Id, 5, TestData.StandardSlabs, new DateOnly(2026, 7, 1), null)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync(other.Id, 5, TestData.StandardSlabs, june.Item1, june.Item2)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AnOpenEndedTariff_BlocksEveryLaterPeriod()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id, validFrom: new DateOnly(2026, 1, 1));

        (await PostAsync(type.Id, 5, TestData.StandardSlabs, new DateOnly(2030, 1, 1), null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostAsync(type.Id, 5, TestData.StandardSlabs, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- reading ----------------------------------------------------------------------------------

    [Fact]
    public async Task Get_AndList_FilterByContainerType()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var tariff = await TestData.NewTariffAsync(ops, type.Id);

        var one = await ops.GetFromJsonAsync<TariffResponse>($"/api/tariffs/{tariff.Id}", TestJson.Options);
        var filtered = await ops.GetFromJsonAsync<List<TariffResponse>>($"/api/tariffs?containerTypeId={type.Id}", TestJson.Options);
        var all = await ops.GetFromJsonAsync<List<TariffResponse>>("/api/tariffs", TestJson.Options);

        one!.Slabs.Should().HaveCount(3);
        filtered.Should().ContainSingle().Which.Id.Should().Be(tariff.Id);
        all!.Count.Should().BeGreaterThan(1, "the seeded starter tariffs are listed too");
        (await ops.GetAsync("/api/tariffs/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- updating and deleting --------------------------------------------------------------------

    [Fact]
    public async Task Update_ReplacesTheSlabs_EvenWhenTheyReuseTheSameStartDays()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var tariff = await TestData.NewTariffAsync(ops, type.Id);

        // Same first day as before (6) and a different shape after it: exercises the unique (tariff, first day) index.
        var update = new TariffRequest(type.Id, 5, Start, null, [new(6, 12, 45m), new(13, null, 99m)]);
        var response = await ops.PutAsJsonAsync($"/api/tariffs/{tariff.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TariffResponse>(TestJson.Options))!;
        updated.Slabs.Select(s => (s.FromDay, s.ToDay, s.RatePerDayUsd)).Should().Equal((6, 12, 45m), (13, (int?)null, 99m));
        (await ops.GetFromJsonAsync<TariffResponse>($"/api/tariffs/{tariff.Id}", TestJson.Options))!.Slabs.Should().HaveCount(2);
    }

    [Fact]
    public async Task Update_DoesNotClashWithItself_ButCannotMoveOntoAnotherTariffsPeriod()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var first = await TestData.NewTariffAsync(ops, type.Id, validFrom: new DateOnly(2026, 1, 1), validTo: new DateOnly(2026, 6, 30));
        await TestData.NewTariffAsync(ops, type.Id, validFrom: new DateOnly(2026, 7, 1));

        var sameRange = await ops.PutAsJsonAsync($"/api/tariffs/{first.Id}",
            new TariffRequest(type.Id, 7, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), [new(8, null, 50m)]));
        var intoNext = await ops.PutAsJsonAsync($"/api/tariffs/{first.Id}",
            new TariffRequest(type.Id, 7, new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 5), [new(8, null, 50m)]));

        sameRange.StatusCode.Should().Be(HttpStatusCode.OK);
        (await sameRange.Content.ReadFromJsonAsync<TariffResponse>(TestJson.Options))!.FreeDays.Should().Be(7);
        intoNext.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_OfAnUnknownTariff_IsNotFound()
    {
        var type = await TestData.NewContainerTypeAsync(ops);

        var response = await ops.PutAsJsonAsync("/api/tariffs/999999", new TariffRequest(type.Id, 5, Start, null, TestData.StandardSlabs.ToList()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RemovesTheTariffAndItsSlabs()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var tariff = await TestData.NewTariffAsync(ops, type.Id);

        (await ops.DeleteAsync($"/api/tariffs/{tariff.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ops.GetAsync($"/api/tariffs/{tariff.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ops.DeleteAsync($"/api/tariffs/{tariff.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The period is free again.
        (await PostAsync(type.Id, 5, TestData.StandardSlabs)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // ---- who may do what --------------------------------------------------------------------------

    [Fact]
    public async Task Shippers_CanReadTariffs_ButOnlyOpsCanChangeThem()
    {
        var shipper = await factory.RegisterShipperAsync();
        var type = await TestData.NewContainerTypeAsync(ops);
        var tariff = await TestData.NewTariffAsync(ops, type.Id);

        (await shipper.Client.GetAsync($"/api/tariffs/{tariff.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await shipper.Client.GetAsync("/api/tariffs")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await shipper.Client.PostAsJsonAsync("/api/tariffs", new TariffRequest(type.Id, 5, Start, null, TestData.StandardSlabs.ToList())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.PutAsJsonAsync($"/api/tariffs/{tariff.Id}", new TariffRequest(type.Id, 5, Start, null, TestData.StandardSlabs.ToList())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.DeleteAsync($"/api/tariffs/{tariff.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/tariffs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SeededStarterTariffs_AreValidAndCoverTheStandardTypes()
    {
        var all = (await ops.GetFromJsonAsync<List<TariffResponse>>("/api/tariffs", TestJson.Options))!;

        foreach (var code in new[] { "20GP", "40GP", "40HC", "20RF", "40RF", "20OT" })
        {
            var tariff = all.Should().ContainSingle(t => t.ContainerType == code && t.ValidTo == null).Subject;
            tariff.Slabs.First().FromDay.Should().Be(tariff.FreeDays + 1);
            tariff.Slabs.Last().ToDay.Should().BeNull();
        }
    }
}
