using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class ReferenceDataEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient client = null!;

    public async Task InitializeAsync() => client = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ports_AreSeededWithTheMajorTradeLanes()
    {
        var ports = await client.GetFromJsonAsync<List<PortResponse>>("/api/ports");

        ports!.Select(p => p.UnLocode).Should().Contain(["INNSA", "INMUN", "AEJEA", "SGSIN", "NLRTM", "DEHAM", "CNSHA"]);
    }

    [Fact]
    public async Task Ports_SearchMatchesNameOrCode()
    {
        var ports = await client.GetFromJsonAsync<List<PortResponse>>("/api/ports?search=rotter");

        ports.Should().ContainSingle().Which.UnLocode.Should().Be("NLRTM");
    }

    [Fact]
    public async Task Ports_CreateUpdateDeleteRoundTrip_NormalizesTheCode()
    {
        var code = $"ZZ{Guid.NewGuid().ToString("N")[..3].ToUpperInvariant().Replace('0', '2').Replace('1', '3')}";

        var create = await client.PostAsJsonAsync("/api/ports", new PortRequest(code.ToLowerInvariant(), "Test Port", "zz"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<PortResponse>())!;
        created.UnLocode.Should().Be(code);
        created.Country.Should().Be("ZZ");

        var update = await client.PutAsJsonAsync($"/api/ports/{created.Id}", new PortRequest(code, "Renamed Port", "ZZ"));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await update.Content.ReadFromJsonAsync<PortResponse>())!.Name.Should().Be("Renamed Port");

        (await client.DeleteAsync($"/api/ports/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/ports/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ports_RejectDuplicatesAndMismatchedCountry()
    {
        (await client.PostAsJsonAsync("/api/ports", new PortRequest("INNSA", "Duplicate", "IN")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var mismatch = await client.PostAsJsonAsync("/api/ports", new PortRequest("QQXYZ", "Odd", "IN"));
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("Country");
    }

    [Fact]
    public async Task ContainerTypes_AreSeededAndCodesAreUnique()
    {
        var types = await client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types");
        types!.Select(t => t.Code).Should().Contain(["20GP", "40GP", "40HC"]);

        (await client.PostAsJsonAsync("/api/container-types", new ContainerTypeRequest("40hc", 40, "dup")))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/api/container-types", new ContainerTypeRequest("BAD", 40, "bad")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ContainerTypes_CannotBeDeletedWhileInUse()
    {
        var types = await client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types");
        var inUse = types!.First(t => t.Code == "20GP");

        var response = await client.DeleteAsync($"/api/container-types/{inUse.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
