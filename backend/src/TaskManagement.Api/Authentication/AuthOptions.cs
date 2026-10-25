using System.ComponentModel.DataAnnotations;

namespace TaskManagement.Api.Authentication;

/// <summary>Configurable authentication policy, bound from the "Authentication" configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Authentication";

    [Range(8, 128)]
    public int PasswordMinimumLength { get; set; } = 12;

    [Range(1, 100)]
    public int MaxFailedAccessAttempts { get; set; } = 5;

    [Range(1, 1440)]
    public int LockoutMinutes { get; set; } = 15;

    /// <summary>Idle timeout of a non-persistent ("Remember Me" unchecked) session. Sliding.</summary>
    [Range(5, 1440)]
    public int SessionIdleTimeoutMinutes { get; set; } = 120;

    /// <summary>Hard limit for a non-persistent session, regardless of activity.</summary>
    [Range(1, 72)]
    public int SessionMaxLifetimeHours { get; set; } = 12;

    /// <summary>Lifetime of a persistent ("Remember Me") cookie. Sliding.</summary>
    [Range(1, 90)]
    public int RememberMeDays { get; set; } = 14;

    /// <summary>Hard limit for a persistent session, regardless of activity.</summary>
    [Range(1, 365)]
    public int RememberMeMaxLifetimeDays { get; set; } = 30;

    /// <summary>How often a session is re-checked against the user's security stamp and active flag.</summary>
    [Range(0, 60)]
    public int SecurityStampValidationMinutes { get; set; } = 5;

    [Range(5, 1440)]
    public int PasswordResetTokenLifetimeMinutes { get; set; } = 30;

    /// <summary>Requests per window per client IP for login and change-password.</summary>
    [Range(1, 10000)]
    public int LoginRateLimitPermits { get; set; } = 10;

    [Range(1, 3600)]
    public int LoginRateLimitWindowSeconds { get; set; } = 60;

    /// <summary>Requests per window per client IP for forgot-password and reset-password.</summary>
    [Range(1, 10000)]
    public int PasswordResetRateLimitPermits { get; set; } = 5;

    [Range(1, 86400)]
    public int PasswordResetRateLimitWindowSeconds { get; set; } = 900;
}

/// <summary>Public origin of the Angular application, used to build password-reset links.</summary>
public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;
}
