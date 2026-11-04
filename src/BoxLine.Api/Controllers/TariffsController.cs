using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Detention;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Detention and demurrage tariffs. Everyone signed in can read them; only Ops can change them.</summary>
[ApiController]
[Authorize]
[Route("api/tariffs")]
public sealed class TariffsController(DetentionService detention) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TariffResponse>>> List([FromQuery] int? containerTypeId, CancellationToken ct) =>
        (await detention.ListTariffsAsync(containerTypeId, ct)).Select(TariffResponse.From).ToList();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TariffResponse>> Get(int id, CancellationToken ct) =>
        TariffResponse.From(await detention.GetTariffAsync(id, ct));

    /// <summary>
    /// Creates a tariff. Slabs use absolute day numbers: the first starts the day after the free days, each
    /// starts where the previous ended, and the last is open-ended. Validity periods of one container type cannot overlap.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Ops)]
    public async Task<ActionResult<TariffResponse>> Create(TariffRequest request, CancellationToken ct)
    {
        var tariff = await detention.CreateTariffAsync(ToCommand(request), ct);
        return CreatedAtAction(nameof(Get), new { id = tariff.Id }, TariffResponse.From(tariff));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Ops)]
    public async Task<ActionResult<TariffResponse>> Update(int id, TariffRequest request, CancellationToken ct) =>
        TariffResponse.From(await detention.UpdateTariffAsync(id, ToCommand(request), ct));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Ops)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await detention.DeleteTariffAsync(id, ct);
        return NoContent();
    }

    private static SaveTariffCommand ToCommand(TariffRequest request) => new(
        request.ContainerTypeId,
        request.FreeDays,
        request.ValidFrom,
        request.ValidTo,
        request.Slabs.Select(s => new SlabDraft(s.FromDay, s.ToDay, s.RatePerDayUsd)).ToList());
}
