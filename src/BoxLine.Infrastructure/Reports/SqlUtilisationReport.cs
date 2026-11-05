using System.Data;
using BoxLine.Core.Reports;
using BoxLine.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Reports;

/// <summary>
/// The utilisation report computed inside SQL Server by <c>usp_UtilisationByMonthAndPort</c>: the grouping, counting and
/// paging all happen in the database, backed by the covering index <c>IX_ContainerMovements_Report</c>, and only the
/// requested page and the monthly totals come back.
/// </summary>
public sealed class SqlUtilisationReport(BoxLineDbContext db) : IUtilisationReport
{
    public const string ProcedureName = "dbo.usp_UtilisationByMonthAndPort";

    private sealed record SummaryRow(int TotalRows, int FleetSize);

    private sealed record PageRow(DateTime Month, string Location, string? PortName, int Movements, int Containers, int Loaded, int Discharged);

    private sealed record MonthRow(DateTime Month, int Movements, int ActiveContainers);

    public async Task<RawUtilisation> RunAsync(UtilisationQuery query, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var parameters = new DynamicParameters();
            parameters.Add("FromMonth", query.FromMonth?.ToDateTime(TimeOnly.MinValue), DbType.Date);
            parameters.Add("ToMonth", query.ToMonth?.ToDateTime(TimeOnly.MinValue), DbType.Date);
            parameters.Add("Location", query.Location, DbType.AnsiStringFixedLength, size: 5);
            parameters.Add("Page", query.Page, DbType.Int32);
            parameters.Add("PageSize", query.PageSize, DbType.Int32);

            await using var grid = await connection.QueryMultipleAsync(
                new CommandDefinition(ProcedureName, parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));

            var summary = await grid.ReadSingleAsync<SummaryRow>();
            var rows = (await grid.ReadAsync<PageRow>())
                .Select(r => new RawUtilisationRow(DateOnly.FromDateTime(r.Month), r.Location, r.PortName, r.Movements, r.Containers, r.Loaded, r.Discharged))
                .ToList();
            var monthly = (await grid.ReadAsync<MonthRow>())
                .Select(m => new RawMonthly(DateOnly.FromDateTime(m.Month), m.Movements, m.ActiveContainers))
                .ToList();

            return new RawUtilisation(rows, summary.TotalRows, summary.FleetSize, monthly);
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
