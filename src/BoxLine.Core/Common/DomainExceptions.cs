namespace BoxLine.Core.Common;

/// <summary>The requested record does not exist, or the caller is not allowed to know that it does.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The caller is authenticated but not allowed to perform the operation.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>The request conflicts with the current state of the data (duplicate, bad transition, stale edit).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The request failed validation; <see cref="Errors"/> maps field names to messages.</summary>
public sealed class DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public DomainValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }
}
