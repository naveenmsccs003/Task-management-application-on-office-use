namespace TaskManagement.Api.Data;

/// <summary>
/// A registered permission. Rows are seeded from <see cref="Authorization.PermissionCatalog"/> through
/// migrations; clients can never create permissions.
/// </summary>
public class Permission
{
    public int Id { get; set; }

    /// <summary>Unique machine-readable identifier, e.g. "members.change_role".</summary>
    public required string Key { get; set; }

    public required string DisplayName { get; set; }

    public required string Description { get; set; }

    public required string ModuleKey { get; set; }

    /// <summary>Human-readable module/category name, e.g. "Member Management".</summary>
    public required string Category { get; set; }

    /// <summary>The module's tab-access permission; a child is effective only when its parent is.</summary>
    public int? ParentPermissionId { get; set; }

    /// <summary>False for permissions of modules that are not implemented yet; never granted or assignable.</summary>
    public bool IsActive { get; set; }

    /// <summary>Whether an individual member-level Allow override may grant this permission.</summary>
    public bool AllowMemberOverride { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>A permission granted by default to every member of a role. Absence means not granted.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }

    public int PermissionId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }
}

public enum PermissionEffect : byte
{
    Allow = 1,
    Deny = 2,
}

/// <summary>Member-specific exception to the role default. Absence of a row means "inherit from role".</summary>
public class MemberPermissionOverride
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public int PermissionId { get; set; }

    public PermissionEffect Effect { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>Optional personal details, kept apart from the account and protected by dedicated permissions.</summary>
public class MemberPersonalInformation
{
    public Guid UserId { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? AddressLine { get; set; }

    public string? City { get; set; }

    public string? StateOrProvince { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public class MemberProfilePhoto
{
    public const int MaxBytes = 1024 * 1024;

    public Guid UserId { get; set; }

    public required byte[] Content { get; set; }

    public required string ContentType { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Narrow audit trail for role, permission and member administration. Never store passwords, tokens or
/// personal-information values in <see cref="Summary"/>.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>Plain id (no foreign key) so history survives account deletion.</summary>
    public Guid? ActorUserId { get; set; }

    public required string Action { get; set; }

    public required string TargetType { get; set; }

    public required string TargetId { get; set; }

    public required string Summary { get; set; }
}
