using BoxLine.Core.Reports;

namespace BoxLine.Api.Contracts;

public sealed record UtilisationRowResponse(
    DateOnly Month,
    string Location,
    string? PortName,
    int Movements,
    int Containers,
    int Loaded,
    int Discharged,
    decimal UtilisationPercent)
{
    public static UtilisationRowResponse From(UtilisationRow row) =>
        new(row.Month, row.Location, row.PortName, row.Movements, row.Containers, row.Loaded, row.Discharged, row.UtilisationPercent);
}

public sealed record MonthlyUtilisationResponse(DateOnly Month, int Movements, int ActiveContainers, decimal UtilisationPercent)
{
    public static MonthlyUtilisationResponse From(MonthlyUtilisation month) =>
        new(month.Month, month.Movements, month.ActiveContainers, month.UtilisationPercent);
}

/// <param name="ElapsedMilliseconds">How long the report took to produce on the server, so its speed is visible to the user.</param>
public sealed record UtilisationResponse(
    IReadOnlyList<UtilisationRowResponse> Rows,
    int TotalRows,
    int Page,
    int PageSize,
    int FleetSize,
    IReadOnlyList<MonthlyUtilisationResponse> Monthly,
    long ElapsedMilliseconds)
{
    public static UtilisationResponse From(UtilisationResult result, long elapsedMilliseconds) => new(
        result.Rows.Select(UtilisationRowResponse.From).ToList(),
        result.TotalRows,
        result.Page,
        result.PageSize,
        result.FleetSize,
        result.Monthly.Select(MonthlyUtilisationResponse.From).ToList(),
        elapsedMilliseconds);
}
