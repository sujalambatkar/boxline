using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using FluentAssertions;

namespace BoxLine.Tests.Api;

/// <summary>Who may call the reference-data and booking endpoints.</summary>
[Collection(ApiCollection.Name)]
public class RoleAuthorizationTests(BoxLineApiFactory factory)
{
    [Theory]
    [InlineData("/api/ports")]
    [InlineData("/api/container-types")]
    [InlineData("/api/containers")]
    [InlineData("/api/bookings")]
    public async Task Anonymous_IsRejectedEverywhere(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/ports")]
    [InlineData("/api/container-types")]
    [InlineData("/api/bookings")]
    public async Task Shipper_CanReadReferenceDataAndOwnBookings(string url)
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Shipper_CannotChangeReferenceData()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.PostAsJsonAsync("/api/ports", new PortRequest("INXXX", "Nowhere", "IN")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.PutAsJsonAsync("/api/ports/1", new PortRequest("INXXX", "Nowhere", "IN")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.DeleteAsync("/api/ports/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.PostAsJsonAsync("/api/container-types", new ContainerTypeRequest("20XX", 20, "x")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.DeleteAsync("/api/container-types/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Shipper_CannotUseTheContainerMaster()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.GetAsync("/api/containers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await shipper.Client.DeleteAsync("/api/containers/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Ops_CanUseTheContainerMaster()
    {
        var ops = await factory.CreateOpsClientAsync();

        (await ops.GetAsync("/api/containers?pageSize=1")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
