using System.Security.Cryptography;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace BoxLine.Api.Auth;

/// <summary>
/// Creates the two development accounts on first run. Passwords come from <c>Seed:OpsPassword</c> and
/// <c>Seed:ShipperPassword</c> when set (user-secrets); otherwise they are generated and written to the
/// console once, and never stored anywhere in clear text.
/// </summary>
public sealed class DevelopmentUserSeeder(
    IUserRepository users,
    IPasswordHasher<User> hasher,
    IConfiguration configuration,
    TimeProvider clock)
{
    public const string OpsEmail = "ops@boxline.test";
    public const string ShipperEmail = "shipper@boxline.test";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await users.AnyAsync(cancellationToken))
        {
            return;
        }

        var opsPassword = configuration["Seed:OpsPassword"] ?? GeneratePassword();
        var shipperPassword = configuration["Seed:ShipperPassword"] ?? GeneratePassword();

        await AddAsync(OpsEmail, opsPassword, UserRole.Ops, null, cancellationToken);
        await AddAsync(ShipperEmail, shipperPassword, UserRole.Shipper, "Demo Shipper Pvt Ltd", cancellationToken);

        Console.WriteLine();
        Console.WriteLine("=== BoxLine development accounts (shown once) ===");
        Console.WriteLine($"  Ops:     {OpsEmail}  /  {opsPassword}");
        Console.WriteLine($"  Shipper: {ShipperEmail}  /  {shipperPassword}");
        Console.WriteLine("=================================================");
        Console.WriteLine();
    }

    private async Task AddAsync(string email, string password, UserRole role, string? company, CancellationToken cancellationToken)
    {
        var user = new User
        {
            Email = email,
            Role = role,
            ShipperCompany = company,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        await users.AddAsync(user, cancellationToken);
    }

    private static string GeneratePassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = RandomNumberGenerator.GetItems<char>(alphabet, 14);
        return new string(chars) + "7a";
    }
}
