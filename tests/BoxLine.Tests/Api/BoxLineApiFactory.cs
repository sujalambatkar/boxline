using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BoxLine.Tests.Api;

/// <summary>Hosts the API in-memory using the developer's user-secrets (database + JWT secret).</summary>
public sealed class BoxLineApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddUserSecrets<Program>());
    }
}
