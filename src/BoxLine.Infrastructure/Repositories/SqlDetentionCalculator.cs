using System.Data;
using BoxLine.Core.Common;
using BoxLine.Core.Detention;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

/// <summary>Runs the detention calculation inside SQL Server through <c>usp_CalculateDetention</c>.</summary>
public sealed class SqlDetentionCalculator(BoxLineDbContext db) : IDetentionSqlCalculator
{
    private const int ReturnBeforeOut = 50010;
    private const int TariffNotValid = 50011;
    private const int TariffNotFound = 50012;

    private sealed record SummaryRow(int TotalDays, int CountedDays, int WeekendDaysSkipped, int FreeDaysUsed, int ChargeableDays, decimal TotalAmountUsd);

    private sealed record SlabRow(int FromDay, int? ToDay, int Days, decimal RatePerDayUsd, decimal AmountUsd);

    public async Task<DetentionBreakdown> CalculateAsync(
        int tariffId, DateOnly outDate, DateOnly returnDate, bool countWeekendsAsChargeable, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var command = new CommandDefinition(
                "dbo.usp_CalculateDetention",
                new
                {
                    TariffId = tariffId,
                    OutDate = outDate.ToDateTime(TimeOnly.MinValue),
                    ReturnDate = returnDate.ToDateTime(TimeOnly.MinValue),
                    CountWeekends = countWeekendsAsChargeable,
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken);

            await using var grid = await connection.QueryMultipleAsync(command);
            var summary = await grid.ReadSingleAsync<SummaryRow>();
            var slabs = (await grid.ReadAsync<SlabRow>()).ToList();

            return new DetentionBreakdown(
                summary.TotalDays,
                summary.CountedDays,
                summary.WeekendDaysSkipped,
                summary.FreeDaysUsed,
                summary.ChargeableDays,
                slabs.Select(s => new SlabCharge(s.FromDay, s.ToDay, s.Days, s.RatePerDayUsd, s.AmountUsd)).ToList(),
                summary.TotalAmountUsd);
        }
        catch (SqlException ex) when (ex.Number == ReturnBeforeOut)
        {
            throw new DomainValidationException("ReturnDate", ex.Message);
        }
        catch (SqlException ex) when (ex.Number == TariffNotValid)
        {
            throw new DomainValidationException("OutDate", ex.Message);
        }
        catch (SqlException ex) when (ex.Number == TariffNotFound)
        {
            throw new NotFoundException(ex.Message);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }
}
