namespace TaskManagement.Api.Data;

/// <summary>
/// Server-side authentication session. The browser cookie only carries a protected random key;
/// this row holds the authentication ticket so sessions can be revoked on logout or password change.
/// </summary>
public class UserSession
{
    /// <summary>SHA-256 hash (hex) of the session key held in the cookie. The raw key is never stored.</summary>
    public required string Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Data-protected, serialized authentication ticket.</summary>
    public required byte[] Ticket { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastRenewedAtUtc { get; set; }

    /// <summary>Sliding expiry; renewed while the session is in use.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Hard upper bound that sliding renewal can never extend.</summary>
    public DateTime AbsoluteExpiresAtUtc { get; set; }
}
