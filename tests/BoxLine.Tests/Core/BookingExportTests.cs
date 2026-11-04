using System.Text;
using BoxLine.Core.Documents;
using BoxLine.Core.Entities;
using BoxLine.Infrastructure.Documents;
using FluentAssertions;
using MiniExcelLibs;

namespace BoxLine.Tests.Core;

public class BookingExportTests
{
    private static BookingExportRow Row(string number = "BL-202611-0001", string cargo = "Cotton", string shipper = "Acme") => new(
        number, "Confirmed", shipper, "INNSA", "NLRTM", "BXL2612A1", "MV Aurora",
        new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 20, 8, 30, 0, DateTimeKind.Utc),
        5, "2x20GP, 3x40HC", cargo, new DateTime(2026, 11, 1, 9, 5, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 10, 6, 0, DateTimeKind.Utc));

    /// <summary>The CSV as text, with the byte-order mark removed.</summary>
    private static string Csv(params BookingExportRow[] rows)
    {
        var bytes = BookingCsv.Write(rows);
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    // ---- the row ----------------------------------------------------------------------------------

    [Fact]
    public void EveryRow_HasOneCellPerHeader() => Row().Cells().Should().HaveCount(BookingExportRow.Headers.Count).And.HaveCount(14);

    [Fact]
    public void FromABooking_MapsTheColumns_AndSummarisesTheContainerTypes()
    {
        var booking = new Booking
        {
            BookingNumber = "BL-202611-0007",
            Status = BookingStatus.Draft,
            Shipper = new User { ShipperCompany = "Acme", Email = "a@b.c" },
            PolId = 1,
            Pol = new Port { UnLocode = "INNSA" },
            Pod = new Port { UnLocode = "NLRTM" },
            CargoDescription = "Tea",
            Lines =
            [
                new() { Quantity = 3, ContainerType = new ContainerType { Code = "40HC", LengthFt = 40 } },
                new() { Quantity = 2, ContainerType = new ContainerType { Code = "20GP", LengthFt = 20 } },
            ],
        };

        var row = BookingExportRow.From(booking);

        row.Status.Should().Be("Draft");
        row.Shipper.Should().Be("Acme");
        row.PortOfLoading.Should().Be("INNSA");
        row.TotalContainers.Should().Be(5);
        row.ContainerTypes.Should().Be("2x20GP, 3x40HC");
        row.VoyageNumber.Should().BeEmpty();
        row.Departure.Should().BeNull();
    }

    // ---- CSV --------------------------------------------------------------------------------------

    [Fact]
    public void Csv_StartsWithAByteOrderMark_ThenTheHeaderRow_AndUsesWindowsLineEndings()
    {
        var lines = Csv(Row()).Split("\r\n");

        lines[0].Should().Be(string.Join(',', BookingExportRow.Headers));
        lines.Should().HaveCount(3, "header, one row, and the empty string after the final line ending");
        lines[2].Should().BeEmpty();
    }

    [Fact]
    public void Csv_WritesDatesAndNumbersPlainly()
    {
        var line = Csv(Row()).Split("\r\n")[1];

        line.Should().Contain("2026-12-01 08:00").And.Contain("2026-12-20 08:30").And.Contain("2026-11-01 09:05");
        line.Should().Contain(",5,");
    }

    [Fact]
    public void Csv_LeavesMissingDatesEmpty()
    {
        var row = Row() with { Departure = null, Arrival = null, VoyageNumber = string.Empty, Vessel = string.Empty };

        Csv(row).Split("\r\n")[1].Should().Contain("INNSA,NLRTM,,,,,5,");
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("crlf\r\nline", "\"crlf\r\nline\"")]
    [InlineData("", "")]
    [InlineData("naïve café 日本", "naïve café 日本")]
    public void Escape_QuotesOnlyWhenNeeded_AndKeepsUnicode(string value, string expected) =>
        BookingCsv.Escape(value).Should().Be(expected);

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+49 170 1234", "'+49 170 1234")]
    [InlineData("-5 pallets", "'-5 pallets")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTabbed", "'\tTabbed")]
    [InlineData("=HYPERLINK(\"http://x\",\"y\")", "\"'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"")]
    public void Escape_DefusesTextThatASpreadsheetWouldRunAsAFormula(string value, string expected) =>
        BookingCsv.Escape(value).Should().Be(expected);

    [Fact]
    public void Escape_DoesNotTouchTextThatMerelyContainsAFormulaCharacter() =>
        BookingCsv.Escape("rice = good").Should().Be("rice = good");

    [Fact]
    public void Csv_IsHeaderOnly_WhenThereAreNoRows()
    {
        var text = Csv();

        text.Should().Be(string.Join(',', BookingExportRow.Headers) + "\r\n");
    }

    [Fact]
    public void Csv_KeepsEveryRow_InOrder()
    {
        var text = Csv(Row("BL-202611-0001"), Row("BL-202611-0002"), Row("BL-202611-0003"));

        text.Split("\r\n").Skip(1).Where(l => l.Length > 0).Select(l => l[..14]).Should().Equal("BL-202611-0001", "BL-202611-0002", "BL-202611-0003");
    }

    // ---- Excel ------------------------------------------------------------------------------------

    private static List<IDictionary<string, object>> ReadXlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return MiniExcel.Query(stream, useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();
    }

    [Fact]
    public void Xlsx_HasTheHeaderAndOneLinePerRow_WithTypedValues()
    {
        var bytes = new MiniExcelBookingSpreadsheetWriter().WriteXlsx([Row("BL-202611-0001", "Cotton", "Acme"), Row("BL-202611-0002")]);

        bytes.Take(2).Should().Equal((byte)'P', (byte)'K');
        var rows = ReadXlsx(bytes);
        rows.Should().HaveCount(2);
        rows[0].Keys.Should().Equal(BookingExportRow.Headers);
        rows[0]["Booking number"].Should().Be("BL-202611-0001");
        rows[0]["Shipper"].Should().Be("Acme");
        rows[0]["Containers"].Should().Be(5.0, "numbers are stored as numbers");
        rows[0]["Container types"].Should().Be("2x20GP, 3x40HC");
        rows[1]["Booking number"].Should().Be("BL-202611-0002");
    }

    [Fact]
    public void Xlsx_StoresDatesAsRealDateCells()
    {
        var rows = ReadXlsx(new MiniExcelBookingSpreadsheetWriter().WriteXlsx([Row()]));

        rows[0]["Created (UTC)"].Should().Be(new DateTime(2026, 11, 1, 9, 5, 0));
        rows[0]["Departure (UTC)"].Should().Be(new DateTime(2026, 12, 1, 8, 0, 0));
    }

    [Fact]
    public void Xlsx_KeepsFormulaLikeCargoAsText_NeverAFormula()
    {
        var bytes = new MiniExcelBookingSpreadsheetWriter().WriteXlsx([Row(cargo: "=HYPERLINK(\"http://evil.example\",\"click\")")]);

        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var sheet = archive.Entries.Single(e => e.FullName == "xl/worksheets/sheet1.xml");
        using var reader = new StreamReader(sheet.Open());
        var xml = reader.ReadToEnd();

        xml.Should().NotContain("<f>").And.NotContain("<f ");
        ReadXlsx(bytes)[0]["Cargo description"].Should().Be("=HYPERLINK(\"http://evil.example\",\"click\")");
    }

    [Fact]
    public void Xlsx_WithNoRows_StillHasTheHeader()
    {
        var bytes = new MiniExcelBookingSpreadsheetWriter().WriteXlsx([]);

        using var stream = new MemoryStream(bytes);
        var sheet = MiniExcel.Query(stream, useHeaderRow: false).Cast<IDictionary<string, object>>().ToList();
        sheet.Should().HaveCount(1);
        sheet[0].Values.Select(v => v?.ToString()).Should().Equal(BookingExportRow.Headers);
    }

    [Fact]
    public void Xlsx_BlankCells_ForMissingValues()
    {
        var row = Row() with { Departure = null, Arrival = null, VoyageNumber = string.Empty, Vessel = string.Empty };

        var rows = ReadXlsx(new MiniExcelBookingSpreadsheetWriter().WriteXlsx([row]));

        rows[0]["Departure (UTC)"].Should().BeNull();
        rows[0]["Arrival (UTC)"].Should().BeNull();
    }
}
