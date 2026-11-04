using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class AuthEndpointTests(BoxLineApiFactory factory)
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Register_CreatesAShipper_ReturnsAUsableToken()
    {
        var client = factory.CreateClient();
        var email = NewEmail().ToUpperInvariant();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd-ok", "  Acme Ltd "));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        auth.User.Role.Should().Be(UserRole.Shipper);
        auth.User.Email.Should().Be(email.ToLowerInvariant());
        auth.User.ShipperCompany.Should().Be("Acme Ltd");
        auth.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me", TestJson.Options);
        me!.Id.Should().Be(auth.User.Id);
    }

    [Fact]
    public async Task Token_CarriesTheUserIdAndRole()
    {
        var shipper = await factory.RegisterShipperAsync();
        var token = shipper.Client.DefaultRequestHeaders.Authorization!.Parameter!;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Subject.Should().Be(shipper.User.Id.ToString());
        jwt.Claims.Single(c => c.Type == "role").Value.Should().Be("Shipper");
    }

    [Fact]
    public async Task Register_RejectsADuplicateEmailIgnoringCase()
    {
        var shipper = await factory.RegisterShipperAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(shipper.Email.ToUpperInvariant(), "Passw0rd-ok", "Other Co"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("not-an-email", "Passw0rd-ok", "Acme")]
    [InlineData("a@test.local", "short1", "Acme")]
    [InlineData("a@test.local", "onlyletters", "Acme")]
    [InlineData("a@test.local", "12345678", "Acme")]
    [InlineData("a@test.local", "Passw0rd-ok", "")]
    public async Task Register_RejectsInvalidInput(string email, string password, string company)
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, company));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_CannotCreateAnOpsAccount()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "Passw0rd-ok", shipperCompany = "Sneaky", role = "Ops" });

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        auth.User.Role.Should().Be(UserRole.Shipper);
    }

    [Fact]
    public async Task Login_SucceedsWithTheRightPassword_IgnoringEmailCase()
    {
        var shipper = await factory.RegisterShipperAsync();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(shipper.Email.ToUpperInvariant(), shipper.Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!.User.Id.Should().Be(shipper.User.Id);
    }

    [Fact]
    public async Task Login_GivesTheSameAnswerForAWrongPasswordAndAnUnknownEmail()
    {
        var shipper = await factory.RegisterShipperAsync();
        var client = factory.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(shipper.Email, "Wrong-pass-1"));
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(NewEmail(), "Wrong-pass-1"));

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var a = await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>();
        var b = await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>();
        a!.Title.Should().Be(b!.Title).And.Be("Invalid e-mail or password.");
    }

    [Fact]
    public async Task Me_RequiresAValidToken()
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_RejectsATokenWithATamperedSignature()
    {
        var shipper = await factory.RegisterShipperAsync();
        var token = shipper.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^3] + (token.EndsWith("AAA") ? "BBB" : "AAA");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
