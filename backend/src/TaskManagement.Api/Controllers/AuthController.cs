using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(
    AuthenticationService authentication,
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : ControllerBase
{
    public const string InvalidCredentialsMessage = "Invalid username or password.";
    public const string ForgotPasswordMessage =
        "If an account with that email exists, a password reset link has been sent.";

    /// <summary>Returns the authentication status and refreshes the CSRF token cookie.</summary>
    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<ActionResult<AuthStatusResponse>> Me()
    {
        var user = await authentication.GetCurrentUserAsync(User);
        if (user is null && User.Identity?.IsAuthenticated == true)
        {
            // The session refers to an account that no longer exists or is inactive.
            await authentication.LogoutAsync(User);
            HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        }

        AntiforgeryCookie.Issue(HttpContext, antiforgery);
        return new AuthStatusResponse(user is not null, user);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting(AuthenticationSetup.LoginRateLimitPolicy)]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await authentication.LoginAsync(request, RemoteIp);
        if (user is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication failed", detail: InvalidCredentialsMessage);
        }

        // The CSRF token is bound to the user, so issue a fresh one for the new identity.
        HttpContext.User = await signInManager.CreateUserPrincipalAsync(user);
        AntiforgeryCookie.Issue(HttpContext, antiforgery);
        return Ok(await authentication.GetCurrentUserAsync(HttpContext.User));
    }

    /// <summary>Ends the session. Safe to call repeatedly or without a session.</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await authentication.LogoutAsync(User);
        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());
        AntiforgeryCookie.Issue(HttpContext, antiforgery);
        return NoContent();
    }

    [Authorize]
    [HttpPost("change-password")]
    [EnableRateLimiting(AuthenticationSetup.LoginRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var result = await authentication.ChangePasswordAsync(User, request);
        if (result is null)
        {
            return Unauthorized();
        }

        return result.Succeeded ? NoContent() : ValidationFailure(result);
    }

    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [EnableRateLimiting(AuthenticationSetup.PasswordResetRateLimitPolicy)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        await authentication.RequestPasswordResetAsync(request, RemoteIp);
        return Accepted(new MessageResponse(ForgotPasswordMessage));
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    [EnableRateLimiting(AuthenticationSetup.PasswordResetRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var result = await authentication.ResetPasswordAsync(request, RemoteIp);
        return result.Succeeded ? NoContent() : ValidationFailure(result);
    }

    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private ActionResult ValidationFailure(AuthResult result)
    {
        foreach (var (field, messages) in result.Errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }
        return ValidationProblem(ModelState);
    }
}
