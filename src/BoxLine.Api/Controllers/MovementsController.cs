using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Containers;
using BoxLine.Core.Movements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Recording container movement events. Operations staff only.</summary>
[ApiController]
[Authorize(Roles = Roles.Ops)]
[Route("api/movements")]
public sealed class MovementsController(MovementService movements) : ControllerBase
{
    /// <summary>
    /// Records the next event in a container's life. Events must follow the cycle (gate out empty, stuffed,
    /// gate in full, loaded, discharged, gate out full, delivered, empty returned) and cannot be backdated.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MovementResponse>> Record(RecordMovementRequest request, CancellationToken ct)
    {
        var command = new RecordMovementCommand(
            request.ContainerNumber, request.EventType, request.LocationUnLocode, request.OccurredAt, request.BookingNumber, request.Remarks);

        var movement = await movements.RecordAsync(User.ToActor(), command, ct);
        var number = Iso6346Validator.Normalize(request.ContainerNumber);
        var booking = request.BookingNumber?.Trim().ToUpperInvariant();

        var response = MovementResponse.From(movement, number) with { BookingNumber = string.IsNullOrEmpty(booking) ? null : booking };
        return Created($"/api/tracking/containers/{number}", response);
    }
}
