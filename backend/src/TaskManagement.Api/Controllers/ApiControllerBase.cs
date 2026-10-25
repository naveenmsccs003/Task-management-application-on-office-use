using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Common;

namespace TaskManagement.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated caller. Endpoints deriving from this base always require authentication.</summary>
    protected Guid ActorId => AuthenticationService.GetUserId(User)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");

    protected ActionResult ToResult<T>(ServiceResult<T> result, Func<T, ActionResult>? onSuccess = null)
    {
        switch (result.Status)
        {
            case ServiceStatus.Ok:
                return onSuccess is not null ? onSuccess(result.Value!) : result.Value is Unit ? NoContent() : Ok(result.Value);
            case ServiceStatus.Invalid:
                foreach (var (field, messages) in result.Errors ?? new Dictionary<string, string[]>())
                {
                    foreach (var message in messages)
                    {
                        ModelState.AddModelError(field, message);
                    }
                }
                return ValidationProblem(ModelState);
            default:
                var status = result.Status switch
                {
                    ServiceStatus.NotFound => StatusCodes.Status404NotFound,
                    ServiceStatus.Forbidden => StatusCodes.Status403Forbidden,
                    _ => StatusCodes.Status409Conflict,
                };
                return Problem(statusCode: status, detail: result.Message);
        }
    }
}
