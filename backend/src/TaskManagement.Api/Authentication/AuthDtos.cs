using System.ComponentModel.DataAnnotations;

namespace TaskManagement.Api.Authentication;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Username or email is required.")]
    [MaxLength(256)]
    public string UserNameOrEmail { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MaxLength(ApplicationPasswordValidator.MaximumLength)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = "Current password is required.")]
    [MaxLength(ApplicationPasswordValidator.MaximumLength)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [MaxLength(ApplicationPasswordValidator.MaximumLength)]
    public string NewPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Please confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation do not match.")]
    public string ConfirmNewPassword { get; init; } = string.Empty;
}

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    [MaxLength(64)]
    public string UserId { get; init; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string Token { get; init; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [MaxLength(ApplicationPasswordValidator.MaximumLength)]
    public string NewPassword { get; init; } = string.Empty;

    [Required(ErrorMessage = "Please confirm the new password.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation do not match.")]
    public string ConfirmNewPassword { get; init; } = string.Empty;

    /// <summary>True when the link came from a member invitation (first password) rather than a reset.</summary>
    public bool IsInvitation { get; init; }
}

/// <summary>
/// Approved profile fields only; never add hashes, stamps or tokens here. <see cref="Permissions"/> are the
/// effective permission keys, used by the frontend only to shape navigation; the API re-checks every request.
/// </summary>
public sealed record CurrentUserResponse(
    Guid Id, string UserName, string? Email, string? DisplayName, IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions, bool IsAdministrator);

public sealed record AuthStatusResponse(bool IsAuthenticated, CurrentUserResponse? User);

public sealed record MessageResponse(string Message);
