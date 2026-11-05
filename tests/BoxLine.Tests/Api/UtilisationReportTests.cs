using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Reports;
using BoxLine.Infrastructure.Persistence;
using BoxLine.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoxLine.Tests.Api;

/// <summary>
/// The optimised SQL report must give exactly the numbers of the naive one. The data is inserted into far-past years that no
/// other test touches, with counts worked out by hand, and removed afterwards.
/// </summary>
[Collection(ApiCollection.Name)]
public class UtilisationReportTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Spec(int Container, MovementEventType Type, string Location, DateTime At);

    /// <summary>Inserts the movements for four brand-new containers, runs the test body, then removes everything it inserted.</summary>
    private async Task WithDatasetAsync(IReadOnlyList<Spec> specs, Func<Func<IServiceScope>, Task> body)
    {
        var ids = new List<int>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BoxLineDbContext>();
            var typeId = await db.ContainerTypes.Select(t => t.Id).FirstAsync();
            var userId = await db.Users.Where(u => u.Role == UserRole.Ops).Select(u => u.Id).FirstAsync();

            // These numbers only need to be unique; they never go through the ISO 6346 validator.
            var containers = Enumerable.Range(1, specs.Max(s => s.Container))
                .Select(i => new Container { Number = $"RPT{i}{Guid.NewGuid():N}"[..11].ToUpperInvariant(), ContainerTypeId = typeId })
                .ToList();

            db.Containers.AddRange(containers);
            await db.SaveChangesAsync();
            ids.AddRange(containers.Select(c => c.Id));

            db.ContainerMovements.AddRange(specs.Select(s => new ContainerMovement
            {
                ContainerId = containers[s.Container - 1].Id,
                EventType = s.Type,
                LocationUnLocode = s.Location,
                OccurredAt = s.At,
                RecordedByUserId = userId,
                RecordedAt = s.At,
            }));
            await db.SaveChangesAsync();
        }

        try
        {
            await body(factory.Services.CreateScope);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BoxLineDbContext>();
            await db.ContainerMovements.Where(m => ids.Contains(m.ContainerId)).ExecuteDeleteAsync();
            await db.Containers.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
        }
    }

    private static DateTime At(int year, int month, int day = 15, int hour = 12, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static async Task<(RawUtilisation Naive, RawUtilisation Sql)> RunBothAsync(Func<IServiceScope> scopes, UtilisationQuery query)
    {
        // The EF version that groups on the server is checked against the oracle on every call, so all tests cover it too.
        using (var efScope = scopes())
        {
            var grouped = await new EfGroupedUtilisationReport(efScope.ServiceProvider.GetRequiredService<BoxLineDbContext>()).RunAsync(query, default);
            using var oracleScope = scopes();
            var oracle = await new NaiveUtilisationReport(oracleScope.ServiceProvider.GetRequiredService<BoxLineDbContext>()).RunAsync(query, default);
            grouped.Should().BeEquivalentTo(oracle, o => o.WithStrictOrdering(), $"EF grouped vs naive for {query}");
        }

        using var naiveScope = scopes();
        var naive = await new NaiveUtilisationReport(naiveScope.ServiceProvider.GetRequiredService<BoxLineDbContext>()).RunAsync(query, default);

        using var sqlScope = scopes();
        var sql = await new SqlUtilisationReport(sqlScope.ServiceProvider.GetRequiredService<BoxLineDbContext>()).RunAsync(query, default);
        return (naive, sql);
    }

    /// <summary>Containers 1-4. March and April of one year at a known port, an unknown port and an unknown inland depot.</summary>
    private static List<Spec> Dataset(int year) =>
    [
        // March: INNSA 4 movements, 3 containers (1 twice), 2 loaded, 1 discharged.
        new(1, MovementEventType.LoadedOnVessel, "INNSA", At(year, 3, 2)),
        new(1, MovementEventType.Stuffed, "INNSA", At(year, 3, 3)),
        new(2, MovementEventType.LoadedOnVessel, "INNSA", At(year, 3, 10)),
        new(3, MovementEventType.Discharged, "INNSA", At(year, 3, 20)),
        // March: ZZTST (not a port) 2 movements, 1 container, 1 discharged.
        new(1, MovementEventType.Discharged, "ZZTST", At(year, 3, 25)),
        new(1, MovementEventType.GateOutFull, "ZZTST", At(year, 3, 26)),
        // April: INNSA 5 movements, 3 containers, 1 loaded, 2 discharged.
        new(2, MovementEventType.Discharged, "INNSA", At(year, 4, 1)),
        new(4, MovementEventType.Discharged, "INNSA", At(year, 4, 2)),
        new(4, MovementEventType.GateOutFull, "INNSA", At(year, 4, 3)),
        new(2, MovementEventType.EmptyReturned, "INNSA", At(year, 4, 9)),
        new(1, MovementEventType.LoadedOnVessel, "INNSA", At(year, 4, 28)),
        // April: ZZAAA 1 movement.
        new(3, MovementEventType.GateOutEmpty, "ZZAAA", At(year, 4, 30)),
    ];

    private static UtilisationQuery Whole(int year, int page = 1, int pageSize = 50) =>
        new(new DateOnly(year, 1, 1), new DateOnly(year, 12, 1), null, page, pageSize);

    [Fact]
    public async Task BothImplementations_GiveTheHandComputedCounts()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async scopes =>
        {
            var (naive, sql) = await RunBothAsync(scopes, Whole(year));

            foreach (var result in new[] { naive, sql })
            {
                result.TotalRows.Should().Be(4);
                result.Rows.Select(r => (r.Month.Month, r.Location, r.Movements, r.Containers, r.Loaded, r.Discharged)).Should().Equal(
                    (4, "INNSA", 5, 3, 1, 2),
                    (4, "ZZAAA", 1, 1, 0, 0),
                    (3, "INNSA", 4, 3, 2, 1),
                    (3, "ZZTST", 2, 1, 0, 1));
                result.Monthly.Select(m => (m.Month.Month, m.Movements, m.ActiveContainers)).Should().Equal((3, 6, 3), (4, 6, 4));
            }
        });
    }

    [Fact]
    public async Task TheyAgreeOnEveryFilterAndPage()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async scopes =>
        {
            var queries = new[]
            {
                Whole(year),
                Whole(year, page: 1, pageSize: 2),
                Whole(year, page: 2, pageSize: 2),
                Whole(year, page: 3, pageSize: 2),
                new UtilisationQuery(new DateOnly(year, 4, 1), new DateOnly(year, 4, 1), null, 1, 50),
                new UtilisationQuery(new DateOnly(year, 3, 1), new DateOnly(year, 3, 1), null, 1, 50),
                new UtilisationQuery(new DateOnly(year, 1, 1), new DateOnly(year, 12, 1), "INNSA", 1, 50),
                new UtilisationQuery(new DateOnly(year, 1, 1), new DateOnly(year, 12, 1), "ZZTST", 1, 50),
                new UtilisationQuery(new DateOnly(year, 1, 1), new DateOnly(year, 12, 1), "AAAAA", 1, 50),
                new UtilisationQuery(new DateOnly(year, 5, 1), new DateOnly(year, 12, 1), null, 1, 50),
                new UtilisationQuery(new DateOnly(year, 1, 1), null, null, 1, 50),
                new UtilisationQuery(null, new DateOnly(year, 12, 1), null, 1, 5),
            };

            foreach (var query in queries)
            {
                var (naive, sql) = await RunBothAsync(scopes, query);

                sql.Should().BeEquivalentTo(naive, o => o.WithStrictOrdering(), $"{query}");
            }
        });
    }

    [Fact]
    public async Task PagesAreOrderedNewestMonthFirstThenByLocation_AndTogetherCoverTheReport()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async scopes =>
        {
            var (_, first) = await RunBothAsync(scopes, Whole(year, 1, 3));
            var (_, second) = await RunBothAsync(scopes, Whole(year, 2, 3));
            var (_, beyond) = await RunBothAsync(scopes, Whole(year, 3, 3));

            first.Rows.Select(r => (r.Month.Month, r.Location)).Should().Equal((4, "INNSA"), (4, "ZZAAA"), (3, "INNSA"));
            second.Rows.Select(r => (r.Month.Month, r.Location)).Should().Equal((3, "ZZTST"));
            beyond.Rows.Should().BeEmpty();
            first.TotalRows.Should().Be(4).And.Be(second.TotalRows).And.Be(beyond.TotalRows);
            first.Monthly.Should().HaveCount(2, "the monthly totals cover the whole filter, not just the page");
        });
    }

    [Fact]
    public async Task ThePortNameComesFromTheRealPortsOnly()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async scopes =>
        {
            var (naive, sql) = await RunBothAsync(scopes, Whole(year));

            foreach (var result in new[] { naive, sql })
            {
                result.Rows.Single(r => r.Location == "INNSA" && r.Month.Month == 3).PortName.Should().StartWith("Nhava Sheva");
                result.Rows.Single(r => r.Location == "ZZTST").PortName.Should().BeNull("an unknown code is not a port");
            }
        });
    }

    [Fact]
    public async Task AnEmptyRange_GivesAnEmptyReport_WithTheFleetSizeStillReported()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async scopes =>
        {
            var (naive, sql) = await RunBothAsync(scopes, new UtilisationQuery(new DateOnly(1900, 1, 1), new DateOnly(1900, 12, 1)));

            foreach (var result in new[] { naive, sql })
            {
                result.Rows.Should().BeEmpty();
                result.TotalRows.Should().Be(0);
                result.Monthly.Should().BeEmpty();
                result.FleetSize.Should().BeGreaterThan(3);
            }

            sql.FleetSize.Should().Be(naive.FleetSize);
        });
    }

    [Fact]
    public async Task MonthAndYearBoundaries_PutMovementsInTheRightMonth()
    {
        var year = Random.Shared.Next(1950, 1990);
        var specs = new List<Spec>
        {
            new(1, MovementEventType.GateOutEmpty, "INNSA", At(year, 3, 31, 23, 59, 59)),
            new(2, MovementEventType.GateOutEmpty, "INNSA", At(year, 4, 1, 0, 0, 0)),
            new(3, MovementEventType.GateOutEmpty, "INNSA", At(year, 12, 31, 23, 59, 59)),
            new(4, MovementEventType.GateOutEmpty, "INNSA", At(year + 1, 1, 1, 0, 0, 0)),
        };

        await WithDatasetAsync(specs, async scopes =>
        {
            var (naive, sql) = await RunBothAsync(scopes, new UtilisationQuery(new DateOnly(year, 1, 1), new DateOnly(year + 1, 12, 1), null, 1, 50));

            foreach (var result in new[] { naive, sql })
            {
                result.Rows.Select(r => (r.Month.Year, r.Month.Month, r.Movements)).Should().Equal(
                    (year + 1, 1, 1), (year, 12, 1), (year, 4, 1), (year, 3, 1));
            }

            sql.Should().BeEquivalentTo(naive, o => o.WithStrictOrdering());
        });
    }

    [Fact]
    public async Task TheMonthlyActiveContainerCount_IsDistinctAcrossLocations()
    {
        var year = Random.Shared.Next(1950, 1990);
        var specs = new List<Spec>
        {
            // One container moving through three places in a month is one active container, but three place-rows.
            new(1, MovementEventType.LoadedOnVessel, "INNSA", At(year, 6, 1)),
            new(1, MovementEventType.Discharged, "AEJEA", At(year, 6, 10)),
            new(1, MovementEventType.GateOutFull, "AEJEA", At(year, 6, 12)),
            new(1, MovementEventType.EmptyReturned, "NLRTM", At(year, 6, 20)),
        };

        await WithDatasetAsync(specs, async scopes =>
        {
            var (naive, sql) = await RunBothAsync(scopes, Whole(year));

            foreach (var result in new[] { naive, sql })
            {
                result.Rows.Should().HaveCount(3).And.OnlyContain(r => r.Containers == 1);
                result.Monthly.Should().ContainSingle().Which.Should().Be(new RawMonthly(new DateOnly(year, 6, 1), 4, 1));
            }
        });
    }

    // ---- through the API --------------------------------------------------------------------------

    [Fact]
    public async Task TheEndpoint_ReturnsThePagedReportWithPercentagesAndTheTimeItTook()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async _ =>
        {
            var response = await ops.GetAsync($"/api/reports/utilisation?fromMonth={year}-01-01&toMonth={year}-12-31&pageSize=2");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var report = (await response.Content.ReadFromJsonAsync<UtilisationResponse>(TestJson.Options))!;
            report.TotalRows.Should().Be(4);
            report.Page.Should().Be(1);
            report.PageSize.Should().Be(2);
            report.Rows.Select(r => (r.Month.Month, r.Location)).Should().Equal((4, "INNSA"), (4, "ZZAAA"));
            report.FleetSize.Should().BeGreaterThan(3);
            report.Rows[0].UtilisationPercent.Should().Be(Utilisation.Percent(3, report.FleetSize));
            report.Monthly.Select(m => (m.Month.Month, m.ActiveContainers)).Should().Equal((3, 3), (4, 4));
            report.Monthly[1].UtilisationPercent.Should().Be(Utilisation.Percent(4, report.FleetSize));
            report.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        });
    }

    [Fact]
    public async Task TheEndpoint_FiltersByLocation_AndAcceptsAnyDayInAMonth()
    {
        var year = Random.Shared.Next(1950, 1990);

        await WithDatasetAsync(Dataset(year), async _ =>
        {
            var report = (await ops.GetFromJsonAsync<UtilisationResponse>(
                $"/api/reports/utilisation?fromMonth={year}-03-17&toMonth={year}-03-02&location=innsa", TestJson.Options))!;

            report.Rows.Should().ContainSingle().Which.Movements.Should().Be(4);
            report.Rows[0].Location.Should().Be("INNSA");
        });
    }

    [Fact]
    public async Task TheEndpoint_WithoutARange_CoversTheLatestTwelveMonths()
    {
        var report = (await ops.GetFromJsonAsync<UtilisationResponse>("/api/reports/utilisation", TestJson.Options))!;

        var thisMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        report.Rows.Should().OnlyContain(r => r.Month >= thisMonth.AddMonths(-11) && r.Month <= thisMonth);
        report.Monthly.Should().OnlyContain(m => m.Month >= thisMonth.AddMonths(-11) && m.Month <= thisMonth);
    }

    [Theory]
    [InlineData("fromMonth=2026-05-01&toMonth=2026-04-01", "ToMonth")]
    [InlineData("location=1NNSA", "Location")]
    [InlineData("page=0", "Page")]
    [InlineData("pageSize=0", "PageSize")]
    [InlineData("pageSize=201", "PageSize")]
    public async Task TheEndpoint_RejectsInvalidRequests(string query, string field)
    {
        var response = await ops.GetAsync($"/api/reports/utilisation?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>())!.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task TheEndpoint_IsForOpsOnly()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.GetAsync("/api/reports/utilisation")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await factory.CreateClient().GetAsync("/api/reports/utilisation")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
