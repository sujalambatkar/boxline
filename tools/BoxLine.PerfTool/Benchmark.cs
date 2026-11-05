using System.Diagnostics;
using System.Text.Json;
using BoxLine.Core.Reports;
using BoxLine.Infrastructure.Reports;
using Microsoft.Data.SqlClient;

namespace BoxLine.PerfTool;

/// <summary>
/// Times one scenario of the utilisation report. Run each scenario in its own process (scripts/perf.sh does), and let the
/// wrapper read the operating system's peak memory for that process: .NET cannot report it on macOS.
/// </summary>
public static class Benchmark
{
    public static UtilisationQuery BuildQuery(string range, int pageSize)
    {
        var thisMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        return range.ToLowerInvariant() switch
        {
            "last12" => new UtilisationQuery(thisMonth.AddMonths(-11), thisMonth, null, 1, pageSize),
            "all" => new UtilisationQuery(null, null, null, 1, pageSize),
            // Every month of the data given explicitly, as a UI date picker would send it (different from "all", which sends no bounds).
            "wide" => new UtilisationQuery(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 1), null, 1, pageSize),
            _ => throw new ArgumentException("--range must be last12, all or wide"),
        };
    }

    public static async Task RunAsync(string scenario, string range, int runs, bool cold, int pageSize)
    {
        if (runs < 1)
        {
            throw new ArgumentException("--runs must be at least 1");
        }

        var connectionString = PerfConnection.Resolve();
        var query = BuildQuery(range, pageSize);

        bool indexPresent;
        await using (var probe = new SqlConnection(connectionString))
        {
            await probe.OpenAsync();
            indexPresent = await IndexTool.ExistsAsync(probe);
        }

        double? coldMs = null;
        if (cold)
        {
            await DropCachesAsync(connectionString);
            coldMs = (await TimeOnceAsync(scenario, connectionString, query)).Milliseconds;
        }

        var warm = new List<double>();
        var allocated = new List<double>();
        RawUtilisation? last = null;
        for (var i = 0; i < runs; i++)
        {
            var sample = await TimeOnceAsync(scenario, connectionString, query);
            warm.Add(sample.Milliseconds);
            allocated.Add(sample.AllocatedMegabytes);
            last = sample.Result;
        }

        warm.Sort();
        var median = warm[warm.Count / 2];

        Console.WriteLine($"scenario={scenario} range={range} pageSize={pageSize} reportIndex={(indexPresent ? "present" : "absent")}");
        if (coldMs is { } c)
        {
            Console.WriteLine($"  cold run        : {c,10:N1} ms  (buffer cache emptied first)");
        }

        Console.WriteLine($"  warm runs ({runs})   : {string.Join(", ", warm.Select(w => w.ToString("N1")))} ms");
        Console.WriteLine($"  warm median     : {median,10:N1} ms   min {warm[0]:N1}   max {warm[^1]:N1}");
        Console.WriteLine($"  allocated       : {allocated.Average(),10:N1} MB per run (managed)");
        Console.WriteLine($"  result          : {last!.Rows.Count} rows on the page of {last.TotalRows:N0} total, fleet {last.FleetSize:N0}, {last.Monthly.Count} months");

        Console.WriteLine("RESULT " + JsonSerializer.Serialize(new
        {
            scenario,
            range,
            pageSize,
            reportIndex = indexPresent,
            coldMs,
            warmMs = warm,
            medianMs = median,
            allocatedMbPerRun = Math.Round(allocated.Average(), 1),
            pageRows = last.Rows.Count,
            totalRows = last.TotalRows,
        }));
    }

    private sealed record Sample(double Milliseconds, double AllocatedMegabytes, RawUtilisation Result);

    private static async Task<Sample> TimeOnceAsync(string scenario, string connectionString, UtilisationQuery query)
    {
        // A new context every time, as a web request would have.
        await using var db = PerfConnection.CreateContext(connectionString);
        IUtilisationReport report = scenario.ToLowerInvariant() switch
        {
            "naive-ef" => new NaiveUtilisationReport(db),
            "ef-grouped" => new EfGroupedUtilisationReport(db),
            "sql" => new SqlUtilisationReport(db),
            _ => throw new ArgumentException("--scenario must be naive-ef, ef-grouped or sql"),
        };

        GC.Collect();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        var result = await report.RunAsync(query, CancellationToken.None);
        watch.Stop();

        return new Sample(watch.Elapsed.TotalMilliseconds, (GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / 1048576.0, result);
    }

    /// <summary>Writes dirty pages to disk and empties SQL Server's buffer cache, so the next query starts cold.</summary>
    private static async Task DropCachesAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("CHECKPOINT; DBCC DROPCLEANBUFFERS;", connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }
}
