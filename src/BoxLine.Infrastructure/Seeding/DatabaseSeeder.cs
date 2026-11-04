using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Vessels;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Seeding;

/// <summary>Applies migrations and seeds reference data and sample containers. Safe to run repeatedly.</summary>
public sealed class DatabaseSeeder(BoxLineDbContext db)
{
    public const int SampleContainerCount = 200;

    private static readonly Port[] Ports =
    [
        new() { UnLocode = "INNSA", Name = "Nhava Sheva (Jawaharlal Nehru)", Country = "IN" },
        new() { UnLocode = "INMUN", Name = "Mundra", Country = "IN" },
        new() { UnLocode = "AEJEA", Name = "Jebel Ali", Country = "AE" },
        new() { UnLocode = "SGSIN", Name = "Singapore", Country = "SG" },
        new() { UnLocode = "NLRTM", Name = "Rotterdam", Country = "NL" },
        new() { UnLocode = "DEHAM", Name = "Hamburg", Country = "DE" },
        new() { UnLocode = "CNSHA", Name = "Shanghai", Country = "CN" },
    ];

    private static readonly ContainerType[] ContainerTypes =
    [
        new() { Code = "20GP", LengthFt = 20, Description = "20' general purpose" },
        new() { Code = "40GP", LengthFt = 40, Description = "40' general purpose" },
        new() { Code = "40HC", LengthFt = 40, Description = "40' high cube" },
        new() { Code = "20RF", LengthFt = 20, Description = "20' reefer" },
        new() { Code = "40RF", LengthFt = 40, Description = "40' high cube reefer" },
        new() { Code = "20OT", LengthFt = 20, Description = "20' open top" },
    ];

