using BoxLine.Core.Bookings;
using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;

namespace BoxLine.Core.Documents;

/// <summary>Generates documents and exports. Shippers only ever get documents for their own bookings.</summary>
public sealed class DocumentService(
    IBookingRepository bookings,
    IMovementRepository movements,
    IBillOfLadingRenderer billOfLading,
    IBookingSpreadsheetWriter spreadsheet,
    TimeProvider clock)
{
    /// <summary>The most rows one export may contain.</summary>
    public const int MaxExportRows = 10_000;

    public const string PdfContentType = "application/pdf";
    public const string CsvContentType = "text/csv; charset=utf-8";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// The bill of lading for a booking. A confirmed booking gets a clean document; a draft gets one marked DRAFT;
    /// a cancelled booking has none.
    /// </summary>
    /// <exception cref="NotFoundException">The booking does not exist or belongs to another shipper.</exception>
    /// <exception cref="ConflictException">The booking is cancelled.</exception>
    public async Task<ExportFile> GetBillOfLadingAsync(Actor actor, int bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null || (!actor.IsOps && booking.ShipperId != actor.UserId))
        {
            throw new NotFoundException($"Booking {bookingId} was not found.");
        }

        if (booking.Status == BookingStatus.Cancelled)
        {
            throw new ConflictException($"Booking {booking.BookingNumber} is cancelled, so it has no bill of lading.");
        }

        var data = BillOfLadingData.From(
            booking, await movements.ListForBookingAsync(bookingId, cancellationToken), clock.GetUtcNow().UtcDateTime);

        return new ExportFile($"{booking.BookingNumber}.pdf", PdfContentType, billOfLading.Render(data));
    }

    /// <summary>
    /// Exports every booking matching the filters (not just one page). Shippers are limited to their own bookings
    /// whatever filter they pass.
    /// </summary>
    /// <exception cref="DomainValidationException">More than <see cref="MaxExportRows"/> bookings match.</exception>
    public async Task<ExportFile> ExportBookingsAsync(
        Actor actor, BookingQuery query, ExportFormat format, CancellationToken cancellationToken)
    {
        var scoped = query with
        {
            ShipperId = actor.IsOps ? query.ShipperId : actor.UserId,
            Page = 1,
            PageSize = MaxExportRows + 1,
        };

        var result = await bookings.SearchAsync(scoped, cancellationToken);
        if (result.Items.Count > MaxExportRows)
        {
            throw new DomainValidationException(
                "Export", $"More than {MaxExportRows:N0} bookings match. Narrow the filters and try again.");
        }

        var rows = result.Items.Select(BookingExportRow.From).ToList();
        var stamp = clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);

        return format == ExportFormat.Xlsx
            ? new ExportFile($"bookings-{stamp}.xlsx", XlsxContentType, spreadsheet.WriteXlsx(rows))
            : new ExportFile($"bookings-{stamp}.csv", CsvContentType, BookingCsv.Write(rows));
    }
}
