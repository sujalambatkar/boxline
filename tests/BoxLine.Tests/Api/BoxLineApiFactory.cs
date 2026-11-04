using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BoxLine.Tests.Api;

/// <summary>
/// Hosts the API in-memory using the developer's user-secrets, but against a separate
/// <c>BoxLineTests</c> database on the same SQL Server so tests never touch development data.
/// The Development startup path migrates and seeds that database on first use.
/// </summary>
public sealed class BoxLineApiFactory : WebApplicationFactory<Program>
{
    public const string TestDatabase = "BoxLineTests";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddUserSecrets<Program>();

            var connectionString = config.Build().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not set in user-secrets.");

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    connectionString.Replace("Database=BoxLine;", $"Database={TestDatabase};", StringComparison.OrdinalIgnoreCase),
            });
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BoxLineApiFactory>
{
    public const string Name = "Api";
}
