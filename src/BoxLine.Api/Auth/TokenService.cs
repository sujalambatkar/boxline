using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BoxLine.Api.Configuration;
using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BoxLine.Api.Auth;

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAt);

/// <summary>Issues signed JWTs and reads the calling user back from a validated principal.</summary>
public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public const string RoleClaim = "role";
    public const string CompanyClaim = "company";

    private readonly JwtOptions jwt = options.Value;

    public IssuedToken Issue(User user)
    {
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(jwt.ExpiryMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(RoleClaim, user.Role.ToString()),
        };
        if (!string.IsNullOrEmpty(user.ShipperCompany))
        {
            claims.Add(new Claim(CompanyClaim, user.ShipperCompany));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: clock.GetUtcNow().UtcDateTime,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>Builds the <see cref="Actor"/> for the authenticated caller from their token claims.</summary>
    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var role = principal.FindFirstValue(TokenService.RoleClaim);

        if (!int.TryParse(id, out var userId) || !Enum.TryParse<UserRole>(role, out var userRole))
        {
            throw new InvalidOperationException("The token does not carry a valid user id and role.");
        }

        return new Actor(userId, userRole);
    }
}
