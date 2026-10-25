using Microsoft.AspNetCore.Identity;

namespace TaskManagement.Api.Data;

/// <summary>
/// Application role (ASP.NET Core Identity role with management metadata). Permissions are attached
/// through <see cref="RolePermission"/>; the system Administrator role implicitly holds every active permission.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    public const int NameMaxLength = 50;
    public const int DescriptionMaxLength = 250;

    public ApplicationRole()
    {
    }

    public ApplicationRole(string roleName) : base(roleName)
    {
    }

    public string? Description { get; set; }

    /// <summary>Inactive roles grant no permissions and cannot be assigned.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>System roles (Administrator) cannot be renamed, deactivated, deleted or have permissions edited.</summary>
    public bool IsSystem { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}
