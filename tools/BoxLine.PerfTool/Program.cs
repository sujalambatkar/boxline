using System.Globalization;
using BoxLine.PerfTool;

// Same output on every machine: no locale-specific digit grouping (an Indian locale prints 500,000 as 5,00,000).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// BoxLine performance tool. It only ever works on a database whose name contains "Perf" (BoxLinePerf by default).
//   seed       [--containers 25000] [--movements 500000] [--seed 20261104] [--span-days 730]
//   info
//   indexes    status | drop | create          (the report index only)
//   benchmark  --scenario naive-ef|ef-grouped|sql  [--range last12|all] [--runs 5] [--cold] [--page-size 20]
//   explain    [--range last12|all]
try
{
    var arguments = Args.Parse(args);
    switch (arguments.Command)
    {
        case "seed":
            await Seeder.RunAsync(
                arguments.Int("containers", 25_000), arguments.Int("movements", 500_000), arguments.Int("seed", 20261104), arguments.Int("span-days", 730));
            break;
        case "info":
            await Info.RunAsync();
            break;
        case "indexes":
            await IndexTool.RunAsync(arguments.Positional ?? "status");
            break;
        case "benchmark":
            await Benchmark.RunAsync(
                arguments.Text("scenario") ?? throw new ArgumentException("--scenario naive-ef|ef-grouped|sql is required"),
                arguments.Text("range") ?? "last12",
                arguments.Int("runs", 5),
                arguments.Flag("cold"),
                arguments.Int("page-size", 20));
            break;
        case "explain":
            await Explain.RunAsync(arguments.Text("range") ?? "last12", arguments.Int("page-size", 20));
            break;
        default:
            Console.WriteLine("Commands: seed | info | indexes status|drop|create | benchmark | explain. See Program.cs for the options.");
            return 1;
    }

    return 0;
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2;
}
