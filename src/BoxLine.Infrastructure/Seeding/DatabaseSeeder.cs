using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
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
