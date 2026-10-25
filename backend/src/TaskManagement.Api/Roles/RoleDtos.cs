using System.ComponentModel.DataAnnotations;
using TaskManagement.Api.Common;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Roles;

public sealed class RoleListQuery : PageQuery
{
    /// <summary>"active", "inactive" or empty for all.</summary>
    public string? Status { get; init; }
}

public sealed record RoleSummaryResponse(
    Guid Id, string Name, string? Description, bool IsActive, bool IsSystem, int MemberCount, int PermissionCount,
    DateTime CreatedAtUtc, string? CreatedByName);

public sealed record RoleDetailResponse(
    Guid Id, string Name, string? Description, bool IsActive, bool IsSystem, int MemberCount, int PermissionCount,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, string? CreatedByName);

/// <summary>Explicit create/update contract: only these fields can be set by clients.</summary>
public sealed class SaveRoleRequest
{
    [Required(ErrorMessage = "Role name is required.")]
    [StringLength(ApplicationRole.NameMaxLength, MinimumLength = 2, ErrorMessage = "Role name must be 2 to 50 characters.")]
    public string Name { get; init; } = string.Empty;

    [StringLength(ApplicationRole.DescriptionMaxLength, ErrorMessage = "Description must be at most 250 characters.")]
    public string? Description { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class UpdateRolePermissionsRequest
{
    [Required]
    public List<int> PermissionIds { get; init; } = [];
}

public sealed class DeleteRoleQuery
{
    /// <summary>Required when members still hold the role: they are moved to this role first.</summary>
    public Guid? ReassignToRoleId { get; init; }
}

public sealed record PermissionResponse(
    int Id, string Key, string DisplayName, string Description, int? ParentPermissionId, bool IsActive, bool AllowMemberOverride);

public sealed record PermissionModuleResponse(
    string Key, string Name, string Icon, bool IsImplemented, IReadOnlyList<PermissionResponse> Permissions);

public sealed record RolePermissionsResponse(Guid RoleId, bool IsSystem, bool IsEditable, IReadOnlyList<int> GrantedPermissionIds);

public sealed record RolePermissionsChangeResponse(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int AffectedMembers);

public sealed record RoleOptionResponse(Guid Id, string Name, bool IsSystem, bool IsAssignable);
