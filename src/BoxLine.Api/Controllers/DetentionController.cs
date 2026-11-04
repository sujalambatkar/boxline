using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Detention;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Detention and demurrage pricing.</summary>
[ApiController]
[Authorize]
[Route("api/detention")]
public sealed class DetentionController(DetentionService detention) : ControllerBase
{
    /// <summary>
    /// Prices a period for a container type using the tariff valid on the out date. Days are calendar days with
    /// both ends included. With <c>countWeekendsAsChargeable</c> false, Saturdays and Sundays are skipped entirely.
    /// </summary>
    [HttpPost("calculate")]
    public async Task<ActionResult<DetentionResponse>> Calculate(CalculateRequest request, CancellationToken ct)
    {
        var quote = await detention.CalculateAsync(
            new CalculateCommand(request.ContainerTypeId, request.OutDate, request.ReturnDate, request.CountWeekendsAsChargeable, request.Engine), ct);
        return DetentionResponse.From(quote);
    }

    /// <summary>
    /// Prices a container's current or latest detention period from its movement history: from gate out full
    /// (or delivery) to empty returned, or up to today while it is still out. Shippers can only price containers
    /// that travelled under their own bookings.
    /// </summary>
    /// <param name="number">ISO 6346 container number.</param>
    /// <param name="bookingNumber">Limit the clock to this booking's gate out.</param>
    [HttpGet("containers/{number}")]
    public async Task<ActionResult<DetentionResponse>> ForContainer(
        string number, [FromQuery] string? bookingNumber, [FromQuery] bool countWeekendsAsChargeable = true, CancellationToken ct = default)
    {
        var quote = await detention.CalculateForContainerAsync(User.ToActor(), number, bookingNumber, countWeekendsAsChargeable, ct);
        return DetentionResponse.From(quote);
    }
}
