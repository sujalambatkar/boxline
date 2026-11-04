using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class VesselEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_StoresTheNormalizedImoNumber()
    {
        var imo = TestData.RandomImo();

        var response = await ops.PostAsJsonAsync("/api/vessels", new VesselRequest("  MV Test Star  ", $"IMO {imo[..4]}-{imo[4..]}"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var vessel = (await response.Content.ReadFromJsonAsync<VesselResponse>())!;
        vessel.ImoNumber.Should().Be(imo);
        vessel.Name.Should().Be("MV Test Star");
    }

    [Fact]
    public async Task Create_RejectsAnImoNumberWithAWrongCheckDigit()
    {
        var imo = TestData.RandomImo();
        var wrong = imo[..6] + ((imo[6] - '0' + 1) % 10);

        var response = await ops.PostAsJsonAsync("/api/vessels", new VesselRequest("MV Wrong", wrong));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["ImoNumber"].Single()
            .Should().Contain("Check digit is wrong").And.Contain($"should end in {imo[6]}");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("90747290")]
    [InlineData("90747AB")]
    public async Task Create_RejectsAnImoNumberOfTheWrongShape(string imo)
    {
        var response = await ops.PostAsJsonAsync("/api/vessels", new VesselRequest("MV Shape", imo));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["ImoNumber"].Single().Should().Contain("7 digits");
    }

    [Fact]
    public async Task Create_RejectsADuplicateImoNumber_AlsoWhenFormattedDifferently()
    {
        var vessel = await TestData.NewVesselAsync(ops);

        var response = await ops.PostAsJsonAsync("/api/vessels", new VesselRequest("MV Twin", $"imo {vessel.ImoNumber}"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ChangesTheNameAndRejectsStealingAnotherVesselsImo()
    {
        var first = await TestData.NewVesselAsync(ops);
        var second = await TestData.NewVesselAsync(ops);

        var rename = await ops.PutAsJsonAsync($"/api/vessels/{first.Id}", new VesselRequest("MV Renamed", first.ImoNumber));
        var steal = await ops.PutAsJsonAsync($"/api/vessels/{first.Id}", new VesselRequest("MV Renamed", second.ImoNumber));

        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rename.Content.ReadFromJsonAsync<VesselResponse>())!.Name.Should().Be("MV Renamed");
        steal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_SearchesByNameOrImo()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var vessel = await TestData.NewVesselAsync(ops, $"MV Search {tag}");

        var byName = await ops.GetFromJsonAsync<List<VesselResponse>>($"/api/vessels?search=search {tag.ToUpperInvariant()}");
        var byImo = await ops.GetFromJsonAsync<List<VesselResponse>>($"/api/vessels?search={vessel.ImoNumber}");

        byName.Should().Contain(v => v.Id == vessel.Id);
        byImo.Should().ContainSingle().Which.Id.Should().Be(vessel.Id);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedVessel_ButNotOneWithVoyages()
    {
        var unused = await TestData.NewVesselAsync(ops);
        var used = await TestData.NewVesselAsync(ops);
        var ports = await TestData.NewPortsAsync(ops, 2);
        await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), used.Id);

        (await ops.DeleteAsync($"/api/vessels/{unused.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var blocked = await ops.DeleteAsync($"/api/vessels/{used.Id}");

        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("has voyages");
    }

    [Fact]
    public async Task OnlyOps_CanUseVessels()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.GetAsync("/api/vessels")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.PostAsJsonAsync("/api/vessels", new VesselRequest("MV Nope", TestData.RandomImo())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/vessels")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
