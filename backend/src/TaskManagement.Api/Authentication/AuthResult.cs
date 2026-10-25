namespace TaskManagement.Api.Authentication;

/// <summary>Outcome of a password operation; <see cref="Errors"/> is keyed by request field name.</summary>
public sealed record AuthResult(bool Succeeded, IReadOnlyDictionary<string, string[]> Errors)
{
    public static AuthResult Success { get; } = new(true, new Dictionary<string, string[]>());

    public static AuthResult Failure(string field, string message) =>
        new(false, new Dictionary<string, string[]> { [field] = [message] });
}
