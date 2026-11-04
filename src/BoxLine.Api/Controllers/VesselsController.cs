using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Core.Vessels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Vessel master data. Operations staff only.</summary>
[ApiController]
[Authorize(Roles = Roles.Ops)]
[Route("api/vessels")]
public sealed class VesselsController(IVesselRepository vessels) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VesselResponse>>> List([FromQuery] string? search, CancellationToken ct) =>
        (await vessels.ListAsync(search, ct)).Select(VesselResponse.From).ToList();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VesselResponse>> Get(int id, CancellationToken ct)
    {
        var vessel = await vessels.GetByIdAsync(id, ct);
        return vessel is null ? NotFound() : VesselResponse.From(vessel);
    }

    /// <summary>Registers a vessel. The IMO number is validated (seven digits with a correct check digit) and normalized.</summary>
    [HttpPost]
    public async Task<ActionResult<VesselResponse>> Create(VesselRequest request, CancellationToken ct)
    {
        var problem = await ValidateAsync(request, excludeId: null, ct);
        if (problem is not null)
        {
            return problem;
        }

        var vessel = new Vessel { Name = request.Name.Trim(), ImoNumber = ImoNumber.Normalize(request.ImoNumber) };
        await vessels.AddAsync(vessel, ct);

        return CreatedAtAction(nameof(Get), new { id = vessel.Id }, VesselResponse.From(vessel));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<VesselResponse>> Update(int id, VesselRequest request, CancellationToken ct)
    {
        var vessel = await vessels.GetByIdAsync(id, ct);
        if (vessel is null)
        {
            return NotFound();
        }

        var problem = await ValidateAsync(request, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        vessel.Name = request.Name.Trim();
        vessel.ImoNumber = ImoNumber.Normalize(request.ImoNumber);
        await vessels.UpdateAsync(vessel, ct);

        return VesselResponse.From(vessel);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var vessel = await vessels.GetByIdAsync(id, ct);
        if (vessel is null)
        {
            return NotFound();
        }

        if (await vessels.HasVoyagesAsync(id, ct))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Vessel has voyages.",
                Detail = $"{vessel.Name} has voyages and cannot be deleted.",
            });
        }

        await vessels.DeleteAsync(vessel, ct);
        return NoContent();
    }

    private async Task<ActionResult?> ValidateAsync(VesselRequest request, int? excludeId, CancellationToken ct)
    {
        var error = ImoNumber.Validate(request.ImoNumber);
        if (error is not null)
        {
            ModelState.AddModelError(nameof(request.ImoNumber), error);
            return ValidationProblem(ModelState);
        }

        var imo = ImoNumber.Normalize(request.ImoNumber);
        if (await vessels.ImoExistsAsync(imo, excludeId, ct))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Vessel already registered.",
                Detail = $"A vessel with IMO number {imo} already exists.",
            });
        }

        return null;
    }
}
