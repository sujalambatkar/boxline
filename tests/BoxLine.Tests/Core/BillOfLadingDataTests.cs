using BoxLine.Core.Documents;
using BoxLine.Core.Entities;
using FluentAssertions;

namespace BoxLine.Tests.Core;

public class BillOfLadingDataTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 14, 30, 0, DateTimeKind.Utc);

    private static Booking NewBooking(BookingStatus status = BookingStatus.Confirmed) => new()
    {
        Id = 5,
        BookingNumber = "BL-202611-0005",
        Status = status,
        ShipperId = 1,
        Shipper = new User { Id = 1, Email = "ship@acme.test", ShipperCompany = "Acme Exports" },
        PolId = 1,
        Pol = new Port { Id = 1, UnLocode = "INNSA", Name = "Nhava Sheva" },
        PodId = 4,
        Pod = new Port { Id = 4, UnLocode = "NLRTM", Name = "Rotterdam" },
        CargoDescription = "Basmati rice",
        CreatedAt = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
        ConfirmedAt = new DateTime(2026, 11, 2, 23, 50, 0, DateTimeKind.Utc),
        Lines =
        [
            new() { Quantity = 3, ContainerType = new ContainerType { Id = 2, Code = "40HC", LengthFt = 40, Description = "40' high cube" } },
            new() { Quantity = 2, ContainerType = new ContainerType { Id = 1, Code = "20GP", LengthFt = 20, Description = "20' general purpose" } },
        ],
    };

    private static ContainerMovement Move(int containerId, string number, string type) => new()
    {
        Container = new Container { Id = containerId, Number = number, ContainerType = new ContainerType { Code = type } },
    };

    [Fact]
    public void AConfirmedBooking_IsNotADraft_AndIsDatedByItsConfirmation()
    {
        var data = BillOfLadingData.From(NewBooking(), [], Now);

        data.IsDraft.Should().BeFalse();
        data.IssueDate.Should().Be(new DateOnly(2026, 11, 2), "the confirmation day, not the day the PDF was generated");
        data.GeneratedAt.Should().Be(Now);
    }

    [Fact]
    public void ADraftBooking_IsMarkedDraft_AndHasNoIssueDate()
    {
        var booking = NewBooking(BookingStatus.Draft);
        booking.ConfirmedAt = null;

        var data = BillOfLadingData.From(booking, [], Now);

        data.IsDraft.Should().BeTrue();
        data.IssueDate.Should().BeNull();
        data.Status.Should().Be(BookingStatus.Draft);
    }

    [Fact]
    public void ABookingConfirmedBeforeTheDateWasRecorded_FallsBackToItsLastUpdate()
    {
        var booking = NewBooking();
        booking.ConfirmedAt = null;

        BillOfLadingData.From(booking, [], Now).IssueDate.Should().Be(new DateOnly(2026, 11, 3));
    }

    [Fact]
    public void ShipperAndPorts_AreTakenFromTheBooking()
    {
        var data = BillOfLadingData.From(NewBooking(), [], Now);

        data.BookingNumber.Should().Be("BL-202611-0005");
        data.ShipperName.Should().Be("Acme Exports");
        data.ShipperEmail.Should().Be("ship@acme.test");
        data.PortOfLoading.Should().Be(new BolPort("INNSA", "Nhava Sheva"));
        data.PortOfDischarge.Should().Be(new BolPort("NLRTM", "Rotterdam"));
        data.CargoDescription.Should().Be("Basmati rice");
    }

    [Fact]
    public void TheShipperName_FallsBackToTheEmail_WhenThereIsNoCompany()
    {
        var booking = NewBooking();
        booking.Shipper!.ShipperCompany = null;

        BillOfLadingData.From(booking, [], Now).ShipperName.Should().Be("ship@acme.test");
    }

    [Fact]
    public void BookedContainers_AreListedSmallestFirst_AndTotalled()
    {
        var data = BillOfLadingData.From(NewBooking(), [], Now);

        data.Booked.Select(b => (b.TypeCode, b.Quantity)).Should().Equal(("20GP", 2), ("40HC", 3));
        data.TotalBooked.Should().Be(5);
    }

    [Fact]
    public void ARecordedContainer_IsListedOnce_EvenWithManyMovements_InNumberOrder()
    {
        var movements = new[] { Move(2, "TSTU0000030", "40HC"), Move(1, "BXLU0000030", "20GP"), Move(2, "TSTU0000030", "40HC"), Move(1, "BXLU0000030", "20GP") };

        var data = BillOfLadingData.From(NewBooking(), movements, Now);

        data.Loaded.Should().Equal(new BolLoadedContainer("BXLU0000030", "20GP"), new BolLoadedContainer("TSTU0000030", "40HC"));
    }

    [Fact]
    public void WithoutAVoyage_ThereIsNoVoyageBlock() => BillOfLadingData.From(NewBooking(), [], Now).Voyage.Should().BeNull();

    [Fact]
    public void AVoyage_ShowsTheVesselAndTheDatesAtThisBookingsPorts()
    {
        var booking = NewBooking();
        booking.Voyage = new Voyage
        {
            VoyageNumber = "BXL2612A1",
            Vessel = new Vessel { Name = "MV Aurora", ImoNumber = "9900019" },
            Calls =
            [
                new() { PortId = 1, Sequence = 1, Etd = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc) },
                new() { PortId = 2, Sequence = 2, Eta = new DateTime(2026, 12, 5, 8, 0, 0, DateTimeKind.Utc), Etd = new DateTime(2026, 12, 6, 8, 0, 0, DateTimeKind.Utc) },
                new() { PortId = 4, Sequence = 3, Eta = new DateTime(2026, 12, 20, 8, 0, 0, DateTimeKind.Utc) },
            ],
        };

        var voyage = BillOfLadingData.From(booking, [], Now).Voyage!;

        voyage.VoyageNumber.Should().Be("BXL2612A1");
        voyage.VesselName.Should().Be("MV Aurora");
        voyage.ImoNumber.Should().Be("9900019");
        voyage.Departure.Should().Be(new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc));
        voyage.Arrival.Should().Be(new DateTime(2026, 12, 20, 8, 0, 0, DateTimeKind.Utc));
    }
}
