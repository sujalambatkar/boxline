using BoxLine.Api.Contracts;
using BoxLine.Core.Voyages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Sailing search, open to every signed-in user, used to choose a voyage when booking.</summary>
[ApiController]
[Authorize]
[Route("api/schedules")]
public sealed class SchedulesController(ScheduleService schedules) : ControllerBase
{
    /// <summary>
    /// Open sailings from the loading port to the discharge port (loading before discharge), earliest departure
    /// first. The window defaults to the next 90 days.
    /// </summary>
    /// <param name="departFrom">First departure day, inclusive (yyyy-MM-dd, UTC).</param>
    /// <param name="departTo">Last departure day, inclusive (yyyy-MM-dd, UTC).</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ScheduleResponse>>> Search(
        [FromQuery] int polId,
        [FromQuery] int podId,
        [FromQuery] DateOnly? departFrom,
        [FromQuery] DateOnly? departTo,
        CancellationToken ct)
    {
        var options = await schedules.SearchAsync(new ScheduleQuery(polId, podId, departFrom, departTo), ct);
        return options.Select(ScheduleResponse.From).ToList();
    }
}
