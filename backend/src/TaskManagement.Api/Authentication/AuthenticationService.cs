using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authentication;

/// <summary>
/// Authentication use cases on top of ASP.NET Core Identity. Authorization (roles, permissions)
/// is intentionally not decided here.
/// </summary>
public class AuthenticationService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IEmailSender<ApplicationUser> emailSender,
    ApplicationDbContext db,
    IOptions<AuthOptions> authOptions,
    IOptions<FrontendOptions> frontendOptions,
    TimeProvider timeProvider,
    ILogger<AuthenticationService> logger)
{
    public const string InvalidResetLinkMessage = "The password reset link is invalid or has expired.";

    private static readonly ApplicationUser TimingUser = new() { UserName = "timing-equalizer" };
    private static string? s_timingHash;

    /// <summary>Verifies credentials and, on success, issues the session cookie. Returns null on any failure.</summary>
    public async Task<ApplicationUser?> LoginAsync(LoginRequest request, string? remoteIp)
    {
        var user = await FindByUserNameOrEmailAsync(request.UserNameOrEmail.Trim());
        if (user is null)
        {
            EqualizeTiming(request.Password);
            SecurityLog.LoginFailedUnknownAccount(logger, remoteIp);
            return null;
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            EqualizeTiming(request.Password);
            SecurityLog.LoginLockedOut(logger, user.Id, remoteIp);
            return null;
        }

        if (result.IsNotAllowed)
        {
            EqualizeTiming(request.Password);
            SecurityLog.LoginNotAllowed(logger, user.Id, remoteIp);
            return null;
        }

        if (!result.Succeeded)
        {
            SecurityLog.LoginFailedInvalidPassword(logger, user.Id, remoteIp);
            return null;
        }

        await signInManager.SignInAsync(user, CreateSessionProperties(request.RememberMe), authenticationMethod: "pwd");
        SecurityLog.LoginSucceeded(logger, user.Id, request.RememberMe, remoteIp);
        return user;
    }

    public async Task LogoutAsync(ClaimsPrincipal principal)
    {
        var userId = GetUserId(principal);
        await signInManager.SignOutAsync();
        if (userId is not null)
        {
            SecurityLog.LoggedOut(logger, userId.Value);
        }
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var user = await userManager.GetUserAsync(principal);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var roles = await userManager.GetRolesAsync(user);
        return new CurrentUserResponse(user.Id, user.UserName!, user.Email, user.DisplayName, [.. roles]);
    }

    /// <summary>Changes the password, revokes every other session and re-issues the current one.</summary>
    public async Task<AuthResult?> ChangePasswordAsync(ClaimsPrincipal principal, ChangePasswordRequest request)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return null;
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            SecurityLog.PasswordChangeFailed(logger, user.Id, string.Join(",", result.Errors.Select(e => e.Code)));
            return ToAuthResult(result);
        }

        // Revoke every other session, then refresh this one so it carries the new security stamp
        // (the cookie handler renews the current session row in place).
        var currentSessionId = DbTicketStore.GetCurrentSessionId(signInManager.Context);
        var revoked = await db.UserSessions
            .Where(s => s.UserId == user.Id && s.Id != currentSessionId)
            .ExecuteDeleteAsync();
        await signInManager.RefreshSignInAsync(user);
        SecurityLog.PasswordChanged(logger, user.Id, revoked);
        return AuthResult.Success;
    }

    /// <summary>
    /// Sends a reset link when an active account owns the address. The caller always returns the same
    /// response, so this never reveals whether the account exists.
    /// </summary>
    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, string? remoteIp)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            SecurityLog.PasswordResetRequestedUnknown(logger, remoteIp);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{frontendOptions.Value.BaseUrl.TrimEnd('/')}/reset-password" +
                   $"?uid={Uri.EscapeDataString(user.Id.ToString())}&token={Uri.EscapeDataString(encodedToken)}";
        try
        {
            await emailSender.SendPasswordResetLinkAsync(user, user.Email, HtmlEncoder.Default.Encode(link));
            SecurityLog.PasswordResetRequested(logger, user.Id);
        }
        catch (Exception ex)
        {
            SecurityLog.PasswordResetEmailFailed(logger, ex, user.Id);
        }
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, string? remoteIp)
    {
        var user = Guid.TryParse(request.UserId, out var userId) ? await userManager.FindByIdAsync(userId.ToString()) : null;
        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            token = string.Empty;
        }

        if (user is null || !user.IsActive || token.Length == 0)
        {
            SecurityLog.PasswordResetRejected(logger, remoteIp);
            return AuthResult.Failure("token", InvalidResetLinkMessage);
        }

        // Identity verifies the token (signature, lifetime, security stamp) before validating the
        // new password, and the stamp changes on success, so a token can only be used once.
        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                SecurityLog.PasswordResetRejected(logger, remoteIp);
            }
            return ToAuthResult(result);
        }

        await RevokeAllSessionsAsync(user.Id);
        SecurityLog.PasswordResetCompleted(logger, user.Id);
        return AuthResult.Success;
    }

    /// <summary>Deletes every server-side session of the user. Future member management can reuse this on deactivation.</summary>
    public Task<int> RevokeAllSessionsAsync(Guid userId) =>
        db.UserSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync();

    public static Guid? GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private AuthenticationProperties CreateSessionProperties(bool rememberMe)
    {
        var options = authOptions.Value;
        var now = timeProvider.GetUtcNow();
        var (lifetime, maxLifetime) = rememberMe
            ? (TimeSpan.FromDays(options.RememberMeDays), TimeSpan.FromDays(options.RememberMeMaxLifetimeDays))
            : (TimeSpan.FromMinutes(options.SessionIdleTimeoutMinutes), TimeSpan.FromHours(options.SessionMaxLifetimeHours));

        // IsPersistent=false produces a browser-session cookie; the server-side expiry still applies.
        var properties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            IssuedUtc = now,
            ExpiresUtc = now.Add(lifetime < maxLifetime ? lifetime : maxLifetime),
            AllowRefresh = true,
        };
        properties.Items[DbTicketStore.AbsoluteExpiresItem] = now.Add(maxLifetime).ToString("O");
        return properties;
    }

    private async Task<ApplicationUser?> FindByUserNameOrEmailAsync(string userNameOrEmail)
    {
        if (userNameOrEmail.Length == 0)
        {
            return null;
        }

        // User names cannot contain '@' (see Identity options), so the two lookups never collide.
        return userNameOrEmail.Contains('@')
            ? await userManager.FindByEmailAsync(userNameOrEmail)
            : await userManager.FindByNameAsync(userNameOrEmail);
    }

    /// <summary>Spends roughly the same time as a real password check so response timing does not reveal account state.</summary>
    private void EqualizeTiming(string password)
    {
        s_timingHash ??= passwordHasher.HashPassword(TimingUser, Guid.NewGuid().ToString());
        passwordHasher.VerifyHashedPassword(TimingUser, s_timingHash, password);
    }

    private static AuthResult ToAuthResult(IdentityResult result)
    {
        var errors = new Dictionary<string, List<string>>();
        foreach (var error in result.Errors)
        {
            var (field, message) = error.Code switch
            {
                nameof(IdentityErrorDescriber.PasswordMismatch) => ("currentPassword", "The current password is incorrect."),
                nameof(IdentityErrorDescriber.InvalidToken) => ("token", InvalidResetLinkMessage),
                var code when code.StartsWith("Password", StringComparison.Ordinal) => ("newPassword", error.Description),
                _ => (string.Empty, "The request could not be completed."),
            };
            if (!errors.TryGetValue(field, out var messages))
            {
                errors[field] = messages = [];
            }
            if (!messages.Contains(message))
            {
                messages.Add(message);
            }
        }

        return new AuthResult(false, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
