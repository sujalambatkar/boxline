using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BoxLine.Tests.Api;

public sealed record Session(HttpClient Client, UserResponse User, string Email, string Password);

/// <summary>Creates throw-away accounts in the isolated test database and signs them in.</summary>
public static class TestAccounts
{
    public const string Password = "Test-pass-123";

    /// <summary>Registers a brand-new shipper through the public API and returns a client signed in as them.</summary>
    public static async Task<Session> RegisterShipperAsync(this BoxLineApiFactory factory, string? company = null)
    {
        var email = $"shipper-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, Password, company ?? "Acme Exports"));
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new Session(client, auth.User, email, Password);
    }

    /// <summary>Creates an Ops user directly in the database (there is no public way to) and signs in through the API.</summary>
    public static async Task<HttpClient> CreateOpsClientAsync(this BoxLineApiFactory factory)
    {
        var email = $"ops-{Guid.NewGuid():N}@test.local";

        using (var scope = factory.Services.CreateScope())
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
            var user = new User { Email = email, Role = UserRole.Ops, CreatedAt = DateTime.UtcNow };
            user.PasswordHash = hasher.HashPassword(user, Password);
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user, CancellationToken.None);
        }

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }
}
