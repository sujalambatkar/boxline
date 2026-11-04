using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/container-types")]
public sealed class ContainerTypesController(IContainerTypeRepository types) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContainerTypeResponse>>> List(CancellationToken ct)
    {
        var result = await types.ListAsync(ct);
        return Ok(result.Select(ContainerTypeResponse.From).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ContainerTypeResponse>> Get(int id, CancellationToken ct)
    {
        var type = await types.GetByIdAsync(id, ct);
        return type is null ? NotFound() : ContainerTypeResponse.From(type);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Ops)]
    public async Task<ActionResult<ContainerTypeResponse>> Create(ContainerTypeRequest request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await types.CodeExistsAsync(code, excludeId: null, ct))
        {
            return CodeConflict(code);
        }

        var type = new ContainerType { Code = code, LengthFt = request.LengthFt, Description = request.Description.Trim() };
        await types.AddAsync(type, ct);

        return CreatedAtAction(nameof(Get), new { id = type.Id }, ContainerTypeResponse.From(type));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Ops)]
    public async Task<ActionResult<ContainerTypeResponse>> Update(int id, ContainerTypeRequest request, CancellationToken ct)
    {
        var type = await types.GetByIdAsync(id, ct);
        if (type is null)
        {
            return NotFound();
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await types.CodeExistsAsync(code, id, ct))
        {
            return CodeConflict(code);
        }

        type.Code = code;
        type.LengthFt = request.LengthFt;
        type.Description = request.Description.Trim();
        await types.UpdateAsync(type, ct);

        return ContainerTypeResponse.From(type);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Ops)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var type = await types.GetByIdAsync(id, ct);
        if (type is null)
        {
            return NotFound();
        }

        if (await types.IsInUseAsync(id, ct))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Container type is in use.",
                Detail = $"Container type {type.Code} is assigned to containers and cannot be deleted.",
            });
        }

        await types.DeleteAsync(type, ct);
        return NoContent();
    }

    private ConflictObjectResult CodeConflict(string code) => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Container type already exists.",
        Detail = $"A container type with code {code} already exists.",
    });
}
