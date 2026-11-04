using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using FluentAssertions;

namespace BoxLine.Tests.Api;

/// <summary>Builders for the records the movement, tracking and chat tests need.</summary>
public static class TestData
{
    /// <summary>The eight events of one full container cycle, in order.</summary>
    public static readonly MovementEventType[] Cycle = Enum.GetValues<MovementEventType>();

    /// <summary>Registers a new container under a prefix the seeder never uses and returns its number.</summary>
    public static async Task<string> NewContainerAsync(HttpClient ops)
    {
        var types = (await ops.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;
        var body = $"TSTU{Random.Shared.Next(0, 1_000_000):D6}";
        var number = body + Iso6346Validator.ComputeCheckDigit(body);

        var response = await ops.PostAsJsonAsync("/api/containers", new CreateContainerRequest(number, types[0].Id, OwnershipType.Owned));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return number;
    }

    /// <summary>Creates a booking for the shipper and confirms it.</summary>
    public static async Task<BookingResponse> ConfirmedBookingAsync(Session shipper, bool confirm = true)
    {
        var ports = (await shipper.Client.GetFromJsonAsync<List<PortResponse>>("/api/ports"))!;
        var types = (await shipper.Client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;

        var create = await shipper.Client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(
            ports.Single(p => p.UnLocode == "INNSA").Id,
            ports.Single(p => p.UnLocode == "NLRTM").Id,
            "Auto parts",
            [new BookingLineRequest(types[0].Id, 2)]));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await create.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;

        if (!confirm)
        {
            return booking;
        }

        var confirmed = await shipper.Client.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        confirmed.EnsureSuccessStatusCode();
        return (await confirmed.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;
    }

    public static Task<HttpResponseMessage> RecordAsync(
        HttpClient ops, string container, MovementEventType type, DateTime at, string? booking = null, string location = "INNSA") =>
        ops.PostAsJsonAsync("/api/movements", new RecordMovementRequest(container, type, location, at, booking));

    public static async Task<ContainerTrackingResponse> TrackAsync(HttpClient client, string container) =>
        (await client.GetFromJsonAsync<ContainerTrackingResponse>($"/api/tracking/containers/{container}", TestJson.Options))!;

    /// <summary>A moment safely in the past, so a sequence of events one hour apart never reaches the future.</summary>
    public static DateTime Start() => DateTime.UtcNow.AddDays(-2);
}
