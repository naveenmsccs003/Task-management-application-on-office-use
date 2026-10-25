using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Authorization;

namespace TaskManagement.Api.Tests.Infrastructure;

/// <summary>Test-only endpoints standing in for future protected modules.</summary>
[ApiController]
[Route("api/test")]
public class ProbeController : ControllerBase
{
    [HttpGet("protected")]
    public IActionResult Protected() => Ok();

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    [HttpGet("admin")]
    public IActionResult AdminOnly() => Ok();

    [RequirePermission("tasks.create")]
    [HttpGet("tasks-create")]
    public IActionResult TasksCreate() => Ok();
}