    // Fictional owner codes (category letter U = freight container). BXLU is BoxLine's own fleet.
    private static readonly string[] OwnedPrefixes = ["BXLU"];
    private static readonly string[] LeasedPrefixes = ["LSXU", "NVCU", "PCLU", "OCTU"];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        await SeedPortsAsync(cancellationToken);
        await SeedContainerTypesAsync(cancellationToken);
        await SeedContainersAsync(cancellationToken);
        await SeedVoyagesAsync(cancellationToken);
        await SeedTariffsAsync(cancellationToken);
    }

    private async Task SeedPortsAsync(CancellationToken cancellationToken)
    {
        if (await db.Ports.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Ports.AddRange(Ports);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedContainerTypesAsync(CancellationToken cancellationToken)
    {
        if (await db.ContainerTypes.AnyAsync(cancellationToken))
        {
            return;
        }

        db.ContainerTypes.AddRange(ContainerTypes);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedContainersAsync(CancellationToken cancellationToken)
    {
        if (await db.Containers.AnyAsync(cancellationToken))
        {
            return;
        }

        var typeIds = await db.ContainerTypes.OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(cancellationToken);
        db.Containers.AddRange(GenerateContainers(typeIds, SampleContainerCount));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A starter detention tariff per container type: free days, then three steps of rising daily rates.</summary>
    private async Task SeedTariffsAsync(CancellationToken cancellationToken)
    {
        if (await db.DetentionTariffs.AnyAsync(cancellationToken))
        {
            return;
        }

        // code -> (free days, rate for days after the free period: first step, second step, third step)
        var plan = new Dictionary<string, (int Free, decimal[] Rates)>
        {
            ["20GP"] = (5, [30m, 60m, 90m]),
            ["40GP"] = (5, [40m, 80m, 120m]),
            ["40HC"] = (5, [40m, 80m, 120m]),
            ["20OT"] = (5, [35m, 70m, 105m]),
            ["20RF"] = (4, [55m, 110m, 165m]),
            ["40RF"] = (4, [75m, 150m, 225m]),
        };

        var types = await db.ContainerTypes.ToListAsync(cancellationToken);
        foreach (var type in types.Where(t => plan.ContainsKey(t.Code)))
        {
            var (free, rates) = plan[type.Code];
            db.DetentionTariffs.Add(new DetentionTariff
            {
                ContainerTypeId = type.Id,
                FreeDays = free,
                ValidFrom = new DateOnly(2026, 1, 1),
                Slabs =
                [
                    new() { FromDay = free + 1, ToDay = free + 5, RatePerDayUsd = rates[0] },
                    new() { FromDay = free + 6, ToDay = free + 10, RatePerDayUsd = rates[1] },
                    new() { FromDay = free + 11, ToDay = null, RatePerDayUsd = rates[2] },
                ],
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Three fictional vessels, each with two upcoming voyages on the Asia to Europe lane, so schedules have something to show.</summary>
    private async Task SeedVoyagesAsync(CancellationToken cancellationToken)
    {
        if (await db.Vessels.AnyAsync(cancellationToken))
        {
            return;
        }

        var ports = await db.Ports.ToDictionaryAsync(p => p.UnLocode, p => p.Id, cancellationToken);
        if (new[] { "CNSHA", "SGSIN", "INNSA", "AEJEA", "NLRTM", "DEHAM" }.Any(code => !ports.ContainsKey(code)))
        {
            return;
        }

        var names = new[] { "MV BoxLine Aurora", "MV BoxLine Meridian", "MV BoxLine Zephyr" };
        var vessels = names
            .Select((name, i) => new Vessel { Name = name, ImoNumber = ImoNumbers.WithCheckDigit(990001 + i) })
            .ToList();
        db.Vessels.AddRange(vessels);
        await db.SaveChangesAsync(cancellationToken);

        // Shanghai - Singapore - Nhava Sheva - Jebel Ali - Rotterdam - Hamburg; each leg takes a few days.
        string[] lane = ["CNSHA", "SGSIN", "INNSA", "AEJEA", "NLRTM", "DEHAM"];
        double[] legDays = [6, 7, 4, 17, 2];
        var first = DateTime.UtcNow.Date.AddDays(3).AddHours(8);

        for (var v = 0; v < vessels.Count; v++)
        {
            for (var round = 0; round < 2; round++)
            {
                var departure = first.AddDays((v * 7) + (round * 28));
                var calls = new List<VoyageCall>();
                var clock = departure;
                for (var i = 0; i < lane.Length; i++)
                {
                    var arrival = i == 0 ? (DateTime?)null : clock;
                    DateTime? departureAt = i == lane.Length - 1 ? null : (i == 0 ? clock : clock.AddHours(14));
                    calls.Add(new VoyageCall { PortId = ports[lane[i]], Sequence = i + 1, Eta = arrival, Etd = departureAt });
                    if (i < legDays.Length)
                    {
                        clock = (departureAt ?? clock).AddDays(legDays[i]);
                    }
                }

                db.Voyages.Add(new Voyage
                {
                    VesselId = vessels[v].Id,
                    VoyageNumber = $"BXL{departure:yyMM}{(char)('A' + v)}{round + 1}",
                    Status = VoyageStatus.Scheduled,
                    Calls = calls,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Generates deterministic containers with valid ISO 6346 check digits.</summary>
    internal static IEnumerable<Container> GenerateContainers(IReadOnlyList<int> typeIds, int count)
    {
        var random = new Random(6346);
        var used = new HashSet<string>();
        var statuses = new[]
        {
            ContainerStatus.Available, ContainerStatus.Available, ContainerStatus.Available,
            ContainerStatus.InUse, ContainerStatus.InUse, ContainerStatus.UnderRepair,
        };

        while (used.Count < count)
        {
            var owned = random.NextDouble() < 0.4;
            var prefixes = owned ? OwnedPrefixes : LeasedPrefixes;
            var body = $"{prefixes[random.Next(prefixes.Length)]}{random.Next(0, 1_000_000):D6}";
            var number = body + Iso6346Validator.ComputeCheckDigit(body);

            if (!used.Add(number))
            {
                continue;
            }

            yield return new Container
            {
                Number = number,
                ContainerTypeId = typeIds[random.Next(typeIds.Count)],
                OwnershipType = owned ? OwnershipType.Owned : OwnershipType.Leased,
                Status = statuses[random.Next(statuses.Length)],
            };
        }
    }
}

internal static class ImoNumbers
{
    /// <summary>Builds a valid seven-digit IMO number from its first six digits.</summary>
    public static string WithCheckDigit(int firstSix) => $"{firstSix}{ImoNumber.ComputeCheckDigit(firstSix.ToString())}";
}
