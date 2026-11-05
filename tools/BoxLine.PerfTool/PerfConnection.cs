using BoxLine.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BoxLine.PerfTool;

/// <summary>
/// Works out which database the tool uses and refuses to touch anything that is not clearly a performance database,
/// so a slip can never fill the development database with half a million rows.
/// </summary>
public static class PerfConnection
{
    public const string DefaultDatabase = "BoxLinePerf";

    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("BOXLINE_PERF_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var secrets = new ConfigurationBuilder().AddUserSecrets(typeof(PerfConnection).Assembly).Build();
            configured = secrets.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "No connection string. Set BOXLINE_PERF_CONNECTION, or ConnectionStrings:DefaultConnection in the API's user-secrets.");
        }

        var builder = new SqlConnectionStringBuilder(configured);
        if (string.IsNullOrEmpty(builder.InitialCatalog) || !builder.InitialCatalog.Contains("Perf", StringComparison.OrdinalIgnoreCase))
        {
            // The development connection string points at BoxLine; aim at the performance database instead.
            builder.InitialCatalog = DefaultDatabase;
        }

        if (!builder.InitialCatalog.Contains("Perf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to use database '{builder.InitialCatalog}': its name must contain 'Perf'.");
        }

        return builder.ConnectionString;
    }

    public static BoxLineDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<BoxLineDbContext>().UseSqlServer(connectionString).Options);
}
