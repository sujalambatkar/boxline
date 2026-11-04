namespace BoxLine.Api.Configuration;

/// <summary>Settings used to issue and validate JSON Web Tokens.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    /// <summary>Symmetric signing key. Supplied through user-secrets, never committed.</summary>
    public string Secret { get; init; } = string.Empty;

    public int ExpiryMinutes { get; init; } = 60;
}
