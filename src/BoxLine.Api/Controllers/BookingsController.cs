using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Bookings;
using BoxLine.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Bookings. Shippers see and change only their own; Ops see and change everything.</summary>
[ApiController]
[Authorize]
[Route("api/bookings")]
public sealed class BookingsController(BookingService bookings) : ControllerBase
{
    /// <summary>Lists bookings, newest first, with server-side filtering and paging.</summary>
    /// <param name="createdFrom">First creation day, inclusive (yyyy-MM-dd).</param>
    /// <param name="createdTo">Last creation day, inclusive (yyyy-MM-dd).</param>
    /// <param name="search">Part of the booking number or cargo description.</param>
    /// <param name="shipperId">Ops only; shippers are always limited to their own bookings.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<BookingResponse>>> List(
        [FromQuery] BookingStatus? status,
        [FromQuery] int? polId,
        [FromQuery] int? podId,
        [FromQuery] int? shipperId,
        [FromQuery] DateOnly? createdFrom,
        [FromQuery] DateOnly? createdTo,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new BookingQuery(status, polId, podId, shipperId, createdFrom, createdTo, search, page, pageSize);
        var result = await bookings.SearchAsync(User.ToActor(), query, ct);

        return new PagedResponse<BookingResponse>(
            result.Items.Select(BookingResponse.From).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingResponse>> Get(int id, CancellationToken ct) =>
        BookingResponse.From(await bookings.GetAsync(User.ToActor(), id, ct));

    /// <summary>Creates a draft booking.</summary>
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var command = new CreateBookingCommand(
            request.ShipperId, request.PolId, request.PodId, request.CargoDescription, ToDrafts(request.Lines));
        var booking = await bookings.CreateAsync(User.ToActor(), command, ct);

        return CreatedAtAction(nameof(Get), new { id = booking.Id }, BookingResponse.From(booking));
    }

    /// <summary>Replaces the content of a draft booking.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<BookingResponse>> Update(int id, UpdateBookingRequest request, CancellationToken ct)
    {
        byte[]? rowVersion = null;
        if (!string.IsNullOrEmpty(request.RowVersion))
        {
            try
            {
                rowVersion = Convert.FromBase64String(request.RowVersion);
            }
            catch (FormatException)
            {
                ModelState.AddModelError(nameof(request.RowVersion), "RowVersion is not valid.");
                return ValidationProblem(ModelState);
            }
        }

        var command = new UpdateBookingCommand(
            request.PolId, request.PodId, request.CargoDescription, ToDrafts(request.Lines), rowVersion);

        return BookingResponse.From(await bookings.UpdateAsync(User.ToActor(), id, command, ct));
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<BookingResponse>> Confirm(int id, CancellationToken ct) =>
        BookingResponse.From(await bookings.ConfirmAsync(User.ToActor(), id, ct));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<BookingResponse>> Cancel(int id, CancellationToken ct) =>
        BookingResponse.From(await bookings.CancelAsync(User.ToActor(), id, ct));

    private static List<BookingLineDraft> ToDrafts(IEnumerable<BookingLineRequest> lines) =>
        lines.Select(l => new BookingLineDraft(l.ContainerTypeId, l.Quantity)).ToList();
}
