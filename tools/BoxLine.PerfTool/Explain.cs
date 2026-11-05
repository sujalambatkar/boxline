using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace BoxLine.PerfTool;

/// <summary>Runs the report procedure once with actual-plan and I/O statistics switched on, and prints what SQL Server did.</summary>
public static partial class Explain
{
    private static readonly XNamespace Plan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    [GeneratedRegex(@"Table '(?<table>[^']+)'\. Scan count (?<scans>\d+), logical reads (?<logical>\d+), physical reads (?<physical>\d+)")]
    private static partial Regex IoLine();

    public static async Task RunAsync(string range, int pageSize)
    {
        var query = Benchmark.BuildQuery(range, pageSize);
        await using var connection = new SqlConnection(PerfConnection.Resolve());

        var messages = new List<string>();
        connection.InfoMessage += (_, e) => messages.AddRange(e.Errors.Cast<SqlError>().Select(error => error.Message));
        await connection.OpenAsync();
        Console.WriteLine($"Report index : {(await IndexTool.ExistsAsync(connection) ? "present" : "absent")}");
        Console.WriteLine($"Request      : range={range}, page size {pageSize} ({query.FromMonth?.ToString("yyyy-MM") ?? "start"} to {query.ToMonth?.ToString("yyyy-MM") ?? "end"})");

        string Literal(DateOnly? date) => date is { } d ? $"'{d:yyyy-MM-dd}'" : "NULL";
        var sql = $"""
            SET STATISTICS IO ON; SET STATISTICS TIME ON; SET STATISTICS XML ON;
            EXEC dbo.usp_UtilisationByMonthAndPort @FromMonth = {Literal(query.FromMonth)}, @ToMonth = {Literal(query.ToMonth)}, @Location = NULL, @Page = 1, @PageSize = {pageSize};
            """;

        var plans = new List<XDocument>();
        await using (var command = new SqlCommand(sql, connection) { CommandTimeout = 0 })
        await using (var reader = await command.ExecuteReaderAsync())
        {
            do
            {
                if (reader.FieldCount == 1 && reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase))
                {
                    while (await reader.ReadAsync())
                    {
                        plans.Add(XDocument.Parse(reader.GetString(0)));
                    }
                }
            }
            while (await reader.NextResultAsync());
        }

        Console.WriteLine();
        Console.WriteLine("Logical reads per table (8 KB pages), summed over the procedure's statements");
        var totals = new Dictionary<string, (int Scans, long Logical, long Physical)>();
        foreach (var match in messages.SelectMany(m => IoLine().Matches(m)).Cast<Match>())
        {
            var table = match.Groups["table"].Value;
            var current = totals.GetValueOrDefault(table);
            totals[table] = (current.Scans + int.Parse(match.Groups["scans"].Value), current.Logical + long.Parse(match.Groups["logical"].Value), current.Physical + long.Parse(match.Groups["physical"].Value));
        }

        foreach (var (table, value) in totals.OrderByDescending(t => t.Value.Logical).Where(t => !t.Key.StartsWith("Worktable") || t.Value.Logical > 0))
        {
            Console.WriteLine($"  {table,-22} scans {value.Scans,3}   logical reads {value.Logical,10:N0}   physical reads {value.Physical,8:N0}");
        }

        foreach (var line in messages.Where(m => m.Contains("CPU time", StringComparison.Ordinal)).Select(m => m.Trim()))
        {
            Console.WriteLine("  " + line.Replace("\n", " "));
        }

        Console.WriteLine();
        Console.WriteLine("Execution plans (operator, object, estimated rows -> actual rows)");
        foreach (var plan in plans)
        {
            foreach (var statement in plan.Descendants(Plan + "StmtSimple"))
            {
                var text = Regex.Replace(statement.Attribute("StatementText")?.Value ?? string.Empty, @"\s+", " ").Trim();
                var cost = statement.Attribute("StatementSubTreeCost")?.Value;
                Console.WriteLine($"- {text[..Math.Min(100, text.Length)]}  [cost {cost}]");
                var root = statement.Descendants(Plan + "RelOp").FirstOrDefault();
                if (root is not null)
                {
                    PrintOperator(root, 1);
                }
            }
        }
    }

    private static void PrintOperator(XElement op, int depth)
    {
        var physical = op.Attribute("PhysicalOp")?.Value;
        var estimated = double.TryParse(op.Attribute("EstimateRows")?.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : 0;
        var actual = op.Descendants(Plan + "RunTimeCountersPerThread").Where(t => t.Ancestors(Plan + "RelOp").First() == op)
            .Sum(t => long.TryParse(t.Attribute("ActualRows")?.Value, out var a) ? a : 0);
        var target = op.Elements().SelectMany(c => c.Elements(Plan + "Object")).FirstOrDefault();
        var name = target is null ? string.Empty : $" on {target.Attribute("Table")?.Value}.{target.Attribute("Index")?.Value}".Replace("[", string.Empty).Replace("]", string.Empty);
        var parallel = op.Attribute("Parallel")?.Value == "1" ? " (parallel)" : string.Empty;

        Console.WriteLine($"{new string(' ', depth * 2)}{physical}{name}{parallel}   est {e:N0} -> actual {actual:N0}");

        foreach (var child in op.Elements().SelectMany(c => c.Elements(Plan + "RelOp")).Concat(op.Elements(Plan + "RelOp")))
        {
            PrintOperator(child, depth + 1);
        }
    }
}
