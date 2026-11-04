using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

/// <summary>Sailing search. Every test builds its own ports so other tests' voyages cannot appear in the results.</summary>
[Collection(ApiCollection.Name)]
public class ScheduleEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<ScheduleResponse>> SearchAsync(HttpClient client, int pol, int pod, string query = "") =>
        (await client.GetFromJsonAsync<List<ScheduleResponse>>($"/api/schedules?polId={pol}&podId={pod}{query}", TestJson.Options))!;

    private static string Day(double fromNow) => DateTime.UtcNow.Date.AddDays(fromNow).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Search_FindsTheSailing_WithDeparturesArrivalsAndTransitTime()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 3);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), startInDays: 5, number: null);

        var results = await SearchAsync(shipper.Client, ports[0].Id, ports[2].Id);

        var option = results.Should().ContainSingle().Subject;
        option.VoyageId.Should().Be(voyage.Id);
        option.VoyageNumber.Should().Be(voyage.VoyageNumber);
        option.VesselName.Should().Be(voyage.Vessel.Name);
        option.ImoNumber.Should().Be(voyage.Vessel.ImoNumber);
        option.Pol.UnLocode.Should().Be(ports[0].UnLocode);
        option.Pod.UnLocode.Should().Be(ports[2].UnLocode);
        option.Departure.Should().Be(voyage.Calls[0].Etd!.Value);
        option.Arrival.Should().Be(voyage.Calls[2].Eta!.Value);
        option.TransitDays.Should().BeApproximately((option.Arrival - option.Departure).TotalDays, 0.06);
    }

    [Fact]
    public async Task Search_OnlyFindsSailingsInTheRequestedDirection()
    {
        var ports = await TestData.NewPortsAsync(ops, 3);
        await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList());

        (await SearchAsync(ops, ports[0].Id, ports[1].Id)).Should().ContainSingle();
        (await SearchAsync(ops, ports[1].Id, ports[2].Id)).Should().ContainSingle();
        (await SearchAsync(ops, ports[2].Id, ports[0].Id)).Should().BeEmpty();
        (await SearchAsync(ops, ports[1].Id, ports[0].Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_ListsEarliestDepartureFirst()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var late = await TestData.NewVoyageAsync(ops, ids, startInDays: 30);
        var early = await TestData.NewVoyageAsync(ops, ids, startInDays: 6);
        var middle = await TestData.NewVoyageAsync(ops, ids, startInDays: 14);

        var results = await SearchAsync(ops, ports[0].Id, ports[1].Id);

        results.Select(r => r.VoyageId).Should().Equal(early.Id, middle.Id, late.Id);
    }

    [Fact]
    public async Task Search_RespectsTheDepartureWindow_IncludingBothEnds()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var sooner = await TestData.NewVoyageAsync(ops, ids, startInDays: 5);
        var later = await TestData.NewVoyageAsync(ops, ids, startInDays: 20);

        (await SearchAsync(ops, ports[0].Id, ports[1].Id, $"&departFrom={Day(5)}&departTo={Day(5)}")).Select(r => r.VoyageId).Should().Equal(sooner.Id);
        (await SearchAsync(ops, ports[0].Id, ports[1].Id, $"&departFrom={Day(6)}&departTo={Day(19)}")).Should().BeEmpty();
        (await SearchAsync(ops, ports[0].Id, ports[1].Id, $"&departFrom={Day(6)}&departTo={Day(25)}")).Select(r => r.VoyageId).Should().Equal(later.Id);
        (await SearchAsync(ops, ports[0].Id, ports[1].Id, $"&departFrom={Day(0)}&departTo={Day(60)}")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_DefaultsToTheNextNinetyDays()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var within = await TestData.NewVoyageAsync(ops, ids, startInDays: 60);
        await TestData.NewVoyageAsync(ops, ids, startInDays: 200);

        var results = await SearchAsync(ops, ports[0].Id, ports[1].Id);

        results.Select(r => r.VoyageId).Should().Equal(within.Id);
    }

    [Fact]
    public async Task Search_SkipsCancelledAndCompletedVoyages()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);
        var ids = ports.Select(p => p.Id).ToList();
        var open = await TestData.NewVoyageAsync(ops, ids, startInDays: 5);
        var cancelled = await TestData.NewVoyageAsync(ops, ids, startInDays: 6);
        await TestData.SetStatusAsync(ops, cancelled.Id, VoyageStatus.Cancelled);
        var completed = await TestData.NewVoyageAsync(ops, ids, startInDays: 7);
        await TestData.SetStatusAsync(ops, completed.Id, VoyageStatus.Underway);
        await TestData.RecordActualsAsync(ops, completed, 0, null, DateTime.UtcNow.AddHours(-30));
        await TestData.RecordActualsAsync(ops, completed, 1, DateTime.UtcNow.AddHours(-2), null);
        await TestData.SetStatusAsync(ops, completed.Id, VoyageStatus.Completed);

        var results = await SearchAsync(ops, ports[0].Id, ports[1].Id);

        results.Select(r => r.VoyageId).Should().Equal(open.Id);
    }

    [Fact]
    public async Task Search_SkipsAnUnderwayVoyageOnceItHasLeftTheLoadingPort_ButNotLaterPorts()
    {
        var ports = await TestData.NewPortsAsync(ops, 3);
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), startInDays: 1);
        await TestData.SetStatusAsync(ops, voyage.Id, VoyageStatus.Underway);
        await TestData.RecordActualsAsync(ops, voyage, 0, null, DateTime.UtcNow.AddHours(-3));

        (await SearchAsync(ops, ports[0].Id, ports[2].Id)).Should().BeEmpty("it has already left the first port");
        (await SearchAsync(ops, ports[1].Id, ports[2].Id)).Should().ContainSingle().Which.VoyageId.Should().Be(voyage.Id);
    }

    [Fact]
    public async Task Search_ValidatesTheRequest()
    {
        var ports = await TestData.NewPortsAsync(ops, 2);

        var same = await ops.GetAsync($"/api/schedules?polId={ports[0].Id}&podId={ports[0].Id}");
        var backwards = await ops.GetAsync($"/api/schedules?polId={ports[0].Id}&podId={ports[1].Id}&departFrom={Day(10)}&departTo={Day(2)}");
        var tooLong = await ops.GetAsync($"/api/schedules?polId={ports[0].Id}&podId={ports[1].Id}&departFrom={Day(0)}&departTo={Day(400)}");
        var unknown = await ops.GetAsync($"/api/schedules?polId=999999&podId={ports[1].Id}");

        foreach (var response in new[] { same, backwards, tooLong, unknown })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        (await same.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("PodId");
        (await backwards.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("DepartTo");
        (await tooLong.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["DepartTo"].Single().Should().Contain("366 days");
        (await unknown.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("PolId");
    }

    [Fact]
    public async Task Search_IsOpenToShippers_ButNotToAnonymousCallers()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);

        (await shipper.Client.GetAsync($"/api/schedules?polId={ports[0].Id}&podId={ports[1].Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.CreateClient().GetAsync($"/api/schedules?polId={ports[0].Id}&podId={ports[1].Id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
