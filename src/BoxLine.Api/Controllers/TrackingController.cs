using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Movements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Container and booking tracking. Shippers only see events of their own bookings.</summary>
[ApiController]
[Authorize]
[Route("api/tracking")]
public sealed class TrackingController(MovementService movements) : ControllerBase
{
    /// <summary>A container's timeline, oldest event first, plus which events may be recorded next (Ops only).</summary>
    [HttpGet("containers/{number}")]
    public async Task<ActionResult<ContainerTrackingResponse>> Container(string number, CancellationToken ct) =>
        ContainerTrackingResponse.From(await movements.GetContainerTrackingAsync(User.ToActor(), number, ct));

    /// <summary>The containers moving under a booking, each with its timeline.</summary>
    [HttpGet("bookings/{number}")]
    public async Task<ActionResult<BookingTrackingResponse>> Booking(string number, CancellationToken ct) =>
        BookingTrackingResponse.From(await movements.GetBookingTrackingAsync(User.ToActor(), number, ct));
}
