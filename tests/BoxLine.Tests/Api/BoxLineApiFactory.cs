using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BoxLine.Tests.Api;

/// <summary>
/// Hosts the API in-memory using the developer's user-secrets, but against a separate
/// <c>BoxLineTests</c> database on the same SQL Server so tests never touch development data.
/// The Development startup path migrates and seeds that database on first use.
/// </summary>
/// <remarks>
/// The override is applied through an environment variable rather than
/// <c>ConfigureAppConfiguration</c>: <c>Program.cs</c> reads the connection string while the builder
/// is being set up, before factory configuration callbacks run, so callbacks cannot change it.
/// </remarks>
public sealed class BoxLineApiFactory : WebApplicationFactory<Program>
{
    public const string TestDatabase = "BoxLineTests";

    public BoxLineApiFactory()
    {
        var secrets = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
        var connectionString = secrets.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not set in user-secrets.");

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = TestDatabase,
        };

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", builder.ConnectionString);
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<BoxLineApiFactory>
{
    public const string Name = "Api";
}
