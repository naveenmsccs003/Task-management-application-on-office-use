using Microsoft.AspNetCore.Identity;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authentication;

/// <summary>
/// Supplements Identity's built-in length rule: caps the length and rejects passwords that contain
/// the account's own user name or email local part.
/// </summary>
public class ApplicationPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const int MaximumLength = 128;

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>();
        if (password.Length > MaximumLength)
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Passwords must be at most {MaximumLength} characters.",
            });
        }

        var emailLocalPart = user.Email?.Split('@')[0];
        if (ContainsIgnoringCase(password, user.UserName) || ContainsIgnoringCase(password, emailLocalPart))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordContainsUserName",
                Description = "Passwords must not contain your user name or email address.",
            });
        }

        return Task.FromResult(errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]));
    }

    private static bool ContainsIgnoringCase(string password, string? value) =>
        value is { Length: >= 3 } && password.Contains(value, StringComparison.OrdinalIgnoreCase);
}
