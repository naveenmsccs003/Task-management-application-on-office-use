namespace TaskManagement.Api.Common;

public enum ServiceStatus
{
    Ok,
    NotFound,
    Forbidden,
    Conflict,
    Invalid,
}

/// <summary>Outcome of an application-service operation, mapped to HTTP responses by the controllers.</summary>
public sealed record ServiceResult<T>(ServiceStatus Status, T? Value, string? Message, IReadOnlyDictionary<string, string[]>? Errors)
{
    public static ServiceResult<T> Ok(T value) => new(ServiceStatus.Ok, value, null, null);

    public static implicit operator ServiceResult<T>(ServiceError error) => new(error.Status, default, error.Message, error.Errors);
}

public sealed record ServiceError(ServiceStatus Status, string Message, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static ServiceError NotFound(string message = "The requested item was not found.") => new(ServiceStatus.NotFound, message);

    public static ServiceError Forbidden(string message) => new(ServiceStatus.Forbidden, message);

    public static ServiceError Conflict(string message) => new(ServiceStatus.Conflict, message);

    public static ServiceError Invalid(string field, string message) =>
        new(ServiceStatus.Invalid, message, new Dictionary<string, string[]> { [field] = [message] });

    public static ServiceError Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ServiceStatus.Invalid, "One or more validation errors occurred.", errors);
}

/// <summary>Marker for operations without a payload.</summary>
public sealed record Unit
{
    public static Unit Value { get; } = new();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public class PageQuery
{
    public const int MaxPageSize = 100;

    public string? Search { get; init; }

    public string? Sort { get; init; }

    /// <summary>"asc" or "desc".</summary>
    public string? Direction { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 10;

    public bool Descending => string.Equals(Direction, "desc", StringComparison.OrdinalIgnoreCase);

    public int SafePage => Math.Max(1, Page);

    public int SafePageSize => Math.Clamp(PageSize, 1, MaxPageSize);

    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}
