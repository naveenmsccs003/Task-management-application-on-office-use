using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Auditing;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Common;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Roles;

public sealed partial class RoleService(
    ApplicationDbContext db,
    RoleManager<ApplicationRole> roleManager,
    PermissionRegistry registry,
    IPermissionService permissions,
    AccessPolicy policy,
    AuditService audit,
    TimeProvider timeProvider)
{
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} &._-]*$")]
    private static partial Regex RoleNamePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public async Task<PagedResult<RoleSummaryResponse>> ListAsync(RoleListQuery query)
    {
        var roles = db.Roles.AsNoTracking().AsQueryable();
        if (query.Term is { } term)
        {
            var upper = term.ToUpperInvariant();
            roles = roles.Where(r => r.NormalizedName!.Contains(upper) || (r.Description != null && r.Description.ToUpper().Contains(upper)));
        }
        roles = query.Status?.ToLowerInvariant() switch
        {
            "active" => roles.Where(r => r.IsActive),
            "inactive" => roles.Where(r => !r.IsActive),
            _ => roles,
        };

        var projected = roles.Select(r => new
        {
            Role = r,
            MemberCount = db.UserRoles.Count(ur => ur.RoleId == r.Id),
            PermissionCount = db.RolePermissions.Count(rp => rp.RoleId == r.Id),
            CreatedByName = db.Users.Where(u => u.Id == r.CreatedByUserId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefault(),
        });
        projected = (query.Sort?.ToLowerInvariant(), query.Descending) switch
        {
            ("members", false) => projected.OrderBy(x => x.MemberCount).ThenBy(x => x.Role.Name),
            ("members", true) => projected.OrderByDescending(x => x.MemberCount).ThenBy(x => x.Role.Name),
            ("created", false) => projected.OrderBy(x => x.Role.CreatedAtUtc),
            ("created", true) => projected.OrderByDescending(x => x.Role.CreatedAtUtc),
            ("status", false) => projected.OrderByDescending(x => x.Role.IsActive).ThenBy(x => x.Role.Name),
            ("status", true) => projected.OrderBy(x => x.Role.IsActive).ThenBy(x => x.Role.Name),
            (_, true) => projected.OrderByDescending(x => x.Role.Name),
            _ => projected.OrderBy(x => x.Role.Name),
        };

        var total = await projected.CountAsync();
        var page = await projected.Skip((query.SafePage - 1) * query.SafePageSize).Take(query.SafePageSize).ToListAsync();
        var activeCount = (await registry.GetAllAsync()).Count(p => p.IsActive);
        var items = page.Select(x => new RoleSummaryResponse(
            x.Role.Id, x.Role.Name!, x.Role.Description, x.Role.IsActive, x.Role.IsSystem, x.MemberCount,
            x.Role.IsSystem ? activeCount : x.PermissionCount, x.Role.CreatedAtUtc, x.CreatedByName)).ToList();
        return new PagedResult<RoleSummaryResponse>(items, query.SafePage, query.SafePageSize, total);
    }

    public async Task<IReadOnlyList<RoleOptionResponse>> GetAssignableOptionsAsync(Guid actorId)
    {
        var roles = await db.Roles.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Name).ToListAsync();
        var options = new List<RoleOptionResponse>();
        foreach (var role in roles)
        {
            options.Add(new RoleOptionResponse(role.Id, role.Name!, role.IsSystem, await policy.CheckRoleAssignableAsync(actorId, role) is null));
        }
        return options;
    }

    public async Task<ServiceResult<RoleDetailResponse>> GetAsync(Guid id)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return role is null ? ServiceError.NotFound("Role not found.") : ServiceResult<RoleDetailResponse>.Ok(await ToDetailAsync(role));
    }

    public async Task<ServiceResult<RoleDetailResponse>> CreateAsync(Guid actorId, SaveRoleRequest request)
    {
        var name = NormalizeName(request.Name);
        if (ValidateName(name) is { } invalid)
        {
            return invalid;
        }
        if (await roleManager.RoleExistsAsync(name))
        {
            return ServiceError.Invalid("name", $"A role named '{name}' already exists.");
        }

        var role = new ApplicationRole(name)
        {
            Description = NormalizeDescription(request.Description),
            IsActive = request.IsActive,
            CreatedByUserId = actorId,
        };
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            return ServiceError.Invalid("name", result.Errors.First().Description);
        }

        await audit.RecordAsync(actorId, "role.created", AuditTargets.Role, role.Id.ToString(),
            $"Created role '{role.Name}' ({(role.IsActive ? "active" : "inactive")}).");
        return ServiceResult<RoleDetailResponse>.Ok(await ToDetailAsync(role));
    }

    public async Task<ServiceResult<RoleDetailResponse>> UpdateAsync(Guid actorId, Guid id, SaveRoleRequest request)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return ServiceError.NotFound("Role not found.");
        }

        var name = NormalizeName(request.Name);
        if (ValidateName(name) is { } invalid)
        {
            return invalid;
        }
        var nameChanged = !string.Equals(name, role.Name, StringComparison.Ordinal);
        if (role.IsSystem && (nameChanged || !request.IsActive))
        {
            return ServiceError.Conflict("The Administrator role is protected: it cannot be renamed or deactivated.");
        }
        if (nameChanged && !string.Equals(name, role.Name, StringComparison.OrdinalIgnoreCase) && await roleManager.RoleExistsAsync(name))
        {
            return ServiceError.Invalid("name", $"A role named '{name}' already exists.");
        }
        if (!await permissions.IsAdministratorAsync(actorId) && await HoldsRoleAsync(actorId, role.Id) && role.IsActive != request.IsActive)
        {
            return ServiceError.Forbidden("You cannot change the status of a role you hold.");
        }

        var changes = new List<string>();
        if (nameChanged)
        {
            changes.Add($"name '{role.Name}' → '{name}'");
            role.Name = name;
        }
        var description = NormalizeDescription(request.Description);
        if (description != role.Description)
        {
            changes.Add("description updated");
            role.Description = description;
        }
        if (role.IsActive != request.IsActive)
        {
            changes.Add(request.IsActive ? "activated" : "deactivated");
            role.IsActive = request.IsActive;
        }
        if (changes.Count == 0)
        {
            return ServiceResult<RoleDetailResponse>.Ok(await ToDetailAsync(role));
        }

        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            return ServiceError.Invalid("name", result.Errors.First().Description);
        }
        permissions.Invalidate();
        await audit.RecordAsync(actorId, "role.updated", AuditTargets.Role, role.Id.ToString(), $"Updated role '{role.Name}': {string.Join(", ", changes)}.");
        return ServiceResult<RoleDetailResponse>.Ok(await ToDetailAsync(role));
    }

    /// <summary>Deletes a non-system role. Members still holding it must be reassigned to another role in the same operation.</summary>
    public async Task<ServiceResult<Unit>> DeleteAsync(Guid actorId, Guid id, Guid? reassignToRoleId)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return ServiceError.NotFound("Role not found.");
        }
        if (role.IsSystem)
        {
            return ServiceError.Conflict("The Administrator role is protected and cannot be deleted.");
        }

        var memberIds = await db.UserRoles.Where(ur => ur.RoleId == id).Select(ur => ur.UserId).ToListAsync();
        ApplicationRole? target = null;
        if (memberIds.Count > 0)
        {
            if (reassignToRoleId is null)
            {
                return ServiceError.Conflict(
                    $"{memberIds.Count} member(s) still have this role. Choose another role to move them to before deleting it.");
            }
            target = await db.Roles.FirstOrDefaultAsync(r => r.Id == reassignToRoleId);
            if (target is null || target.Id == id)
            {
                return ServiceError.Invalid("reassignToRoleId", "Choose a different, existing role to move the members to.");
            }
            if (!await permissions.HasPermissionAsync(actorId, PermissionKeys.Members.ChangeRole))
            {
                return ServiceError.Forbidden("Moving members to another role requires the Change Member Role permission.");
            }
            if (memberIds.Contains(actorId))
            {
                return ServiceError.Forbidden("You cannot delete a role you hold.");
            }
            if (await policy.CheckRoleAssignableAsync(actorId, target) is { } reason)
            {
                return ServiceError.Forbidden(reason);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (target is not null)
        {
            await db.UserRoles.Where(ur => ur.RoleId == id).ExecuteDeleteAsync();
            db.UserRoles.AddRange(memberIds.Select(userId => new IdentityUserRole<Guid> { UserId = userId, RoleId = target.Id }));
        }
        db.Roles.Remove(role);
        audit.Add(actorId, "role.deleted", AuditTargets.Role, role.Id.ToString(),
            target is null ? $"Deleted role '{role.Name}'." : $"Deleted role '{role.Name}'; moved {memberIds.Count} member(s) to '{target.Name}'.");
        foreach (var memberId in memberIds)
        {
            audit.Add(actorId, "member.role_changed", AuditTargets.Member, memberId.ToString(),
                $"Role changed from '{role.Name}' to '{target!.Name}' because the role was deleted.");
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        permissions.Invalidate();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<ServiceResult<RolePermissionsResponse>> GetPermissionsAsync(Guid actorId, Guid id)
    {
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return ServiceError.NotFound("Role not found.");
        }

        var granted = role.IsSystem
            ? (await registry.GetAllAsync()).Where(p => p.IsActive).Select(p => p.Id).ToList()
            : await db.RolePermissions.Where(rp => rp.RoleId == id).Select(rp => rp.PermissionId).OrderBy(x => x).ToListAsync();
        var editable = !role.IsSystem
            && await permissions.HasPermissionAsync(actorId, PermissionKeys.Roles.ManagePermissions)
            && (await permissions.IsAdministratorAsync(actorId) || !await HoldsRoleAsync(actorId, id));
        return ServiceResult<RolePermissionsResponse>.Ok(new RolePermissionsResponse(role.Id, role.IsSystem, editable, granted));
    }

    /// <summary>Replaces the role's default permissions after validating the complete submitted set.</summary>
    public async Task<ServiceResult<RolePermissionsChangeResponse>> UpdatePermissionsAsync(Guid actorId, Guid id, UpdateRolePermissionsRequest request)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return ServiceError.NotFound("Role not found.");
        }
        if (role.IsSystem)
        {
            return ServiceError.Conflict("The Administrator role always has every permission; its permissions cannot be changed.");
        }

        var duplicates = request.PermissionIds.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            return ServiceError.Invalid("permissionIds", $"Duplicate permission ids: {string.Join(", ", duplicates)}.");
        }
        var all = (await registry.GetAllAsync()).ToDictionary(p => p.Id);
        var unknown = request.PermissionIds.Where(pid => !all.ContainsKey(pid)).ToList();
        if (unknown.Count > 0)
        {
            return ServiceError.Invalid("permissionIds", $"Unknown permission ids: {string.Join(", ", unknown)}.");
        }
        var unavailable = request.PermissionIds.Where(pid => !all[pid].IsActive).Select(pid => all[pid].Key).ToList();
        if (unavailable.Count > 0)
        {
            return ServiceError.Invalid("permissionIds", $"These permissions belong to modules that are not available yet: {string.Join(", ", unavailable)}.");
        }

        var isAdministrator = await permissions.IsAdministratorAsync(actorId);
        if (!isAdministrator && await HoldsRoleAsync(actorId, id))
        {
            return ServiceError.Forbidden("You cannot change the permissions of a role you hold.");
        }

        var current = await db.RolePermissions.Where(rp => rp.RoleId == id).ToListAsync();
        var currentIds = current.Select(rp => rp.PermissionId).ToHashSet();
        var requested = request.PermissionIds.ToHashSet();
        var added = requested.Except(currentIds).Select(pid => all[pid].Key).Order().ToList();
        var removed = currentIds.Except(requested).Select(pid => all[pid].Key).Order().ToList();
        var ungrantable = await policy.GetUngrantableAsync(actorId, added);
        if (ungrantable.Count > 0)
        {
            return ServiceError.Forbidden($"You cannot grant permissions you do not hold yourself: {string.Join(", ", ungrantable)}.");
        }

        var affectedMembers = await db.UserRoles.CountAsync(ur => ur.RoleId == id);
        if (added.Count > 0 || removed.Count > 0)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            db.RolePermissions.RemoveRange(current.Where(rp => !requested.Contains(rp.PermissionId)));
            db.RolePermissions.AddRange(requested.Except(currentIds).Select(pid => new RolePermission
            {
                RoleId = id, PermissionId = pid, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actorId,
            }));
            audit.Add(actorId, "role.permissions_changed", AuditTargets.Role, id.ToString(),
                $"Permissions of '{role.Name}' changed (affects {affectedMembers} member(s)). " +
                $"Added: {(added.Count == 0 ? "none" : string.Join(", ", added))}. Removed: {(removed.Count == 0 ? "none" : string.Join(", ", removed))}.");
            await db.SaveChangesAsync();
            permissions.Invalidate();
        }

        return ServiceResult<RolePermissionsChangeResponse>.Ok(new RolePermissionsChangeResponse(added, removed, affectedMembers));
    }

    private Task<bool> HoldsRoleAsync(Guid userId, Guid roleId) =>
        db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);

    private async Task<RoleDetailResponse> ToDetailAsync(ApplicationRole role)
    {
        var memberCount = await db.UserRoles.CountAsync(ur => ur.RoleId == role.Id);
        var permissionCount = role.IsSystem
            ? (await registry.GetAllAsync()).Count(p => p.IsActive)
            : await db.RolePermissions.CountAsync(rp => rp.RoleId == role.Id);
        var createdBy = role.CreatedByUserId is { } creatorId
            ? await db.Users.Where(u => u.Id == creatorId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefaultAsync()
            : null;
        return new RoleDetailResponse(role.Id, role.Name!, role.Description, role.IsActive, role.IsSystem, memberCount,
            permissionCount, role.CreatedAtUtc, role.UpdatedAtUtc, createdBy);
    }

    private static string NormalizeName(string name) => Whitespace().Replace(name.Trim(), " ");

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static ServiceError? ValidateName(string name)
    {
        if (name.Length is < 2 or > ApplicationRole.NameMaxLength)
        {
            return ServiceError.Invalid("name", "Role name must be 2 to 50 characters.");
        }
        return RoleNamePattern().IsMatch(name)
            ? null
            : ServiceError.Invalid("name", "Role names may contain letters, numbers, spaces and & . _ - and must start with a letter or number.");
    }
}
