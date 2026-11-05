using BoxLine.Infrastructure.Persistence.Configurations;
using Microsoft.Data.SqlClient;

namespace BoxLine.PerfTool;

/// <summary>
/// Drops and re-creates the report index on the performance database, so the same query can be measured with and
/// without it. The statements are the ones the migration uses.
/// </summary>
public static class IndexTool
{
    private const string Name = ContainerMovementConfiguration.ReportIndexName;

    public static async Task RunAsync(string action)
    {
        await using var connection = new SqlConnection(PerfConnection.Resolve());
        await connection.OpenAsync();

        switch (action.ToLowerInvariant())
        {
            case "drop":
                await ExecuteAsync(connection, $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{Name}' AND object_id = OBJECT_ID('dbo.ContainerMovements')) DROP INDEX {Name} ON dbo.ContainerMovements");
                Console.WriteLine($"{Name}: dropped");
                break;
            case "create":
                var watch = System.Diagnostics.Stopwatch.StartNew();
                await ExecuteAsync(connection,
                    $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = '{Name}' AND object_id = OBJECT_ID('dbo.ContainerMovements')) " +
                    $"CREATE INDEX {Name} ON dbo.ContainerMovements (OccurredMonth, LocationUnLocode, ContainerId) INCLUDE (EventType)");
                Console.WriteLine($"{Name}: created in {watch.Elapsed.TotalSeconds:N1} s");
                break;
            case "status":
                break;
            default:
                throw new ArgumentException("Use: indexes status | drop | create");
        }

        Console.WriteLine($"{Name}: {(await ExistsAsync(connection) ? "present" : "absent")}");
    }

    public static async Task<bool> ExistsAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand($"SELECT COUNT(*) FROM sys.indexes WHERE name = '{Name}' AND object_id = OBJECT_ID('dbo.ContainerMovements')", connection);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync();
    }
}
