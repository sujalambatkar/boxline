using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IUserRepository users,
    IPasswordHasher<User> passwordHasher,
    TokenService tokens,
    TimeProvider clock) : ControllerBase
{
    // Verified against when the e-mail is unknown, so a miss costs the same time as a wrong password.
    private static readonly User Decoy = new();

    /// <summary>Self-service sign-up. Always creates a Shipper; Ops accounts are created by BoxLine staff.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (!request.Password.Any(char.IsLetter) || !request.Password.Any(char.IsDigit))
        {
            ModelState.AddModelError(nameof(request.Password), "Password must contain at least one letter and one digit.");
            return ValidationProblem(ModelState);
        }

        var email = NormalizeEmail(request.Email);
        if (await users.GetByEmailAsync(email, ct) is not null)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "E-mail already registered.",
                Detail = "An account with this e-mail address already exists.",
            });
        }

        var user = new User
        {
            Email = email,
            Role = UserRole.Shipper,
            ShipperCompany = request.ShipperCompany.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        await users.AddAsync(user, ct);

        return CreatedAtAction(nameof(Me), null, ToAuthResponse(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(NormalizeEmail(request.Email), ct);
        var verification = passwordHasher.VerifyHashedPassword(
            user ?? Decoy,
            user?.PasswordHash ?? passwordHasher.HashPassword(Decoy, "decoy-password-1"),
            request.Password);

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid e-mail or password.",
            });
        }

        return ToAuthResponse(user);
    }

    /// <summary>The signed-in user, as recorded in the database.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var user = await users.GetByIdAsync(User.ToActor().UserId, ct);
        return user is null ? Unauthorized() : UserResponse.From(user);
    }

    private AuthResponse ToAuthResponse(User user)
    {
        var token = tokens.Issue(user);
        return new AuthResponse(token.AccessToken, token.ExpiresAt, UserResponse.From(user));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
