using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;

namespace BoxLine.Api.Contracts;

public sealed record VesselRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required] string ImoNumber);

public sealed record VesselResponse(int Id, string Name, string ImoNumber)
{
    public static VesselResponse From(Vessel vessel) => new(vessel.Id, vessel.Name, vessel.ImoNumber);
}

/// <param name="Eta">Estimated arrival (UTC). Required except at the first port.</param>
/// <param name="Etd">Estimated departure (UTC). Required except at the last port.</param>
public sealed record CallRequest(int Sequence, int PortId, DateTime? Eta, DateTime? Etd);

public sealed record VoyageRequest(
    int VesselId,
    [Required] string VoyageNumber,
    [Required] List<CallRequest> Calls);

public sealed record ActualsRequest(DateTime? Ata, DateTime? Atd);

public sealed record VoyageStatusRequest(VoyageStatus Status);

public sealed record CallResponse(
    int Id,
    int Sequence,
    PortSummary Port,
    DateTime? Eta,
    DateTime? Etd,
    DateTime? Ata,
    DateTime? Atd)
{
    public static CallResponse From(VoyageCall call) => new(
        call.Id,
        call.Sequence,
        PortSummary.From(call.Port),
        Utc(call.Eta),
        Utc(call.Etd),
        Utc(call.Ata),
        Utc(call.Atd));

    internal static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}

public sealed record VesselSummary(int Id, string Name, string ImoNumber);

public sealed record VoyageResponse(
    int Id,
    string VoyageNumber,
    VoyageStatus Status,
    IReadOnlyList<VoyageStatus> AllowedTransitions,
    VesselSummary Vessel,
    IReadOnlyList<CallResponse> Calls)
{
    public static VoyageResponse From(Voyage voyage) => new(
        voyage.Id,
        voyage.VoyageNumber,
        voyage.Status,
        VoyageWorkflow.NextStatuses(voyage.Status),
        new VesselSummary(voyage.VesselId, voyage.Vessel?.Name ?? string.Empty, voyage.Vessel?.ImoNumber ?? string.Empty),
        voyage.Calls.OrderBy(c => c.Sequence).Select(CallResponse.From).ToList());
}

/// <summary>A sailing that connects the requested ports, as shown to a shipper choosing a voyage.</summary>
public sealed record ScheduleResponse(
    int VoyageId,
    string VoyageNumber,
    string VesselName,
    string ImoNumber,
    PortSummary Pol,
    PortSummary Pod,
    DateTime Departure,
    DateTime Arrival,
    double TransitDays)
{
    public static ScheduleResponse From(ScheduleOption option)
    {
        var departure = DateTime.SpecifyKind(option.Pol.Etd!.Value, DateTimeKind.Utc);
        var arrival = DateTime.SpecifyKind(option.Pod.Eta!.Value, DateTimeKind.Utc);

        return new ScheduleResponse(
            option.Voyage.Id,
            option.Voyage.VoyageNumber,
            option.Voyage.Vessel?.Name ?? string.Empty,
            option.Voyage.Vessel?.ImoNumber ?? string.Empty,
            PortSummary.From(option.Pol.Port),
            PortSummary.From(option.Pod.Port),
            departure,
            arrival,
            Math.Round((arrival - departure).TotalDays, 1));
    }
}
