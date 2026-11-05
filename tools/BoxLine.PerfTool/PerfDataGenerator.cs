using BoxLine.Core.Containers;
using BoxLine.Core.Entities;

namespace BoxLine.PerfTool;

public sealed record PerfContainer(string Number, int TypeIndex, OwnershipType Ownership, ContainerStatus Status);

/// <summary>One generated movement; <see cref="ContainerIndex"/> points into the list returned by <see cref="PerfDataGenerator.Containers"/>.</summary>
public sealed record PerfMovement(int ContainerIndex, MovementEventType EventType, string Location, DateTime OccurredAt);

/// <summary>
/// Generates a large, realistic, repeatable data set. Every container goes through whole life-cycles (gate out empty,
/// stuffed, gate in full, loaded, discharged, gate out full, delivered, empty returned) that obey the same state machine
/// the application enforces, with the next cycle starting where the previous one ended. Locations are skewed so that a few big
/// ports see most of the traffic, as in real life. The same seed always gives the same data.
/// </summary>
public static class PerfDataGenerator
{
    /// <summary>The first seven are the seeded ports; the rest are other ports and inland depots. Earlier entries are busier.</summary>
    public static readonly IReadOnlyList<string> Locations =
    [
        "CNSHA", "SGSIN", "INNSA", "AEJEA", "NLRTM", "DEHAM", "INMUN",
        "USNYC", "GBFXT", "BEANR", "LKCMB", "MYPKG", "KRPUS", "ESVLC", "ITGOA", "THLCH",
        "INTKD", "NLUTC", "DEFRA", "INBLR",
    ];

    /// <summary>Owner codes for the generated fleet (fictional; category letter U).</summary>
    private static readonly string[] Prefixes = ["PFAU", "PFBU", "PFCU", "PFDU"];

    private const int CyclesPerContainerMin = 2;
    private const int CyclesPerContainerMax = 3;
    private const int EventsPerCycle = 8;
    private const int LongestCycleDays = 70;
    private const int LongestGapDays = 30;

    /// <summary>The fleet: <paramref name="count"/> containers with valid, unique ISO 6346 numbers.</summary>
    public static IReadOnlyList<PerfContainer> Containers(int count, int typeCount, int seed)
    {
        var random = new Random(seed);
        var containers = new List<PerfContainer>(count);
        for (var i = 0; i < count; i++)
        {
            var body = $"{Prefixes[i % Prefixes.Length]}{i / Prefixes.Length:D6}";
            containers.Add(new PerfContainer(
                body + Iso6346Validator.ComputeCheckDigit(body),
                random.Next(typeCount),
                random.NextDouble() < 0.4 ? OwnershipType.Owned : OwnershipType.Leased,
                random.NextDouble() < 0.7 ? ContainerStatus.Available : ContainerStatus.InUse));
        }

        return containers;
    }

