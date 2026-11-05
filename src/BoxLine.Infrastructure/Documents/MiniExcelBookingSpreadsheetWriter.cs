using System.Data;
using BoxLine.Core.Documents;
using BoxLine.Core.Interfaces;
using MiniExcelLibs;
using MiniExcelLibs.Attributes;
using MiniExcelLibs.OpenXml;

namespace BoxLine.Infrastructure.Documents;

/// <summary>Writes the bookings export as an .xlsx workbook with real date cells, set column widths and a filter row.</summary>
public sealed class MiniExcelBookingSpreadsheetWriter : IBookingSpreadsheetWriter
{
    private const string SheetName = "Bookings";
    private const string DateFormat = "yyyy-mm-dd hh:mm";

    public byte[] WriteXlsx(IReadOnlyList<BookingExportRow> rows)
    {
        // A DataTable keeps the header row even when there are no bookings.
        var table = new DataTable(SheetName);
        var headers = BookingExportRow.Headers;
        foreach (var (header, index) in headers.Select((h, i) => (h, i)))
        {
            table.Columns.Add(header, index switch
            {
                7 or 8 or 12 or 13 => typeof(DateTime),
                9 => typeof(int),
                _ => typeof(string),
            });
        }

        foreach (var row in rows)
        {
            table.Rows.Add(row.Cells().Select(cell => cell ?? DBNull.Value).ToArray());
        }

        var configuration = new OpenXmlConfiguration
        {
            AutoFilter = true,
            FreezeRowCount = 1,
            DynamicColumns = headers.Select((header, index) => new DynamicExcelColumn(header)
            {
                Width = index switch
                {
                    0 => 18,
                    2 => 24,
                    5 => 14,
                    6 => 22,
                    7 or 8 or 12 or 13 => 18,
                    10 => 22,
                    11 => 40,
                    _ => 14,
                },
                Format = index is 7 or 8 or 12 or 13 ? DateFormat : null,
            }).ToArray(),
        };

        using var stream = new MemoryStream();
        MiniExcel.SaveAs(stream, table, sheetName: SheetName, configuration: configuration);
        return stream.ToArray();
    }
}
