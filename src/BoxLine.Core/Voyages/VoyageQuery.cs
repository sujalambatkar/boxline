using BoxLine.Core.Entities;

namespace BoxLine.Core.Voyages;

public sealed record VoyageQuery(
    VoyageStatus? Status = null,
    int? VesselId = null,
    int? PortId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20);
