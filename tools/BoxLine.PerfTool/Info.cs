using Microsoft.Data.SqlClient;

namespace BoxLine.PerfTool;

/// <summary>Row counts, table and index sizes of the performance database.</summary>
public static class Info
{
    public static async Task RunAsync()
    {
        await using var connection = new SqlConnection(PerfConnection.Resolve());
        await connection.OpenAsync();
        await PrintAsync(connection);
    }

    public static async Task PrintAsync(SqlConnection connection)
    {
        Console.WriteLine();
        Console.WriteLine("Data volume");
        foreach (var (label, sql) in new[]
        {
            ("Containers", "SELECT COUNT(*) FROM dbo.Containers"),
            ("Container movements", "SELECT COUNT(*) FROM dbo.ContainerMovements"),
            ("Distinct locations", "SELECT COUNT(DISTINCT LocationUnLocode) FROM dbo.ContainerMovements"),
            ("Distinct months", "SELECT COUNT(DISTINCT OccurredMonth) FROM dbo.ContainerMovements"),
            ("Earliest movement (UTC)", "SELECT CONVERT(varchar(16), MIN(OccurredAt), 120) FROM dbo.ContainerMovements"),
            ("Latest movement (UTC)", "SELECT CONVERT(varchar(16), MAX(OccurredAt), 120) FROM dbo.ContainerMovements"),
        })
        {
            await using var command = new SqlCommand(sql, connection);
            var value = await command.ExecuteScalarAsync();
            Console.WriteLine($"  {label,-26} {(value is int n ? n.ToString("N0") : value)}");
        }

        Console.WriteLine();
        Console.WriteLine("ContainerMovements storage (MB)");
        const string sizes = """
            SELECT i.name AS IndexName, i.type_desc AS IndexType,
                   CAST(SUM(s.used_page_count) * 8.0 / 1024 AS decimal(10, 1)) AS SizeMb
            FROM sys.indexes i
            JOIN sys.dm_db_partition_stats s ON s.object_id = i.object_id AND s.index_id = i.index_id
            WHERE i.object_id = OBJECT_ID('dbo.ContainerMovements')
            GROUP BY i.name, i.type_desc, i.index_id
            ORDER BY i.index_id
            """;
        await using var command2 = new SqlCommand(sizes, connection);
        await using var reader = await command2.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine($"  {(reader.IsDBNull(0) ? "(heap)" : reader.GetString(0)),-44} {reader.GetString(1),-22} {reader.GetDecimal(2),8:N1}");
        }
    }
}
