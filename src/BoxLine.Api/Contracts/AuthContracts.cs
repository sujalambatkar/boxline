using System.ComponentModel.DataAnnotations;
using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [Required, StringLength(200, MinimumLength = 2)] string ShipperCompany);

public sealed record LoginRequest(
    [Required] string Email,
    [Required] string Password);

public sealed record UserResponse(int Id, string Email, UserRole Role, string? ShipperCompany)
{
    public static UserResponse From(User user) => new(user.Id, user.Email, user.Role, user.ShipperCompany);
}

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAt, UserResponse User);
