using System.Text;
using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;

namespace BoxLine.Core.Documents;

public enum ExportFormat
{
    Csv,
    Xlsx,
}

/// <summary>A generated file ready to send to the user.</summary>
public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>One line of the bookings export.</summary>
public sealed record BookingExportRow(
    string BookingNumber,
    string Status,
    string Shipper,
    string PortOfLoading,
    string PortOfDischarge,
    string VoyageNumber,
    string Vessel,
    DateTime? Departure,
    DateTime? Arrival,
    int TotalContainers,
    string ContainerTypes,
    string CargoDescription,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    /// <summary>The column titles, in the order of <see cref="Cells"/>.</summary>
    public static readonly IReadOnlyList<string> Headers =
    [
        "Booking number", "Status", "Shipper", "Port of loading", "Port of discharge", "Voyage", "Vessel",
        "Departure (UTC)", "Arrival (UTC)", "Containers", "Container types", "Cargo description", "Created (UTC)", "Updated (UTC)",
    ];

    /// <summary>Builds a row from a booking with its shipper, ports, voyage and lines loaded.</summary>
    public static BookingExportRow From(Booking booking) => new(
        booking.BookingNumber,
        booking.Status.ToString(),
        booking.Shipper?.ShipperCompany ?? booking.Shipper?.Email ?? string.Empty,
        booking.Pol?.UnLocode ?? string.Empty,
        booking.Pod?.UnLocode ?? string.Empty,
        booking.Voyage?.VoyageNumber ?? string.Empty,
        booking.Voyage?.Vessel?.Name ?? string.Empty,
        booking.Voyage is null ? null : VoyageRotation.Departure(booking.Voyage.Calls, booking.PolId),
        booking.Voyage is null ? null : VoyageRotation.Arrival(booking.Voyage.Calls, booking.PodId),
        booking.Lines.Sum(l => l.Quantity),
        string.Join(", ", booking.Lines
            .OrderBy(l => l.ContainerType?.LengthFt).ThenBy(l => l.ContainerType?.Code, StringComparer.Ordinal)
            .Select(l => $"{l.Quantity}x{l.ContainerType?.Code}")),
        booking.CargoDescription,
        booking.CreatedAt,
        booking.UpdatedAt);

    /// <summary>The cells in column order; dates as <see cref="DateTime"/>, the rest as text or numbers.</summary>
    public object?[] Cells() =>
    [
        BookingNumber, Status, Shipper, PortOfLoading, PortOfDischarge, VoyageNumber, Vessel,
        Departure, Arrival, TotalContainers, ContainerTypes, CargoDescription, CreatedAt, UpdatedAt,
    ];
}

/// <summary>Writes the bookings export as CSV: UTF-8 with a byte-order mark (so Excel reads accents), RFC 4180 quoting.</summary>
public static class BookingCsv
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    public static byte[] Write(IEnumerable<BookingExportRow> rows)
    {
        var text = new StringBuilder();
        text.Append(string.Join(',', BookingExportRow.Headers.Select(Escape))).Append("\r\n");

        foreach (var row in rows)
        {
            text.Append(string.Join(',', row.Cells().Select(Format))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Format(object? cell) => cell switch
    {
        null => string.Empty,
        DateTime date => date.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture),
        int number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Escape(cell.ToString() ?? string.Empty),
    };

    /// <summary>
    /// Quotes a field when needed. Text that a spreadsheet would run as a formula (starting with =, +, -, @, tab or
    /// carriage return) gets a leading apostrophe, so a hostile cargo description cannot execute when the file is opened.
    /// </summary>
    public static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
