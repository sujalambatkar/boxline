using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Vessels;
using FluentAssertions;

namespace BoxLine.Tests.Api;

/// <summary>Builders for the records the movement, tracking and chat tests need.</summary>
public static class TestData
{
    /// <summary>The eight events of one full container cycle, in order.</summary>
    public static readonly MovementEventType[] Cycle = Enum.GetValues<MovementEventType>();

    /// <summary>Registers a new container under a prefix the seeder never uses and returns its number.</summary>
    public static async Task<string> NewContainerAsync(HttpClient ops, int? typeId = null)
    {
        var types = (await ops.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;

        // The test database keeps every container earlier runs created, so a random number can occasionally collide.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var body = $"TSTU{Random.Shared.Next(0, 1_000_000):D6}";
            var number = body + Iso6346Validator.ComputeCheckDigit(body);

            var response = await ops.PostAsJsonAsync("/api/containers", new CreateContainerRequest(number, typeId ?? types[0].Id, OwnershipType.Owned));
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            return number;
        }

        throw new InvalidOperationException("Could not create a unique test container.");
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

    /// <summary>Creates a port with a random UN/LOCODE, so a test has ports no other test's voyages touch.</summary>
    public static async Task<PortResponse> NewPortAsync(HttpClient ops)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = "ZZ" + new string(Enumerable.Range(0, 3).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
            var response = await ops.PostAsJsonAsync("/api/ports", new PortRequest(code, $"Test Port {code}", "ZZ"));
            if (response.StatusCode == HttpStatusCode.Created)
            {
                return (await response.Content.ReadFromJsonAsync<PortResponse>())!;
            }
        }

        throw new InvalidOperationException("Could not create a unique test port.");
    }

    public static async Task<PortResponse[]> NewPortsAsync(HttpClient ops, int count)
    {
        var ports = new PortResponse[count];
        for (var i = 0; i < count; i++)
        {
            ports[i] = await NewPortAsync(ops);
        }

        return ports;
    }

    /// <summary>A valid seven-digit IMO number that no other test is using.</summary>
    public static string RandomImo()
    {
        var six = Random.Shared.Next(100_000, 999_999).ToString();
        return six + ImoNumber.ComputeCheckDigit(six);
    }

