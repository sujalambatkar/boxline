using BoxLine.Core.Bookings;
using BoxLine.Core.Common;
using BoxLine.Core.Documents;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using FluentAssertions;

namespace BoxLine.Tests.Core;

/// <summary>The export cap and scoping, checked without a database through a small fake repository.</summary>
public class DocumentServiceTests
{
    private sealed class FakeBookings(int count) : IBookingRepository
    {
        public BookingQuery? LastQuery { get; private set; }

        public Task<PagedResult<Booking>> SearchAsync(BookingQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            var items = Enumerable.Range(0, Math.Min(count, query.PageSize))
                .Select(i => new Booking { BookingNumber = $"BL-202611-{i:D4}", Status = BookingStatus.Draft, CargoDescription = "x" })
                .ToList();
            return Task.FromResult(new PagedResult<Booking>(items, count, query.Page, query.PageSize));
        }

        public Task<Booking?> GetByIdAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Booking?> GetByNumberAsync(string bookingNumber, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int?> GetShipperIdAsync(int bookingId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Booking>> ListByVoyageAsync(int voyageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(Booking booking, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(Booking booking, byte[]? expectedRowVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoSpreadsheet : IBookingSpreadsheetWriter
    {
        public byte[] WriteXlsx(IReadOnlyList<BookingExportRow> rows) => [(byte)'P', (byte)'K'];
    }

    private sealed class NoPdf : IBillOfLadingRenderer
    {
        public byte[] Render(BillOfLadingData data) => [];
    }

    private static DocumentService Service(FakeBookings bookings) =>
        new(bookings, null!, new NoPdf(), new NoSpreadsheet(), new FixedClock(new DateTimeOffset(2026, 11, 5, 14, 30, 0, TimeSpan.Zero)));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly Actor Ops = new(1, UserRole.Ops);
    private static readonly Actor Shipper = new(7, UserRole.Shipper);

    [Fact]
    public async Task Export_AsksForTheFirstPageOfEverything_OneRowOverTheCap()
    {
        var bookings = new FakeBookings(3);

        await Service(bookings).ExportBookingsAsync(Ops, new BookingQuery(Status: BookingStatus.Draft), ExportFormat.Csv, default);

        bookings.LastQuery!.Page.Should().Be(1);
        bookings.LastQuery.PageSize.Should().Be(DocumentService.MaxExportRows + 1, "one extra row reveals whether the cap was exceeded");
        bookings.LastQuery.Status.Should().Be(BookingStatus.Draft);
    }

    [Fact]
    public async Task Export_AtExactlyTheCap_Succeeds()
    {
        var file = await Service(new FakeBookings(DocumentService.MaxExportRows)).ExportBookingsAsync(Ops, new BookingQuery(), ExportFormat.Csv, default);

        file.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Export_OneRowOverTheCap_IsRefusedWithAClearMessage()
    {
        var act = () => Service(new FakeBookings(DocumentService.MaxExportRows + 1)).ExportBookingsAsync(Ops, new BookingQuery(), ExportFormat.Csv, default);

        (await act.Should().ThrowAsync<DomainValidationException>()).Which.Errors["Export"].Single()
            .Should().Be("More than 10,000 bookings match. Narrow the filters and try again.");
    }

    [Fact]
    public async Task Export_ForAShipper_IgnoresAShipperIdFilterAndUsesTheirOwnId()
    {
        var bookings = new FakeBookings(1);

        await Service(bookings).ExportBookingsAsync(Shipper, new BookingQuery(ShipperId: 999), ExportFormat.Csv, default);

        bookings.LastQuery!.ShipperId.Should().Be(7);
    }

    [Fact]
    public async Task Export_ForOps_KeepsTheShipperIdFilter()
    {
        var bookings = new FakeBookings(1);

        await Service(bookings).ExportBookingsAsync(Ops, new BookingQuery(ShipperId: 42), ExportFormat.Csv, default);

        bookings.LastQuery!.ShipperId.Should().Be(42);
    }

    [Theory]
    [InlineData(ExportFormat.Csv, "bookings-20261105-1430.csv", "text/csv; charset=utf-8")]
    [InlineData(ExportFormat.Xlsx, "bookings-20261105-1430.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task Export_NamesTheFileWithTheTimeAndSetsTheContentType(ExportFormat format, string fileName, string contentType)
    {
        var file = await Service(new FakeBookings(1)).ExportBookingsAsync(Ops, new BookingQuery(), format, default);

        file.FileName.Should().Be(fileName);
        file.ContentType.Should().Be(contentType);
    }
}
