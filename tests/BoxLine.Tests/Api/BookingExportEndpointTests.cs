using System.Net;
using System.Net.Http.Json;
using System.Text;
using BoxLine.Api.Contracts;
using BoxLine.Core.Documents;
using FluentAssertions;
using MiniExcelLibs;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class BookingExportEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A small RFC 4180 reader, written independently of the code under test.</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
                i++;
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static async Task<List<Dictionary<string, string>>> CsvRowsAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"/api/bookings/export?format=csv{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);

        var table = ParseCsv(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        table[0].Should().Equal(BookingExportRow.Headers);
        return table.Skip(1).Select(r => BookingExportRow.Headers.Zip(r).ToDictionary(p => p.First, p => p.Second)).ToList();
    }

    private static async Task<BookingResponse> BookAsync(Session shipper, string cargo, int polId, int podId, int typeId, int quantity = 1)
    {
        var response = await shipper.Client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(
            polId, podId, cargo, [new BookingLineRequest(typeId, quantity)]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;
    }

    private async Task<(int Innsa, int Nlrtm, int Aejea, int Type20)> RefsAsync(HttpClient client)
    {
        var ports = (await client.GetFromJsonAsync<List<PortResponse>>("/api/ports"))!;
        var types = (await client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;
        return (ports.Single(p => p.UnLocode == "INNSA").Id, ports.Single(p => p.UnLocode == "NLRTM").Id,
            ports.Single(p => p.UnLocode == "AEJEA").Id, types.Single(t => t.Code == "20GP").Id);
    }

    // ---- CSV --------------------------------------------------------------------------------------

    [Fact]
    public async Task Csv_HasTheHeaderAndOneRowPerBooking_WithTheRightValues()
    {
        var shipper = await factory.RegisterShipperAsync("Export Freight");
        var refs = await RefsAsync(shipper.Client);
        var booking = await BookAsync(shipper, "Basmati rice", refs.Innsa, refs.Nlrtm, refs.Type20, 3);

        var response = await shipper.Client.GetAsync("/api/bookings/export");

        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^bookings-\d{8}-\d{4}\.csv$");
        var rows = await CsvRowsAsync(shipper.Client);
        var row = rows.Should().ContainSingle().Subject;
        row["Booking number"].Should().Be(booking.BookingNumber);
        row["Status"].Should().Be("Draft");
        row["Shipper"].Should().Be("Export Freight");
        row["Port of loading"].Should().Be("INNSA");
        row["Port of discharge"].Should().Be("NLRTM");
        row["Containers"].Should().Be("3");
        row["Container types"].Should().Be("3x20GP");
        row["Cargo description"].Should().Be("Basmati rice");
        row["Created (UTC)"].Should().MatchRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$");
        row["Voyage"].Should().BeEmpty();
    }

    [Fact]
    public async Task Csv_RoundTripsCommasQuotesAndNewlinesInCargo()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await RefsAsync(shipper.Client);
        var tricky = "Steel coils, 5 \"heavy\" pallets\nsecond line, with comma";
        await BookAsync(shipper, tricky, refs.Innsa, refs.Nlrtm, refs.Type20);

        var rows = await CsvRowsAsync(shipper.Client);

        rows.Should().ContainSingle().Which["Cargo description"].Should().Be(tricky);
    }

    [Theory]
    [InlineData("=SUM(1+1)")]
    [InlineData("+1555")]
    [InlineData("-2+3")]
    [InlineData("@cmd")]
    public async Task Csv_DefusesCargoThatWouldRunAsAFormula(string cargo)
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await RefsAsync(shipper.Client);
        await BookAsync(shipper, cargo, refs.Innsa, refs.Nlrtm, refs.Type20);

        var rows = await CsvRowsAsync(shipper.Client);

        rows.Should().ContainSingle().Which["Cargo description"].Should().Be("'" + cargo);
    }

    [Fact]
    public async Task Csv_ContainsEveryMatchingBooking_NotJustOnePage()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await RefsAsync(shipper.Client);
        for (var i = 0; i < 25; i++)
        {
            await BookAsync(shipper, $"Cargo {i:D2}", refs.Innsa, refs.Nlrtm, refs.Type20);
        }

        var rows = await CsvRowsAsync(shipper.Client);
        var listPage = (await shipper.Client.GetFromJsonAsync<PagedResponse<BookingResponse>>("/api/bookings?pageSize=20", TestJson.Options))!;

        rows.Should().HaveCount(25);
        listPage.Items.Should().HaveCount(20, "a list page is smaller than the export");
        rows.Select(r => r["Booking number"]).Should().OnlyHaveUniqueItems();
        rows.Select(r => r["Created (UTC)"]).Should().BeInDescendingOrder("the export is newest first, like the list");
    }

    [Fact]
    public async Task Csv_RespectsTheSameFiltersAsTheList()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await RefsAsync(shipper.Client);
        var cotton = await BookAsync(shipper, "Cotton garments", refs.Innsa, refs.Nlrtm, refs.Type20);
        var tea = await BookAsync(shipper, "Darjeeling tea", refs.Aejea, refs.Nlrtm, refs.Type20);
        var steel = await BookAsync(shipper, "Steel coils", refs.Innsa, refs.Aejea, refs.Type20);
        await shipper.Client.PostAsync($"/api/bookings/{steel.Id}/confirm", null);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        async Task<string[]> Numbers(string query) => (await CsvRowsAsync(shipper.Client, query)).Select(r => r["Booking number"]).ToArray();

        (await Numbers("&status=Confirmed")).Should().Equal(steel.BookingNumber);
        (await Numbers($"&polId={refs.Aejea}")).Should().Equal(tea.BookingNumber);
        (await Numbers($"&podId={refs.Nlrtm}")).Should().BeEquivalentTo([cotton.BookingNumber, tea.BookingNumber]);
        (await Numbers("&search=darjeeling")).Should().Equal(tea.BookingNumber);
        (await Numbers($"&search={cotton.BookingNumber}")).Should().Equal(cotton.BookingNumber);
        (await Numbers($"&createdFrom={today}&createdTo={today}")).Should().HaveCount(3);
        (await Numbers($"&createdFrom={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Csv_WithNothingToExport_IsJustTheHeader()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await CsvRowsAsync(shipper.Client)).Should().BeEmpty();
    }

    [Fact]
    public async Task Csv_ShowsTheSailing_WhenTheBookingIsOnOne()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var vessel = await TestData.NewVesselAsync(ops, "MV Csv Star");
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), vessel.Id);
        await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id, voyage.Id);

        var row = (await CsvRowsAsync(shipper.Client)).Should().ContainSingle().Subject;

        row["Voyage"].Should().Be(voyage.VoyageNumber);
        row["Vessel"].Should().Be("MV Csv Star");
        row["Departure (UTC)"].Should().Be(voyage.Calls[0].Etd!.Value.ToString("yyyy-MM-dd HH:mm"));
        row["Arrival (UTC)"].Should().Be(voyage.Calls[1].Eta!.Value.ToString("yyyy-MM-dd HH:mm"));
    }

    // ---- who sees what ----------------------------------------------------------------------------

    [Fact]
    public async Task AShipper_OnlyExportsTheirOwnBookings_EvenWhenAskingForSomeoneElses()
    {
        var alice = await factory.RegisterShipperAsync("Alice Ltd");
        var bob = await factory.RegisterShipperAsync("Bob Ltd");
        var refs = await RefsAsync(alice.Client);
        var mine = await BookAsync(alice, "Alice cargo", refs.Innsa, refs.Nlrtm, refs.Type20);
        var theirs = await BookAsync(bob, "Bob cargo", refs.Innsa, refs.Nlrtm, refs.Type20);

        var own = await CsvRowsAsync(alice.Client);
        var widened = await CsvRowsAsync(alice.Client, $"&shipperId={bob.User.Id}");

        own.Select(r => r["Booking number"]).Should().Equal(mine.BookingNumber);
        widened.Select(r => r["Booking number"]).Should().Equal(mine.BookingNumber);
        widened.Should().NotContain(r => r["Booking number"] == theirs.BookingNumber);
    }

    [Fact]
    public async Task Ops_CanExportOneShippersBookings_ByShipperId()
    {
        var alice = await factory.RegisterShipperAsync("Alice Ltd");
        var bob = await factory.RegisterShipperAsync("Bob Ltd");
        var refs = await RefsAsync(alice.Client);
        await BookAsync(alice, "Alice cargo", refs.Innsa, refs.Nlrtm, refs.Type20);
        var theirs = await BookAsync(bob, "Bob cargo", refs.Innsa, refs.Nlrtm, refs.Type20);

        var rows = await CsvRowsAsync(ops, $"&shipperId={bob.User.Id}");

        rows.Should().ContainSingle().Which["Booking number"].Should().Be(theirs.BookingNumber);
        rows[0]["Shipper"].Should().Be("Bob Ltd");
    }

    [Fact]
    public async Task Anonymous_CannotExport_AndABadFormatIsRejected()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await factory.CreateClient().GetAsync("/api/bookings/export")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await shipper.Client.GetAsync("/api/bookings/export?format=pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await shipper.Client.GetAsync("/api/bookings/export?format=9")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TheDefaultFormat_IsCsv_AndTheFormatIsCaseInsensitive()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.GetAsync("/api/bookings/export")).Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await shipper.Client.GetAsync("/api/bookings/export?format=CSV")).Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        (await shipper.Client.GetAsync("/api/bookings/export?format=XLSX")).Content.Headers.ContentType!.MediaType
            .Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    // ---- Excel ------------------------------------------------------------------------------------

    [Fact]
    public async Task Xlsx_IsARealWorkbook_WithTheSameRowsAsTheCsv()
    {
        var shipper = await factory.RegisterShipperAsync("Sheet Freight");
        var refs = await RefsAsync(shipper.Client);
        var first = await BookAsync(shipper, "First cargo", refs.Innsa, refs.Nlrtm, refs.Type20, 2);
        var second = await BookAsync(shipper, "=1+1", refs.Aejea, refs.Nlrtm, refs.Type20);

        var response = await shipper.Client.GetAsync("/api/bookings/export?format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.Should().MatchRegex(@"^bookings-\d{8}-\d{4}\.xlsx$");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(2).Should().Equal((byte)'P', (byte)'K');

        using var stream = new MemoryStream(bytes);
        var rows = MiniExcel.Query(stream, useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();
        rows.Should().HaveCount(2);
        rows.Select(r => (string)r["Booking number"]).Should().Equal(second.BookingNumber, first.BookingNumber);
        var firstRow = rows.Single(r => (string)r["Booking number"] == first.BookingNumber);
        firstRow["Shipper"].Should().Be("Sheet Freight");
        firstRow["Containers"].Should().Be(2.0);
        firstRow["Created (UTC)"].Should().BeOfType<DateTime>();
        rows.Single(r => (string)r["Booking number"] == second.BookingNumber)["Cargo description"].Should().Be("=1+1");
    }

    [Fact]
    public async Task Xlsx_RespectsFilters_AndWithNothingToExportHasOnlyTheHeader()
    {
        var shipper = await factory.RegisterShipperAsync();
        var refs = await RefsAsync(shipper.Client);
        var kept = await BookAsync(shipper, "Kept", refs.Innsa, refs.Nlrtm, refs.Type20);
        await BookAsync(shipper, "Dropped", refs.Aejea, refs.Nlrtm, refs.Type20);

        async Task<int> CountAsync(string query)
        {
            var bytes = await (await shipper.Client.GetAsync($"/api/bookings/export?format=xlsx{query}")).Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            return MiniExcel.Query(stream, useHeaderRow: true).Count();
        }

        (await CountAsync($"&search={kept.BookingNumber}")).Should().Be(1);
        (await CountAsync("&status=Cancelled")).Should().Be(0);
    }
}
