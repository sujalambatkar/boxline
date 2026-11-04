using System.Net;
using FluentAssertions;

namespace BoxLine.Tests.Api;

public class HealthEndpointTests(BoxLineApiFactory factory) : IClassFixture<BoxLineApiFactory>
{
    [Fact]
    public async Task Health_ReturnsHealthy_WhenDatabaseIsReachable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Swagger_ServesTheApiDocument()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("BoxLine API").And.Contain("\"Bearer\"");
    }

    [Fact]
    public async Task Cors_AllowsTheAngularDevOrigin()
    {
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/health");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("http://localhost:4200");
    }
}
