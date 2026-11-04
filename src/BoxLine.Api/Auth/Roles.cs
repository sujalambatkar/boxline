namespace BoxLine.Api.Auth;

/// <summary>Role names as they appear in the JWT <c>role</c> claim and in <c>[Authorize(Roles = ...)]</c>.</summary>
public static class Roles
{
    public const string Ops = "Ops";
    public const string Shipper = "Shipper";
}
