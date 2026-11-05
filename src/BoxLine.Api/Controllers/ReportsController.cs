using System.Diagnostics;
using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Operational reports. Operations staff only.</summary>
[ApiController]
[Authorize(Roles = Roles.Ops)]
[Route("api/reports")]
public sealed class ReportsController(UtilisationReportService utilisation) : ControllerBase
{
    /// <summary>
    /// Container utilisation by month and location: for each month and place, the movements, the different containers
    /// that moved there and how much of the whole fleet that is. Newest month first, paged on the server, plus
    /// per-month totals for charting. Without a month range it covers the latest twelve months.
    /// </summary>
    /// <param name="fromMonth">Any day in the first month wanted.</param>
    /// <param name="toMonth">Any day in the last month wanted.</param>
    /// <param name="location">A UN/LOCODE to limit the report to one place.</param>
    [HttpGet("utilisation")]
    public async Task<ActionResult<UtilisationResponse>> Utilisation(
        [FromQuery] DateOnly? fromMonth,
        [FromQuery] DateOnly? toMonth,
        [FromQuery] string? location,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var result = await utilisation.RunAsync(new UtilisationQuery(fromMonth, toMonth, location, page, pageSize), ct);
        watch.Stop();

        return UtilisationResponse.From(result, watch.ElapsedMilliseconds);
    }
}
