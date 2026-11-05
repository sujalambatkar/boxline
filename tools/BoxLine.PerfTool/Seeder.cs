using System.Data;
using System.Diagnostics;
using BoxLine.Core.Entities;
using BoxLine.Infrastructure.Persistence;
using BoxLine.Infrastructure.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.PerfTool;

/// <summary>Creates the performance database (if needed) and fills it with a large fleet and movement history using SqlBulkCopy.</summary>
public static class Seeder
{
    public const string SeedUserEmail = "perf-seed@boxline.test";
    private const int BatchSize = 50_000;

    public static async Task RunAsync(int containerCount, int movementCount, int seed, int spanDays)
    {
        var connectionString = PerfConnection.Resolve();
        await using var db = PerfConnection.CreateContext(connectionString);
        Console.WriteLine($"Database : {new SqlConnectionStringBuilder(connectionString).InitialCatalog}");

        Console.WriteLine("Applying migrations and the standard reference data (ports, container types, ...)");
        await new DatabaseSeeder(db).SeedAsync();

        var existing = await db.ContainerMovements.CountAsync();
        if (existing > 0)
        {
            throw new InvalidOperationException(
                $"The database already holds {existing:N0} movements. Drop the performance database yourself if you want to seed it again; this tool never deletes data.");
        }

        var seedUserId = await EnsureSeedUserAsync(db);
        var typeIds = await db.ContainerTypes.OrderBy(t => t.Id).Select(t => t.Id).ToListAsync();

        var total = Stopwatch.StartNew();
        var containers = PerfDataGenerator.Containers(containerCount, typeIds.Count, seed);

        var step = Stopwatch.StartNew();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await CopyContainersAsync(connection, containers, typeIds);
        Console.WriteLine($"Containers : {containerCount,9:N0} rows in {step.Elapsed.TotalSeconds,6:N1} s");

        var idByNumber = new Dictionary<string, int>(containerCount);
        await using (var command = new SqlCommand("SELECT Id, Number FROM dbo.Containers", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                idByNumber[reader.GetString(1)] = reader.GetInt32(0);
            }
        }

        var containerIds = containers.Select(c => idByNumber[c.Number]).ToArray();
        var end = DateTime.UtcNow.Date.AddDays(-1);

        step.Restart();
        var written = await CopyMovementsAsync(
            connection, PerfDataGenerator.Movements(containerCount, movementCount, end, spanDays, seed), containerIds, seedUserId, end);
        Console.WriteLine($"Movements  : {written,9:N0} rows in {step.Elapsed.TotalSeconds,6:N1} s");

        Console.WriteLine($"Total      : {total.Elapsed.TotalSeconds:N1} s");
        await Info.PrintAsync(connection);
    }

    private static async Task<int> EnsureSeedUserAsync(BoxLineDbContext db)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == SeedUserEmail);
        if (user is null)
        {
            // An unusable password hash: this account can own movements but can never sign in.
            user = new User { Email = SeedUserEmail, PasswordHash = "!", Role = UserRole.Ops, CreatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        return user.Id;
    }

    private static async Task CopyContainersAsync(SqlConnection connection, IReadOnlyList<PerfContainer> containers, IReadOnlyList<int> typeIds)
    {
        var table = new DataTable();
        table.Columns.Add("Number", typeof(string));
        table.Columns.Add("ContainerTypeId", typeof(int));
        table.Columns.Add("OwnershipType", typeof(string));
        table.Columns.Add("Status", typeof(string));

        foreach (var container in containers)
        {
            table.Rows.Add(container.Number, typeIds[container.TypeIndex], container.Ownership.ToString(), container.Status.ToString());
        }

        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null) { DestinationTableName = "dbo.Containers", BulkCopyTimeout = 0 };
        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table);
    }

    private static async Task<int> CopyMovementsAsync(
        SqlConnection connection, IEnumerable<PerfMovement> movements, int[] containerIds, int seedUserId, DateTime end)
    {
        var written = 0;
        var table = NewMovementTable();

        async Task FlushAsync()
        {
            if (table.Rows.Count == 0)
            {
                return;
            }

            // OccurredMonth is computed by the database and Id is an identity column, so neither is mapped.
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = "dbo.ContainerMovements",
                BatchSize = BatchSize,
                BulkCopyTimeout = 0,
            };
            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulk.WriteToServerAsync(table);
            written += table.Rows.Count;
            Console.Write($"\r  {written:N0} movements written...");
            table = NewMovementTable();
        }

        foreach (var movement in movements)
        {
            var recorded = movement.OccurredAt.AddHours(1);
            table.Rows.Add(
                containerIds[movement.ContainerIndex],
                movement.EventType.ToString(),
                movement.Location,
                movement.OccurredAt,
                seedUserId,
                recorded > end.AddDays(1) ? end.AddDays(1) : recorded);

            if (table.Rows.Count >= BatchSize)
            {
                await FlushAsync();
            }
        }

        await FlushAsync();
        Console.WriteLine();
        return written;
    }

    private static DataTable NewMovementTable()
    {
        var table = new DataTable();
        table.Columns.Add("ContainerId", typeof(int));
        table.Columns.Add("EventType", typeof(string));
        table.Columns.Add("LocationUnLocode", typeof(string));
        table.Columns.Add("OccurredAt", typeof(DateTime));
        table.Columns.Add("RecordedByUserId", typeof(int));
        table.Columns.Add("RecordedAt", typeof(DateTime));
        return table;
    }
}
