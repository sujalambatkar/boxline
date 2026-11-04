using BoxLine.Core.Entities;

namespace BoxLine.Core.Common;

/// <summary>The authenticated user performing an operation.</summary>
public sealed record Actor(int UserId, UserRole Role)
{
    public bool IsOps => Role == UserRole.Ops;
}