    /// <summary>
    /// Exactly <paramref name="total"/> movements (the very last cycle may be cut short, which is still a valid prefix of
    /// the cycle) spread over the <paramref name="spanDays"/> days before <paramref name="endUtc"/>.
    /// </summary>
    public static IEnumerable<PerfMovement> Movements(int containerCount, int total, DateTime endUtc, int spanDays, int seed)
    {
        // A container's cycles are built first, then the whole run is slid to a random place that still ends inside the window,
        // so the history reaches right up to the end date instead of stopping a worst-case cycle short of it.
        var worstCase = CyclesPerContainerMax * (LongestCycleDays + LongestGapDays);
        if (spanDays <= worstCase)
        {
            throw new ArgumentOutOfRangeException(nameof(spanDays), $"The span must exceed {worstCase} days.");
        }

        // The share of containers that get a third cycle, set so the average slightly overshoots what is needed.
        var wantedCycles = total / (double)EventsPerCycle;
        var averageNeeded = Math.Min(CyclesPerContainerMax, (wantedCycles / containerCount) * 1.03);
        var thirdCycleShare = Math.Clamp(averageNeeded - CyclesPerContainerMin, 0, 1);
        var cumulative = CumulativeLocationWeights();
        var start = endUtc.AddDays(-spanDays);
        var emitted = 0;

        // Walk the containers in a scrambled order so that, if the quota runs out early, the idle ones are scattered.
        var stride = CoprimeStride(containerCount);
        for (var step = 0; step < containerCount && emitted < total; step++)
        {
            var index = (int)((long)step * stride % containerCount);
            var random = new Random(unchecked((seed * 1_000_003) + index));
            var cycles = CyclesPerContainerMin + (random.NextDouble() < thirdCycleShare ? 1 : 0);

            var events = new List<PerfMovement>(cycles * EventsPerCycle);
            var clock = start;
            var location = PickLocation(random, cumulative);
            for (var cycle = 0; cycle < cycles; cycle++)
            {
                var destination = PickLocation(random, cumulative, exclude: location);
                var (cycleEvents, finished) = OneCycle(index, location, destination, clock, random);
                events.AddRange(cycleEvents);
                clock = finished.AddDays(1 + random.NextDouble() * (LongestGapDays - 1));
                location = destination;
            }

            var length = events[^1].OccurredAt - events[0].OccurredAt;
            var room = (endUtc - start) - length;
            var shift = TimeSpan.FromTicks((long)(random.NextDouble() * Math.Max(0, room.Ticks)));

            foreach (var movement in events)
            {
                if (emitted == total)
                {
                    yield break;
                }

                emitted++;
                yield return movement with { OccurredAt = movement.OccurredAt + shift };
            }
        }

        if (emitted < total)
        {
            throw new InvalidOperationException($"Only {emitted:N0} of {total:N0} movements could be generated; use more containers.");
        }
    }

    /// <summary>One whole life-cycle starting at <paramref name="start"/>; also returns the time of its last event.</summary>
    private static (List<PerfMovement> Events, DateTime End) OneCycle(int container, string origin, string destination, DateTime start, Random random)
    {
        var events = new List<PerfMovement>(EventsPerCycle);
        var clock = start;

        void Add(MovementEventType type, string place, double minDays, double maxDays)
        {
            clock = clock.AddDays(minDays + (random.NextDouble() * (maxDays - minDays)));
            events.Add(new PerfMovement(container, type, place, clock));
        }

        Add(MovementEventType.GateOutEmpty, origin, 0, 0);
        Add(MovementEventType.Stuffed, origin, 0.5, 2);
        Add(MovementEventType.GateInFull, origin, 0.5, 2);
        Add(MovementEventType.LoadedOnVessel, origin, 1, 4);
        Add(MovementEventType.Discharged, destination, 8, 40);
        Add(MovementEventType.GateOutFull, destination, 0.5, 3);
        Add(MovementEventType.DeliveredToConsignee, destination, 0.5, 2);
        Add(MovementEventType.EmptyReturned, destination, 2, 15);

        return (events, clock);
    }

    private static double[] CumulativeLocationWeights()
    {
        var weights = Locations.Select((_, i) => 1.0 / (i + 1)).ToArray();
        var total = weights.Sum();
        var running = 0.0;
        return weights.Select(w => running += w / total).ToArray();
    }

    private static string PickLocation(Random random, double[] cumulative, string? exclude = null)
    {
        while (true)
        {
            var roll = random.NextDouble();
            var index = Array.FindIndex(cumulative, c => roll <= c);
            var location = Locations[index < 0 ? cumulative.Length - 1 : index];
            if (location != exclude)
            {
                return location;
            }
        }
    }

    /// <summary>A number that shares no factor with <paramref name="count"/>, so stepping by it visits every index once.</summary>
    private static int CoprimeStride(int count)
    {
        var stride = (int)(count * 0.6180339887) | 1;
        while (Gcd(stride, count) != 1)
        {
            stride += 2;
        }

        return stride;
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}
