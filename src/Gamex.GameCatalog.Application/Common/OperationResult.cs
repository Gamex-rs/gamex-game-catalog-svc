namespace Gamex.GameCatalog.Application.Common;

public enum OperationStatus
{
    Ok,
    NotFound,
    Invalid,

    /// <summary>A dependency (RAWG) failed.</summary>
    DependencyFailed,

    /// <summary>A dependency is not configured.</summary>
    DependencyDisabled,
}

/// <summary>Outcome of a command; the API maps <see cref="Status"/> to an HTTP status.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Factory methods read as OperationResult<T>.Ok(value).")]
public sealed record OperationResult<T>(
    OperationStatus Status,
    T? Value = default,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static OperationResult<T> Ok(T value) => new(OperationStatus.Ok, value);

    public static OperationResult<T> NotFound(string detail) => new(OperationStatus.NotFound, Detail: detail);

    public static OperationResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(OperationStatus.Invalid, Errors: errors);

    public static OperationResult<T> DependencyFailed(string detail) => new(OperationStatus.DependencyFailed, Detail: detail);

    public static OperationResult<T> DependencyDisabled(string detail) => new(OperationStatus.DependencyDisabled, Detail: detail);
}
