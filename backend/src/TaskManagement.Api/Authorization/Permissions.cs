using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace TaskManagement.Api.Authorization;

/// <summary>
/// Marks an endpoint as requiring a permission key such as "tasks.create". The requirement is
/// resolved per request by <see cref="IPermissionService"/>, so permissions can live in the database
/// without registering a policy per key.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(Permission)];
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Answers whether a user holds a permission. The RBAC module will provide a database-backed implementation.</summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default);
}

/// <summary>Default until the RBAC module exists: no permission is granted to anyone, administrators included.</summary>
public sealed class DenyAllPermissionService : IPermissionService
{
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed class PermissionAuthorizationHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await permissions.HasPermissionAsync(userId, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
