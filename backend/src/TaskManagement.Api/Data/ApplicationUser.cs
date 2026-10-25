using Microsoft.AspNetCore.Identity;

namespace TaskManagement.Api.Data;

/// <summary>
/// Application account managed by ASP.NET Core Identity. <see cref="IdentityUser{TKey}.Id"/> is the
/// stable identifier that future modules (members, tasks, permissions) must reference.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string? DisplayName { get; set; }

    /// <summary>Inactive accounts cannot sign in and lose existing sessions at the next security-stamp check.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}
