using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Core.Ports;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

[ApiController]
[Route("api/ports")]
public sealed class PortsController(IPortRepository ports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PortResponse>>> List([FromQuery] string? search, CancellationToken ct)
    {
        var result = await ports.ListAsync(search, ct);
        return Ok(result.Select(PortResponse.From).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PortResponse>> Get(int id, CancellationToken ct)
    {
        var port = await ports.GetByIdAsync(id, ct);
        return port is null ? NotFound() : PortResponse.From(port);
    }

    [HttpPost]
    public async Task<ActionResult<PortResponse>> Create(PortRequest request, CancellationToken ct)
    {
        var problem = await ValidateAsync(request, excludeId: null, ct);
        if (problem is not null)
        {
            return problem;
        }

        var port = new Port();
        Apply(port, request);
        await ports.AddAsync(port, ct);

        return CreatedAtAction(nameof(Get), new { id = port.Id }, PortResponse.From(port));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<PortResponse>> Update(int id, PortRequest request, CancellationToken ct)
    {
        var port = await ports.GetByIdAsync(id, ct);
        if (port is null)
        {
            return NotFound();
        }

        var problem = await ValidateAsync(request, excludeId: id, ct);
        if (problem is not null)
        {
            return problem;
        }

        Apply(port, request);
        await ports.UpdateAsync(port, ct);

        return PortResponse.From(port);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var port = await ports.GetByIdAsync(id, ct);
        if (port is null)
        {
            return NotFound();
        }

        await ports.DeleteAsync(port, ct);
        return NoContent();
    }

    private async Task<ActionResult?> ValidateAsync(PortRequest request, int? excludeId, CancellationToken ct)
    {
        var unLocode = UnLocode.Normalize(request.UnLocode);
        if (!UnLocode.IsValid(unLocode))
        {
            return ValidationProblem(nameof(request.UnLocode),
                "UN/LOCODE must be two letters followed by three letters or digits 2-9, e.g. INNSA.");
        }

        if (!string.Equals(request.Country.Trim(), unLocode[..2], StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem(nameof(request.Country),
                $"Country must match the first two letters of the UN/LOCODE ({unLocode[..2]}).");
        }

        if (await ports.UnLocodeExistsAsync(unLocode, excludeId, ct))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Port already exists.",
                Detail = $"A port with UN/LOCODE {unLocode} already exists.",
            });
        }

        return null;
    }

    private static void Apply(Port port, PortRequest request)
    {
        port.UnLocode = UnLocode.Normalize(request.UnLocode);
        port.Name = request.Name.Trim();
        port.Country = port.UnLocode[..2];
    }

    private ActionResult ValidationProblem(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
