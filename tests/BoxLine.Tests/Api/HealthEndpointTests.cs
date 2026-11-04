using System.Net;
using BoxLine.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class HealthEndpointTests(BoxLineApiFactory factory)
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

    [Fact]
    public void IntegrationTests_RunAgainstTheIsolatedTestDatabase_NeverDevelopmentData()
    {
        using var scope = factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<BoxLineDbContext>().Database.GetConnectionString();

        new SqlConnectionStringBuilder(connection).InitialCatalog.Should().Be(BoxLineApiFactory.TestDatabase);
    }
}
