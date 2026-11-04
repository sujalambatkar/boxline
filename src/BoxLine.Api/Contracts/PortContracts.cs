using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

public sealed record PortRequest(
    [Required, StringLength(5, MinimumLength = 5)] string UnLocode,
    [Required, StringLength(100)] string Name,
    [Required, StringLength(2, MinimumLength = 2)] string Country);

public sealed record PortResponse(int Id, string UnLocode, string Name, string Country)
{
    public static PortResponse From(Port port) => new(port.Id, port.UnLocode, port.Name, port.Country);
}
