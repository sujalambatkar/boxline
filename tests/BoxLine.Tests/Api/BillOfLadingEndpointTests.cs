using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using UglyToad.PdfPig;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class BillOfLadingEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>All the text on every page, with whitespace collapsed so words can be searched for.</summary>
    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    /// <summary>
    /// The watermark: the oversized glyphs on the page (130 pt against 10 pt body text), read left to right. Rotated text
    /// is not returned in reading order by word extraction, so it is found by glyph size and position instead.
    /// </summary>
    private static string Watermark(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var big = document.GetPages().SelectMany(p => p.Letters).Where(l => l.PointSize > 100).OrderBy(l => l.GlyphRectangle.Left);
        return string.Concat(big.Select(l => l.Value));
    }

    private static async Task<(byte[] Bytes, HttpResponseMessage Response)> GetPdf(HttpClient client, int bookingId)
    {
        var response = await client.GetAsync($"/api/bookings/{bookingId}/bill-of-lading");
        return (await response.Content.ReadAsByteArrayAsync(), response);
    }

    [Fact]
    public async Task AConfirmedBooking_GivesACleanPdf_NamedAfterTheBooking()
    {
        var shipper = await factory.RegisterShipperAsync("Blue Ocean Traders");
        var booking = await TestData.ConfirmedBookingAsync(shipper);

        var (bytes, response) = await GetPdf(shipper.Client, booking.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be($"{booking.BookingNumber}.pdf");
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", "every PDF starts with its signature");
        bytes.Length.Should().BeGreaterThan(1_000);

        var text = PdfText(bytes);
        text.Should().Contain("BILL OF LADING").And.Contain(booking.BookingNumber);
        text.Should().Contain("Blue Ocean Traders").And.Contain("Auto parts");
        text.Should().Contain("INNSA").And.Contain("NLRTM").And.Contain("Nhava Sheva").And.Contain("Rotterdam");
        Watermark(bytes).Should().BeEmpty("a confirmed booking carries no watermark");
        text.Should().Contain("Date of issue:").And.NotContain("Not issued");
    }

    [Fact]
    public async Task ADraftBooking_GivesAPdfMarkedDraft_AndNotIssued()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper, confirm: false);

        var (bytes, response) = await GetPdf(shipper.Client, booking.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
        var text = PdfText(bytes);
        Watermark(bytes).Should().Be("DRAFT");
        text.Should().Contain("Not issued (draft)").And.Contain(booking.BookingNumber);
        text.Should().NotContain("Date of issue:");
    }

    [Fact]
    public async Task TheIssueDate_IsTheConfirmationDay_AndStaysTheSameOnLaterDownloads()
    {
        var shipper = await factory.RegisterShipperAsync();
        var draft = await TestData.ConfirmedBookingAsync(shipper, confirm: false);
        draft.ConfirmedAt.Should().BeNull("a draft has not been confirmed");

        var confirmed = (await (await shipper.Client.PostAsync($"/api/bookings/{draft.Id}/confirm", null)).Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;
        confirmed.ConfirmedAt.Should().NotBeNull();
        confirmed.ConfirmedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(2));

        var first = PdfText((await GetPdf(shipper.Client, draft.Id)).Bytes);
        var second = PdfText((await GetPdf(shipper.Client, draft.Id)).Bytes);

        var expected = $"Date of issue: {confirmed.ConfirmedAt:dd MMM yyyy}";
        first.Should().Contain(expected);
        second.Should().Contain(expected);
    }

    [Fact]
    public async Task TheVessel_AndVoyage_AreShown_WhenTheBookingIsOnASailing()
    {
        var shipper = await factory.RegisterShipperAsync();
        var ports = await TestData.NewPortsAsync(ops, 2);
        var vessel = await TestData.NewVesselAsync(ops, "MV Pdf Star");
        var voyage = await TestData.NewVoyageAsync(ops, ports.Select(p => p.Id).ToList(), vessel.Id);
        var booking = await TestData.BookingAsync(shipper, ports[0].Id, ports[1].Id, voyage.Id, confirm: true);

        var text = PdfText((await GetPdf(shipper.Client, booking.Id)).Bytes);

        text.Should().Contain("MV Pdf Star").And.Contain(voyage.VoyageNumber).And.Contain(vessel.ImoNumber);
        text.Should().Contain("Estimated departure:").And.Contain("Estimated arrival:");
        text.Should().NotContain("To be advised");
    }

    [Fact]
    public async Task WithoutASailing_TheVoyageIsToBeAdvised()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);

        PdfText((await GetPdf(shipper.Client, booking.Id)).Bytes).Should().Contain("To be advised");
    }

    [Fact]
    public async Task ContainersRecordedAgainstTheBooking_AreListedByNumber()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var first = await TestData.NewContainerAsync(ops);
        var second = await TestData.NewContainerAsync(ops);
        var at = TestData.Start();
        await TestData.RecordAsync(ops, first, MovementEventType.GateOutEmpty, at, booking.BookingNumber);
        await TestData.RecordAsync(ops, first, MovementEventType.Stuffed, at.AddHours(1), booking.BookingNumber);
        await TestData.RecordAsync(ops, second, MovementEventType.GateOutEmpty, at, booking.BookingNumber);

        var text = PdfText((await GetPdf(shipper.Client, booking.Id)).Bytes);

        text.Should().Contain("Containers recorded against this booking").And.Contain(first).And.Contain(second);
        text.Split(first).Length.Should().Be(2, "a container with several movements is listed once");
    }

    [Fact]
    public async Task WithoutRecordedContainers_ThatSectionIsLeftOut()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);

        PdfText((await GetPdf(shipper.Client, booking.Id)).Bytes).Should().NotContain("Containers recorded against this booking");
    }

    [Fact]
    public async Task ACancelledBooking_HasNoBillOfLading()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        await shipper.Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null);

        var response = await shipper.Client.GetAsync($"/api/bookings/{booking.Id}/bill-of-lading");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail.Should().Contain("is cancelled");
    }

    // ---- who may download what --------------------------------------------------------------------

    [Fact]
    public async Task AShipper_CannotGetAnotherShippersBillOfLading_ButOpsCanGetAny()
    {
        var owner = await factory.RegisterShipperAsync();
        var stranger = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(owner);

        (await stranger.Client.GetAsync($"/api/bookings/{booking.Id}/bill-of-lading")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var asOps = await GetPdf(ops, booking.Id);

        asOps.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        PdfText(asOps.Bytes).Should().Contain(booking.BookingNumber);
    }

    [Fact]
    public async Task AnUnknownBooking_IsNotFound_AndAnonymousCallersAreRefused()
    {
        var shipper = await factory.RegisterShipperAsync();

        (await shipper.Client.GetAsync("/api/bookings/999999/bill-of-lading")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.CreateClient().GetAsync("/api/bookings/1/bill-of-lading")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheDispositionHeader_IsExposedToBrowsersOnAnotherOrigin()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/bookings/{booking.Id}/bill-of-lading");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Authorization = shipper.Client.DefaultRequestHeaders.Authorization;

        var response = await factory.CreateClient().SendAsync(request);

        response.Headers.GetValues("Access-Control-Expose-Headers").Single().Should().Contain("Content-Disposition");
    }
}
