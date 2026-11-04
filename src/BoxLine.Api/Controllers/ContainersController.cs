using BoxLine.Api.Contracts;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

[ApiController]
[Route("api/containers")]
public sealed class ContainersController(IContainerRepository containers, IContainerTypeRepository types) : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <summary>Lists containers with server-side search, filtering, sorting and paging.</summary>
    /// <param name="search">Partial container number; formatting and case are ignored.</param>
    /// <param name="sortBy">number, type, ownership or status.</param>
    /// <param name="sortDirection">asc or desc.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ContainerResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] int? containerTypeId,
        [FromQuery] ContainerStatus? status,
        [FromQuery] string sortBy = "number",
        [FromQuery] string sortDirection = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TryParseSortField(sortBy, out var sortField))
        {
            return BadRequestFor(nameof(sortBy), "sortBy must be one of: number, type, ownership, status.");
        }

        var query = new ContainerQuery(
            search,
            containerTypeId,
            status,
            sortField,
            Descending: string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase),
            Page: Math.Max(1, page),
            PageSize: Math.Clamp(pageSize, 1, MaxPageSize));

        var result = await containers.SearchAsync(query, ct);

        return new PagedResponse<ContainerResponse>(
            result.Items.Select(ContainerResponse.From).ToList(),
            result.TotalCount,
            result.Page,
            result.PageSize);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ContainerResponse>> Get(int id, CancellationToken ct)
    {
        var container = await containers.GetByIdAsync(id, ct);
        return container is null ? NotFound() : ContainerResponse.From(container);
    }

    [HttpPost]
    public async Task<ActionResult<ContainerResponse>> Create(CreateContainerRequest request, CancellationToken ct)
    {
        var error = Iso6346Validator.Validate(request.Number);
        if (error is not null)
        {
            return BadRequestFor(nameof(request.Number), error);
        }

        var number = Iso6346Validator.Normalize(request.Number);
        if (await containers.NumberExistsAsync(number, ct))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Container already exists.",
                Detail = $"Container {number} is already registered.",
            });
        }

        if (await types.GetByIdAsync(request.ContainerTypeId, ct) is null)
        {
            return BadRequestFor(nameof(request.ContainerTypeId), "Container type does not exist.");
        }

        var container = new Container
        {
            Number = number,
            ContainerTypeId = request.ContainerTypeId,
            OwnershipType = request.OwnershipType,
            Status = request.Status,
        };
        await containers.AddAsync(container, ct);

        var created = await containers.GetByIdAsync(container.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = container.Id }, ContainerResponse.From(created!));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ContainerResponse>> Update(int id, UpdateContainerRequest request, CancellationToken ct)
    {
        var container = await containers.GetByIdAsync(id, ct);
        if (container is null)
        {
            return NotFound();
        }

        var type = await types.GetByIdAsync(request.ContainerTypeId, ct);
        if (type is null)
        {
            return BadRequestFor(nameof(request.ContainerTypeId), "Container type does not exist.");
        }

        container.ContainerTypeId = type.Id;
        container.ContainerType = type;
        container.OwnershipType = request.OwnershipType;
        container.Status = request.Status;
        await containers.UpdateAsync(container, ct);

        return ContainerResponse.From(container);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var container = await containers.GetByIdAsync(id, ct);
        if (container is null)
        {
            return NotFound();
        }

        await containers.DeleteAsync(container, ct);
        return NoContent();
    }

    private static bool TryParseSortField(string value, out ContainerSortField field)
    {
        field = ContainerSortField.Number;
        return Enum.TryParse(value, ignoreCase: true, out field) && Enum.IsDefined(field);
    }

    private ActionResult BadRequestFor(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
