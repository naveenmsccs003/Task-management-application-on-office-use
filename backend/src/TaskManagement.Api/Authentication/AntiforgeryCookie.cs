using Microsoft.AspNetCore.Antiforgery;

namespace TaskManagement.Api.Authentication;

/// <summary>
/// Double-submit CSRF protection compatible with Angular's built-in XSRF support: the request token
/// is exposed in a JavaScript-readable cookie that Angular echoes back in the X-XSRF-TOKEN header.
/// </summary>
public static class AntiforgeryCookie
{
    public const string RequestTokenCookieName = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>Issues a token bound to <see cref="HttpContext.User"/>; call again whenever the user changes.</summary>
    public static void Issue(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(RequestTokenCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
    }
}
