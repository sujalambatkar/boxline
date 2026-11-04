using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Common;
using BoxLine.Core.Detention;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BoxLine.Tests.Api;

/// <summary>
/// Proves that the C# calculator and the stored procedure <c>usp_CalculateDetention</c> are the same calculation:
/// identical breakdowns for the same inputs, and the same errors, against the real SQL Server.
/// </summary>
[Collection(ApiCollection.Name)]
public class DetentionParityTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Shape(string Name, int FreeDays, SlabRequest[] Slabs, DateOnly? ValidTo = null);

    private static readonly Shape[] Shapes =
    [
        new("standard", 5, TestData.StandardSlabs),
        new("no free days", 0, [new(1, 3, 10m), new(4, null, 25m)]),
        new("single open slab with cents", 2, [new(3, null, 33.33m)]),
        new("many slabs", 3, [new(4, 5, 7.5m), new(6, 9, 15m), new(10, 14, 22.25m), new(15, 30, 60m), new(31, null, 199.99m)]),
    ];

    private async Task<(DetentionTariff Tariff, TariffResponse Api)> CreateAsync(Shape shape, DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        var api = await TestData.NewTariffAsync(ops, type.Id, shape.FreeDays, shape.Slabs, validFrom, validTo);

        using var scope = factory.Services.CreateScope();
        var tariff = (await scope.ServiceProvider.GetRequiredService<IDetentionTariffRepository>().GetReadOnlyAsync(api.Id, CancellationToken.None))!;
        return (tariff, api);
    }

    private async Task<DetentionBreakdown> InSqlAsync(int tariffId, DateOnly from, DateOnly to, bool weekends)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDetentionSqlCalculator>().CalculateAsync(tariffId, from, to, weekends, CancellationToken.None);
    }

    private static void AssertIdentical(DetentionBreakdown fromSql, DetentionBreakdown fromCSharp, string because)
    {
        fromSql.TotalDays.Should().Be(fromCSharp.TotalDays, because);
        fromSql.CountedDays.Should().Be(fromCSharp.CountedDays, because);
        fromSql.WeekendDaysSkipped.Should().Be(fromCSharp.WeekendDaysSkipped, because);
        fromSql.FreeDaysUsed.Should().Be(fromCSharp.FreeDaysUsed, because);
        fromSql.ChargeableDays.Should().Be(fromCSharp.ChargeableDays, because);
        fromSql.TotalAmountUsd.Should().Be(fromCSharp.TotalAmountUsd, because);
        fromSql.Slabs.Should().Equal(fromCSharp.Slabs, because);
    }

    [Fact]
    public async Task EveryStartWeekday_EveryLengthUpToFortyFiveDays_WithAndWithoutWeekends_GivesIdenticalResults()
    {
        var (tariff, _) = await CreateAsync(Shapes[0]);
        var monday = new DateOnly(2026, 11, 2);
        var cases = 0;

        for (var startOffset = 0; startOffset < 7; startOffset++)
        {
            var start = monday.AddDays(startOffset);
            for (var length = 1; length <= 45; length++)
            {
                var end = start.AddDays(length - 1);
                foreach (var weekends in new[] { true, false })
                {
                    var expected = DetentionCalculator.Calculate(start, end, tariff, weekends);
                    var actual = await InSqlAsync(tariff.Id, start, end, weekends);

                    AssertIdentical(actual, expected, $"{start:ddd yyyy-MM-dd} for {length} days, weekends {weekends}");
                    cases++;
                }
            }
        }

        cases.Should().Be(7 * 45 * 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DifferentTariffShapes_GiveIdenticalResults(int shapeIndex)
    {
        var shape = Shapes[shapeIndex];
        var (tariff, _) = await CreateAsync(shape);
        var random = new Random(shapeIndex + 100);

        for (var i = 0; i < 120; i++)
        {
            var start = new DateOnly(2024, 1, 1).AddDays(random.Next(0, 1500));
            var end = start.AddDays(random.Next(0, 90));
            var weekends = random.Next(2) == 0;

            var expected = DetentionCalculator.Calculate(start, end, tariff, weekends);
            var actual = await InSqlAsync(tariff.Id, start, end, weekends);

            AssertIdentical(actual, expected, $"{shape.Name}: {start} to {end}, weekends {weekends}");
        }
    }

    [Theory]
    [InlineData(150)]
    [InlineData(400)]
    [InlineData(1000)]
    public async Task VeryLongStays_GiveIdenticalResults(int days)
    {
        var (tariff, _) = await CreateAsync(Shapes[0]);
        var start = new DateOnly(2026, 3, 14);
        var end = start.AddDays(days - 1);

        foreach (var weekends in new[] { true, false })
        {
            AssertIdentical(
                await InSqlAsync(tariff.Id, start, end, weekends),
                DetentionCalculator.Calculate(start, end, tariff, weekends),
                $"{days} days, weekends {weekends}");
        }
    }

    [Fact]
    public async Task BothEnginesAgreeWithTheIndependentFormula()
    {
        var (tariff, api) = await CreateAsync(Shapes[3]);
        var start = new DateOnly(2026, 11, 6);
        var end = new DateOnly(2027, 1, 20);

        foreach (var weekends in new[] { true, false })
        {
            var expected = TestData.ExpectedAmount(start, end, api.FreeDays, Shapes[3].Slabs, weekends);

            (await InSqlAsync(tariff.Id, start, end, weekends)).TotalAmountUsd.Should().Be(expected);
            DetentionCalculator.Calculate(start, end, tariff, weekends).TotalAmountUsd.Should().Be(expected);
        }
    }

    [Fact]
    public async Task ASingleDayStay_AndAWeekendOnlyStay_GiveIdenticalResults()
    {
        var (tariff, _) = await CreateAsync(Shapes[1]);
        var saturday = new DateOnly(2026, 11, 7);

        foreach (var (from, to, weekends) in new[] { (saturday, saturday, true), (saturday, saturday, false), (saturday, saturday.AddDays(1), false), (saturday, saturday.AddDays(2), false) })
        {
            AssertIdentical(
                await InSqlAsync(tariff.Id, from, to, weekends),
                DetentionCalculator.Calculate(from, to, tariff, weekends),
                $"{from} to {to}, weekends {weekends}");
        }
    }

    // ---- errors: same kind, same message ----------------------------------------------------------

    private static async Task<DomainValidationException> Capture(Func<Task> action)
    {
        var thrown = await Record.ExceptionAsync(action);
        return thrown.Should().BeOfType<DomainValidationException>().Subject;
    }

    [Fact]
    public async Task AReturnBeforeTheOutDate_IsRejectedIdentically()
    {
        var (tariff, _) = await CreateAsync(Shapes[0]);
        var start = new DateOnly(2026, 11, 10);

        var fromCSharp = await Capture(() => Task.Run(() => DetentionCalculator.Calculate(start, start.AddDays(-1), tariff, true)));
        var fromSql = await Capture(() => InSqlAsync(tariff.Id, start, start.AddDays(-1), true));

        fromSql.Errors.Should().BeEquivalentTo(fromCSharp.Errors);
        fromSql.Errors["ReturnDate"].Single().Should().Be("Return date cannot be before the out date.");
    }

    [Fact]
    public async Task ATariffThatIsNotValidOnTheOutDate_IsRejectedIdentically_AndValidityIsInclusive()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);
        var (tariff, _) = await CreateAsync(Shapes[0], from, to);

        foreach (var outDate in new[] { from.AddDays(-1), to.AddDays(1), new DateOnly(2030, 1, 1) })
        {
            var fromCSharp = await Capture(() => Task.Run(() => DetentionCalculator.Calculate(outDate, outDate.AddDays(3), tariff, true)));
            var fromSql = await Capture(() => InSqlAsync(tariff.Id, outDate, outDate.AddDays(3), true));

            fromSql.Errors.Should().BeEquivalentTo(fromCSharp.Errors, $"out date {outDate}");
            fromSql.Errors["OutDate"].Single().Should().Be($"The tariff is not valid on {outDate:yyyy-MM-dd}.");
        }

        foreach (var outDate in new[] { from, to })
        {
            AssertIdentical(
                await InSqlAsync(tariff.Id, outDate, outDate.AddDays(9), true),
                DetentionCalculator.Calculate(outDate, outDate.AddDays(9), tariff, true),
                $"boundary {outDate}");
        }
    }

    [Fact]
    public async Task ValidityIsJudgedOnTheOutDateInBothEngines()
    {
        var (tariff, _) = await CreateAsync(Shapes[0], validTo: new DateOnly(2026, 6, 30));
        var start = new DateOnly(2026, 6, 29);
        var end = start.AddDays(20);

        AssertIdentical(await InSqlAsync(tariff.Id, start, end, true), DetentionCalculator.Calculate(start, end, tariff, true), "runs past the end date");
    }

    [Fact]
    public async Task AnUnknownTariff_IsReportedAsNotFoundBySql()
    {
        var thrown = await Record.ExceptionAsync(() => InSqlAsync(999_999, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 5), true));

        thrown.Should().BeOfType<NotFoundException>().Which.Message.Should().Be("Tariff was not found.");
    }

    // ---- through the HTTP API ---------------------------------------------------------------------

    [Fact]
    public async Task TheApi_GivesTheSameAnswerForEitherEngine()
    {
        var type = await TestData.NewContainerTypeAsync(ops);
        await TestData.NewTariffAsync(ops, type.Id);

        foreach (var (from, to, weekends) in new[]
        {
            (new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 17), true),
            (new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 16), false),
            (new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 8), false),
            (new DateOnly(2026, 1, 5), new DateOnly(2027, 2, 5), false),
        })
        {
            async Task<DetentionResponse> Ask(DetentionEngine engine)
            {
                var response = await ops.PostAsJsonAsync("/api/detention/calculate", new CalculateRequest(type.Id, from, to, weekends, engine));
                response.EnsureSuccessStatusCode();
                return (await response.Content.ReadFromJsonAsync<DetentionResponse>(TestJson.Options))!;
            }

            var csharp = await Ask(DetentionEngine.CSharp);
            var sql = await Ask(DetentionEngine.Sql);

            sql.Should().BeEquivalentTo(csharp, o => o.WithStrictOrdering(), $"{from} to {to}, weekends {weekends}");
        }
    }
}
