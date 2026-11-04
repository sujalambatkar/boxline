using BoxLine.Core.Documents;

namespace BoxLine.Core.Interfaces;

public interface IBillOfLadingRenderer
{
    /// <summary>Renders the bill of lading as a PDF.</summary>
    byte[] Render(BillOfLadingData data);
}

public interface IBookingSpreadsheetWriter
{
    /// <summary>Writes the rows as an Excel workbook (.xlsx).</summary>
    byte[] WriteXlsx(IReadOnlyList<BookingExportRow> rows);
}