    public static async Task<VesselResponse> NewVesselAsync(HttpClient ops, string? name = null)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await ops.PostAsJsonAsync("/api/vessels", new VesselRequest(name ?? $"MV Test {Guid.NewGuid():N}"[..14], RandomImo()));
            if (response.StatusCode == HttpStatusCode.Created)
            {
                return (await response.Content.ReadFromJsonAsync<VesselResponse>())!;
            }
        }

        throw new InvalidOperationException("Could not create a unique test vessel.");
    }

    /// <summary>
    /// A rotation through the given ports: leaves the first <paramref name="startInDays"/> days from now, spends
    /// 12 hours in each intermediate port and takes <paramref name="legDays"/> days between ports.
    /// </summary>
    public static List<CallRequest> Rotation(IReadOnlyList<int> portIds, double startInDays = 5, double legDays = 3)
    {
        var calls = new List<CallRequest>();
        var clock = DateTime.UtcNow.Date.AddHours(8).AddDays(startInDays);
        for (var i = 0; i < portIds.Count; i++)
        {
            DateTime? eta = i == 0 ? null : clock;
            DateTime? etd = i == portIds.Count - 1 ? null : (i == 0 ? clock : clock.AddHours(12));
            calls.Add(new CallRequest(i + 1, portIds[i], eta, etd));
            clock = (etd ?? clock).AddDays(legDays);
        }

        return calls;
    }

    public static async Task<VoyageResponse> NewVoyageAsync(
        HttpClient ops, IReadOnlyList<int> portIds, int? vesselId = null, double startInDays = 5, string? number = null)
    {
        var vessel = vesselId ?? (await NewVesselAsync(ops)).Id;
        var request = new VoyageRequest(vessel, number ?? $"T{Guid.NewGuid():N}"[..12], Rotation(portIds, startInDays));

        var response = await ops.PostAsJsonAsync("/api/voyages", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<VoyageResponse>(TestJson.Options))!;
    }

    public static async Task<VoyageResponse> SetStatusAsync(HttpClient ops, int voyageId, VoyageStatus status)
    {
        var response = await ops.PostAsJsonAsync($"/api/voyages/{voyageId}/status", new VoyageStatusRequest(status));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<VoyageResponse>(TestJson.Options))!;
    }

    public static Task<HttpResponseMessage> RecordActualsAsync(HttpClient ops, VoyageResponse voyage, int callIndex, DateTime? ata, DateTime? atd) =>
        ops.PutAsJsonAsync($"/api/voyages/{voyage.Id}/calls/{voyage.Calls[callIndex].Id}/actuals", new ActualsRequest(ata, atd));

    public static async Task<BookingResponse> BookingAsync(
        Session shipper, int polId, int podId, int? voyageId = null, bool confirm = false)
    {
        var types = (await shipper.Client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))!;
        var create = await shipper.Client.PostAsJsonAsync("/api/bookings", new CreateBookingRequest(
            polId, podId, "Machine parts", [new BookingLineRequest(types[0].Id, 1)], VoyageId: voyageId));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var booking = (await create.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;

        if (!confirm)
        {
            return booking;
        }

        var confirmed = await shipper.Client.PostAsync($"/api/bookings/{booking.Id}/confirm", null);
        confirmed.EnsureSuccessStatusCode();
        return (await confirmed.Content.ReadFromJsonAsync<BookingResponse>(TestJson.Options))!;
    }

    /// <summary>A container type no seeded tariff exists for, so a test controls its tariffs completely.</summary>
    public static async Task<ContainerTypeResponse> NewContainerTypeAsync(HttpClient ops)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var code = $"{Random.Shared.Next(20, 99)}{(char)('A' + Random.Shared.Next(26))}{(char)('A' + Random.Shared.Next(26))}";
            var response = await ops.PostAsJsonAsync("/api/container-types", new ContainerTypeRequest(code, 40, $"Test type {code}"));
            if (response.StatusCode == HttpStatusCode.Created)
            {
                return (await response.Content.ReadFromJsonAsync<ContainerTypeResponse>())!;
            }
        }

        throw new InvalidOperationException("Could not create a unique test container type.");
    }

    public static readonly SlabRequest[] StandardSlabs = [new(6, 10, 40m), new(11, 15, 80m), new(16, null, 120m)];

    /// <summary>Creates a tariff (5 free days and 40/80/120 slabs unless told otherwise), valid from 2020-01-01 with no end.</summary>
    public static async Task<TariffResponse> NewTariffAsync(
        HttpClient ops, int containerTypeId, int freeDays = 5, IReadOnlyList<SlabRequest>? slabs = null,
        DateOnly? validFrom = null, DateOnly? validTo = null)
    {
        var request = new TariffRequest(containerTypeId, freeDays, validFrom ?? new DateOnly(2020, 1, 1), validTo, (slabs ?? StandardSlabs).ToList());
        var response = await ops.PostAsJsonAsync("/api/tariffs", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TariffResponse>(TestJson.Options))!;
    }

    /// <summary>The price of a period worked out with arithmetic rather than by walking the days (an independent check).</summary>
    public static decimal ExpectedAmount(DateOnly from, DateOnly to, int freeDays, IEnumerable<SlabRequest> slabs, bool weekends)
    {
        var counted = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Count(i => weekends || from.AddDays(i).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));

        return slabs.Sum(slab =>
        {
            var top = slab.ToDay is { } end ? Math.Min(counted, end) : counted;
            return Math.Max(0, top - Math.Max(slab.FromDay, freeDays + 1) + 1) * slab.RatePerDayUsd;
        });
    }
}
