using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

public sealed record ContainerTypeRequest(
    [Required, RegularExpression(@"^\d{2}[A-Za-z]{2}$", ErrorMessage = "Code must be two digits followed by two letters, e.g. 40HC.")] string Code,
    [Range(10, 60)] int LengthFt,
    [Required, StringLength(200)] string Description);

public sealed record ContainerTypeResponse(int Id, string Code, int LengthFt, string Description)
{
    public static ContainerTypeResponse From(ContainerType type) => new(type.Id, type.Code, type.LengthFt, type.Description);
}
