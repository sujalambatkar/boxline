using System.Globalization;
using BoxLine.Core.Documents;
using BoxLine.Core.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BoxLine.Infrastructure.Documents;

/// <summary>Renders the bill of lading with QuestPDF (Community licence, set in <c>AddInfrastructure</c>).</summary>
public sealed class QuestPdfBillOfLadingRenderer : IBillOfLadingRenderer
{
    private static readonly Color Brand = Colors.Blue.Darken3;
    private static readonly Color Muted = Colors.Grey.Darken1;

    public byte[] Render(BillOfLadingData data) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(text => text.FontSize(10));

            // Behind the content, so the watermark can never hide any text.
            if (data.IsDraft)
            {
                page.Background().AlignCenter().AlignMiddle().Rotate(-35)
                    .Text("DRAFT").FontSize(130).Bold().FontColor(Colors.Grey.Lighten2);
            }

            page.Header().Element(container => Header(container, data));
            page.Content().PaddingVertical(14).Element(container => Body(container, data));
            page.Footer().Element(container => Footer(container, data));
        })).GeneratePdf();

    private static void Header(IContainer container, BillOfLadingData data) =>
        container.BorderBottom(1.5f).BorderColor(Brand).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("BoxLine").FontSize(24).Bold().FontColor(Brand);
                column.Item().Text("Container shipping line").FontColor(Muted);
            });

            row.RelativeItem().AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text("BILL OF LADING").FontSize(20).Bold();
                column.Item().AlignRight().Text($"B/L No. {data.BookingNumber}").FontSize(12).Bold();
                column.Item().AlignRight().Text(data.IssueDate is { } issued
                    ? $"Date of issue: {issued.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}"
                    : "Not issued (draft)").FontColor(Muted);
            });
        });

    private static void Body(IContainer container, BillOfLadingData data) =>
        container.Column(column =>
        {
            column.Spacing(12);

            column.Item().Row(row =>
            {
                row.Spacing(12);
                row.RelativeItem().Element(c => Field(c, "Shipper", data.ShipperName, data.ShipperEmail));
                row.RelativeItem().Element(c => Field(c, "Carrier", "BoxLine"));
            });

            column.Item().Row(row =>
            {
                row.Spacing(12);
                row.RelativeItem().Element(c => Field(c, "Port of loading", $"{data.PortOfLoading.Name} ({data.PortOfLoading.UnLocode})"));
                row.RelativeItem().Element(c => Field(c, "Port of discharge", $"{data.PortOfDischarge.Name} ({data.PortOfDischarge.UnLocode})"));
            });

            column.Item().Element(c => Vessel(c, data.Voyage));
            column.Item().Element(c => Field(c, "Description of goods", data.CargoDescription));
            column.Item().Element(c => Booked(c, data));

            if (data.Loaded.Count > 0)
            {
                column.Item().Element(c => Loaded(c, data));
            }
        });

    private static void Vessel(IContainer container, BolVoyage? voyage) =>
        Box(container).Column(column =>
        {
            column.Item().Text("Vessel and voyage").FontSize(8).FontColor(Muted).SemiBold();

            if (voyage is null)
            {
                column.Item().Text("To be advised");
                return;
            }

            column.Item().Text($"{voyage.VesselName} (IMO {voyage.ImoNumber}), voyage {voyage.VoyageNumber}").Bold();
            if (voyage.Departure is { } departure)
            {
                column.Item().Text($"Estimated departure: {FormatTime(departure)}");
            }

            if (voyage.Arrival is { } arrival)
            {
                column.Item().Text($"Estimated arrival: {FormatTime(arrival)}");
            }
        });

    private static void Booked(IContainer container, BillOfLadingData data) =>
        Box(container).Column(column =>
        {
            column.Item().PaddingBottom(4).Text("Containers booked").FontSize(8).FontColor(Muted).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(60);
                    columns.RelativeColumn();
                    columns.ConstantColumn(70);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Type");
                    header.Cell().Element(HeaderCell).Text("Description");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Quantity");
                });

                foreach (var line in data.Booked)
                {
                    table.Cell().Element(BodyCell).Text(line.TypeCode);
                    table.Cell().Element(BodyCell).Text(line.Description);
                    table.Cell().Element(BodyCell).AlignRight().Text(line.Quantity.ToString(CultureInfo.InvariantCulture));
                }

                table.Cell().ColumnSpan(2).Element(BodyCell).AlignRight().Text("Total").Bold();
                table.Cell().Element(BodyCell).AlignRight().Text(data.TotalBooked.ToString(CultureInfo.InvariantCulture)).Bold();
            });
        });

    private static void Loaded(IContainer container, BillOfLadingData data) =>
        Box(container).Column(column =>
        {
            column.Item().PaddingBottom(4).Text("Containers recorded against this booking").FontSize(8).FontColor(Muted).SemiBold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(80);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Container number");
                    header.Cell().Element(HeaderCell).Text("Type");
                });

                foreach (var loaded in data.Loaded)
                {
                    table.Cell().Element(BodyCell).Text(loaded.Number).SemiBold();
                    table.Cell().Element(BodyCell).Text(loaded.TypeCode);
                }
            });
        });

    private static void Footer(IContainer container, BillOfLadingData data) =>
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(6).Column(column =>
        {
            column.Item().Text(
                "Demonstration document produced by BoxLine, a portfolio project. It is not a negotiable instrument or a legal bill of lading.")
                .FontSize(8).FontColor(Muted);
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"Generated {FormatTime(data.GeneratedAt)}").FontSize(8).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        });

    private static void Field(IContainer container, string label, string value, string? secondary = null) =>
        Box(container).Column(column =>
        {
            column.Item().Text(label).FontSize(8).FontColor(Muted).SemiBold();
            column.Item().Text(value).Bold();
            if (!string.IsNullOrWhiteSpace(secondary))
            {
                column.Item().Text(secondary).FontColor(Muted);
            }
        });

    private static IContainer Box(IContainer container) =>
        container.Border(0.75f).BorderColor(Colors.Grey.Medium).Padding(8);

    private static IContainer HeaderCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(3).DefaultTextStyle(style => style.SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3);

    private static string FormatTime(DateTime utc) => utc.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";
}
