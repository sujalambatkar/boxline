using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Bookings;
using BoxLine.Core.Documents;
using BoxLine.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Bookings. Shippers see and change only their own; Ops see and change everything.</summary>
[ApiController]
[Authorize]
[Route("api/bookings")]
public sealed class BookingsController(BookingService bookings, DocumentService documents) : ControllerBase
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

    /// <summary>
    /// Exports every booking that matches the filters (all pages, not just the current one) as CSV or Excel. It takes the
    /// same filters as the list and, like it, shows shippers only their own bookings. At most 10,000 rows.
    /// </summary>
    /// <param name="format">csv (default) or xlsx.</param>
    [HttpGet("export")]
    [Produces("text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> Export(
        [FromQuery] BookingStatus? status,
        [FromQuery] int? polId,
        [FromQuery] int? podId,
        [FromQuery] int? shipperId,
        [FromQuery] DateOnly? createdFrom,
        [FromQuery] DateOnly? createdTo,
        [FromQuery] string? search,
        [FromQuery] string format = "csv",
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat) || !Enum.IsDefined(exportFormat))
        {
            ModelState.AddModelError(nameof(format), "format must be csv or xlsx.");
            return ValidationProblem(ModelState);
        }

        var query = new BookingQuery(status, polId, podId, shipperId, createdFrom, createdTo, search);
        var file = await documents.ExportBookingsAsync(User.ToActor(), query, exportFormat, ct);

        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// The bill of lading as a PDF. A confirmed booking gets a clean document and a draft one marked DRAFT; a cancelled
    /// booking has none (409).
    /// </summary>
    [HttpGet("{id:int}/bill-of-lading")]
    [Produces("application/pdf")]
    public async Task<IActionResult> BillOfLading(int id, CancellationToken ct)
    {
        var file = await documents.GetBillOfLadingAsync(User.ToActor(), id, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingResponse>> Get(int id, CancellationToken ct) =>
        BookingResponse.From(await bookings.GetAsync(User.ToActor(), id, ct));

    /// <summary>Creates a draft booking.</summary>
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, CancellationToken ct)
    {
        var command = new CreateBookingCommand(
            request.ShipperId, request.PolId, request.PodId, request.CargoDescription, ToDrafts(request.Lines), request.VoyageId);
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
            request.PolId, request.PodId, request.CargoDescription, ToDrafts(request.Lines), rowVersion, request.VoyageId);

        return BookingResponse.From(await bookings.UpdateAsync(User.ToActor(), id, command, ct));
    }

    /// <summary>
    /// Puts a booking on a sailing, or takes it off with a null voyage. Operations staff only; the voyage must
    /// sail from the booking's loading port to its discharge port, in that order.
    /// </summary>
    [HttpPut("{id:int}/voyage")]
    [Authorize(Roles = Roles.Ops)]
    public async Task<ActionResult<BookingResponse>> AssignVoyage(int id, AssignVoyageRequest request, CancellationToken ct) =>
        BookingResponse.From(await bookings.AssignVoyageAsync(User.ToActor(), id, request.VoyageId, ct));

    [HttpPost("{id:int}/confirm")]
    public async Task<ActionResult<BookingResponse>> Confirm(int id, CancellationToken ct) =>
        BookingResponse.From(await bookings.ConfirmAsync(User.ToActor(), id, ct));

    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<BookingResponse>> Cancel(int id, CancellationToken ct) =>
        BookingResponse.From(await bookings.CancelAsync(User.ToActor(), id, ct));

    private static List<BookingLineDraft> ToDrafts(IEnumerable<BookingLineRequest> lines) =>
        lines.Select(l => new BookingLineDraft(l.ContainerTypeId, l.Quantity)).ToList();
}
