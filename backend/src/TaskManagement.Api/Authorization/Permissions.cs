using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace TaskManagement.Api.Authorization;

/// <summary>
/// Marks an endpoint as requiring a permission key such as "members.create". Every check is resolved per
/// request by the central <see cref="IPermissionService"/>; multiple attributes must all be satisfied.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements() => [new PermissionRequirement(Permission)];
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Satisfied only by active members of the system Administrator role, checked against the database.</summary>
public sealed class AdministratorRequirement : IAuthorizationRequirement;

/// <summary>The single source of truth for effective permissions.</summary>
public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Forgets per-request results after a change that affects permissions.</summary>
    void Invalidate();
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

public sealed class AdministratorAuthorizationHandler(IPermissionService permissions) : AuthorizationHandler<AdministratorRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdministratorRequirement requirement)
    {
        if (Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && await permissions.IsAdministratorAsync(userId))
        {
            context.Succeed(requirement);
        }
    }
}
