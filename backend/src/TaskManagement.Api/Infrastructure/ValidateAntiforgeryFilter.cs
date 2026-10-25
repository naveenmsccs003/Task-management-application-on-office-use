using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaskManagement.Api.Infrastructure;

/// <summary>
/// Global CSRF check: every state-changing request (anything except GET/HEAD/OPTIONS/TRACE),
/// including login and logout, must carry a valid antiforgery token. Equivalent to MVC's
/// AutoValidateAntiforgeryToken without requiring the Razor view services.
/// </summary>
public sealed class ValidateAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace,
    };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (SafeMethods.Contains(context.HttpContext.Request.Method))
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid or missing security token",
                Detail = "Your session security token is missing or expired. Refresh the page and try again.",
            })
            { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}
