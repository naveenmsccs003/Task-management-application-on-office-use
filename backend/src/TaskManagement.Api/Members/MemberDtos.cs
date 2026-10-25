using System.ComponentModel.DataAnnotations;
using TaskManagement.Api.Common;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Members;

public sealed class MemberListQuery : PageQuery
{
    public Guid? RoleId { get; init; }

    /// <summary>"active", "inactive" or empty for all.</summary>
    public string? Status { get; init; }
}

public sealed record RoleReference(Guid Id, string Name, bool IsSystem, bool IsActive);

public sealed record MemberSummaryResponse(
    Guid Id, string DisplayName, string UserName, string? Email, RoleReference? Role, bool IsActive, bool IsLockedOut,
    bool HasPhoto, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);

public sealed record PersonalInformationResponse(
    DateOnly? DateOfBirth, int? Age, string? PhoneNumber, string? AddressLine, string? City, string? StateOrProvince,
    string? PostalCode, string? Country);

/// <summary>
/// Member details. <see cref="PersonalInformation"/> is null unless the caller holds
/// members.view_personal_information: restricted fields are never sent to unauthorized callers.
/// </summary>
public sealed record MemberDetailResponse(
    Guid Id, string DisplayName, string UserName, string? Email, RoleReference? Role, bool IsActive, bool IsLockedOut,
    DateTimeOffset? LockoutEndUtc, bool HasPassword, bool HasPhoto, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    DateTime? LastLoginAtUtc, string? CreatedByName, bool IsSelf, bool IsAdministrator, bool CanManage,
    bool PersonalInformationVisible, PersonalInformationResponse? PersonalInformation);

public enum MemberSetupMethod
{
    /// <summary>Email a single-use link (valid for 3 days) so the member chooses their own password.</summary>
    Invitation,

    /// <summary>The administrator sets an initial password (hashed by Identity, never logged or returned).</summary>
    Password,
}

public sealed class PersonalInformationRequest : IValidatableObject
{
    public DateOnly? DateOfBirth { get; init; }

    [Phone(ErrorMessage = "Enter a valid phone number.")]
    [StringLength(30)]
    public string? PhoneNumber { get; init; }

    [StringLength(200)]
    public string? AddressLine { get; init; }

    [StringLength(100)]
    public string? City { get; init; }

    [StringLength(100)]
    public string? StateOrProvince { get; init; }

    [StringLength(20)]
    [RegularExpression(@"^[A-Za-z0-9 \-]*$", ErrorMessage = "Postal code may contain letters, numbers, spaces and hyphens.")]
    public string? PostalCode { get; init; }

    [StringLength(100)]
    public string? Country { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth is { } dob)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (dob > today)
            {
                yield return new ValidationResult("Date of birth cannot be in the future.", [nameof(DateOfBirth)]);
            }
            else if (dob < new DateOnly(1900, 1, 1))
            {
                yield return new ValidationResult("Enter a valid date of birth.", [nameof(DateOfBirth)]);
            }
        }
    }
}

/// <summary>Explicit create contract. Role and status are validated against the caller's delegation rights.</summary>
public sealed class CreateMemberRequest : IValidatableObject
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(ApplicationUser.DisplayNameMaxLength, ErrorMessage = "Full name must be at most 100 characters.")]
    public string DisplayName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Username is required.")]
    [StringLength(64, MinimumLength = 3, ErrorMessage = "Username must be 3 to 64 characters.")]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Username may contain letters, numbers, dots, hyphens and underscores.")]
    public string UserName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Role is required.")]
    public Guid? RoleId { get; init; }

    public bool IsActive { get; init; } = true;

    public MemberSetupMethod SetupMethod { get; init; } = MemberSetupMethod.Invitation;

    [StringLength(128)]
    public string? Password { get; init; }

    public string? ConfirmPassword { get; init; }

    public PersonalInformationRequest? PersonalInformation { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SetupMethod == MemberSetupMethod.Password)
        {
            if (string.IsNullOrEmpty(Password))
            {
                yield return new ValidationResult("Password is required.", [nameof(Password)]);
            }
            else if (Password != ConfirmPassword)
            {
                yield return new ValidationResult("The password and confirmation do not match.", [nameof(ConfirmPassword)]);
            }
        }
    }
}

/// <summary>
/// Explicit update contract: only non-null sections are changed, and each section needs its own permission
/// (name/username: members.edit, email: members.change_email, personal information: members.edit_personal_information).
/// Role and status can only be changed through their dedicated endpoints.
/// </summary>
public sealed class UpdateMemberRequest
{
    [StringLength(ApplicationUser.DisplayNameMaxLength, MinimumLength = 1, ErrorMessage = "Full name must be 1 to 100 characters.")]
    public string? DisplayName { get; init; }

    [StringLength(64, MinimumLength = 3, ErrorMessage = "Username must be 3 to 64 characters.")]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Username may contain letters, numbers, dots, hyphens and underscores.")]
    public string? UserName { get; init; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string? Email { get; init; }

    /// <summary>When present, replaces the whole personal-information section.</summary>
    public PersonalInformationRequest? PersonalInformation { get; init; }
}

public sealed class ChangeMemberRoleRequest
{
    [Required(ErrorMessage = "Role is required.")]
    public Guid? RoleId { get; init; }
}

public sealed class ChangeMemberStatusRequest
{
    [Required]
    public bool? IsActive { get; init; }
}

public sealed record MemberPermissionItem(
    int PermissionId, string Key, string DisplayName, string Description, string ModuleKey, string ModuleName,
    bool RoleDefault, PermissionEffect? Override, bool Effective, string Source, bool AllowMemberOverride);

public sealed record MemberPermissionsResponse(
    Guid MemberId, string? RoleName, bool IsAdministrator, bool IsEditable, IReadOnlyList<MemberPermissionItem> Items);

public sealed class PermissionOverrideRequest
{
    [Required]
    public int? PermissionId { get; init; }

    [Required]
    public PermissionEffect? Effect { get; init; }
}

public sealed class UpdateMemberPermissionsRequest
{
    /// <summary>The complete set of overrides; permissions not listed inherit from the role.</summary>
    [Required]
    public List<PermissionOverrideRequest> Overrides { get; init; } = [];
}
