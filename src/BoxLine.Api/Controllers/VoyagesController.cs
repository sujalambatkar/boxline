using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Voyages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Voyages and their port rotations. Operations staff only.</summary>
[ApiController]
[Authorize(Roles = Roles.Ops)]
[Route("api/voyages")]
public sealed class VoyagesController(VoyageService voyages) : ControllerBase
{
    /// <summary>Lists voyages, newest departure first.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<VoyageResponse>>> List(
        [FromQuery] VoyageStatus? status,
        [FromQuery] int? vesselId,
        [FromQuery] int? portId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await voyages.SearchAsync(new VoyageQuery(status, vesselId, portId, search, page, pageSize), ct);

        return new PagedResponse<VoyageResponse>(
            result.Items.Select(VoyageResponse.From).ToList(), result.TotalCount, result.Page, result.PageSize);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VoyageResponse>> Get(int id, CancellationToken ct) =>
        VoyageResponse.From(await voyages.GetAsync(id, ct));

    /// <summary>Creates a scheduled voyage with its whole rotation.</summary>
    [HttpPost]
    public async Task<ActionResult<VoyageResponse>> Create(VoyageRequest request, CancellationToken ct)
    {
        var voyage = await voyages.CreateAsync(new CreateVoyageCommand(request.VesselId, request.VoyageNumber, ToDrafts(request.Calls)), ct);
        return CreatedAtAction(nameof(Get), new { id = voyage.Id }, VoyageResponse.From(voyage));
    }

    /// <summary>Replaces the vessel, number and rotation of a scheduled voyage.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<VoyageResponse>> Update(int id, VoyageRequest request, CancellationToken ct) =>
        VoyageResponse.From(await voyages.UpdateAsync(id, new UpdateVoyageCommand(request.VesselId, request.VoyageNumber, ToDrafts(request.Calls)), ct));

    /// <summary>Records the actual arrival and/or departure at a call while the voyage is underway.</summary>
    [HttpPut("{id:int}/calls/{callId:int}/actuals")]
    public async Task<ActionResult<VoyageResponse>> RecordActuals(int id, int callId, ActualsRequest request, CancellationToken ct) =>
        VoyageResponse.From(await voyages.RecordActualsAsync(id, callId, new RecordActualsCommand(request.Ata, request.Atd), ct));

    /// <summary>Moves the voyage to Underway, Completed or Cancelled.</summary>
    [HttpPost("{id:int}/status")]
    public async Task<ActionResult<VoyageResponse>> ChangeStatus(int id, VoyageStatusRequest request, CancellationToken ct) =>
        VoyageResponse.From(await voyages.ChangeStatusAsync(id, request.Status, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await voyages.DeleteAsync(id, ct);
        return NoContent();
    }

    private static List<CallDraft> ToDrafts(IEnumerable<CallRequest> calls) =>
        calls.Select(c => new CallDraft(c.Sequence, c.PortId, c.Eta, c.Etd)).ToList();
}
