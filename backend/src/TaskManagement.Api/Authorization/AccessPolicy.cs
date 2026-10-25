using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authorization;

/// <summary>
/// Delegation and privilege-escalation rules shared by role and member management:
/// <list type="bullet">
/// <item>Administrators may delegate anything active; everyone else may only grant permissions they hold themselves.</item>
/// <item>Only administrators may assign the system role or manage members who hold it.</item>
/// <item>A role may be assigned only if it is active and its permissions are within the assigner's own.</item>
/// <item>Non-administrators may not change the permissions of a role they hold.</item>
/// </list>
/// </summary>
public sealed class AccessPolicy(ApplicationDbContext db, IPermissionService permissions)
{
    public Task<bool> IsAdministratorAsync(Guid userId) => permissions.IsAdministratorAsync(userId);

    public Task<IReadOnlySet<string>> GetEffectiveAsync(Guid userId) => permissions.GetEffectivePermissionsAsync(userId);

    /// <summary>Permission keys (among <paramref name="keys"/>) that the actor is not allowed to grant.</summary>
    public async Task<IReadOnlyList<string>> GetUngrantableAsync(Guid actorId, IEnumerable<string> keys)
    {
        if (await permissions.IsAdministratorAsync(actorId))
        {
            return [];
        }
        var held = await permissions.GetEffectivePermissionsAsync(actorId);
        return keys.Where(k => !held.Contains(k)).Distinct().ToList();
    }

    public async Task<bool> IsSystemRoleMemberAsync(Guid userId) =>
        await db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.IsSystem)
            .AnyAsync(isSystem => isSystem);

    /// <summary>Whether the actor may act on the target member at all (administrators are protected).</summary>
    public async Task<bool> CanManageMemberAsync(Guid actorId, Guid targetId) =>
        await permissions.IsAdministratorAsync(actorId) || !await IsSystemRoleMemberAsync(targetId);

    /// <summary>Null when the actor may assign the role; otherwise the reason it may not.</summary>
    public async Task<string?> CheckRoleAssignableAsync(Guid actorId, ApplicationRole role)
    {
        if (!role.IsActive)
        {
            return "Inactive roles cannot be assigned.";
        }
        if (await permissions.IsAdministratorAsync(actorId))
        {
            return null;
        }
        if (role.IsSystem)
        {
            return "Only administrators can assign the Administrator role.";
        }

        var roleKeys = await db.RolePermissions.Where(rp => rp.RoleId == role.Id)
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Key)
            .ToListAsync();
        var ungrantable = await GetUngrantableAsync(actorId, roleKeys);
        return ungrantable.Count == 0
            ? null
            : "You cannot assign a role that has permissions you do not hold yourself.";
    }

    public async Task<int> CountActiveAdministratorsAsync() =>
        await db.UserRoles
            .Join(db.Roles.Where(r => r.IsSystem), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.IsActive), id => id, u => u.Id, (id, _) => id)
            .Distinct()
            .CountAsync();
}
