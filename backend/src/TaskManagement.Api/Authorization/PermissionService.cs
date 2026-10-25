using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authorization;

/// <summary>
/// Central permission resolution. Rules, in order:
/// 1. Unknown or inactive users get nothing. 2. Unknown or inactive (unimplemented) permissions are never granted.
/// 3. Active members of an active system role (Administrator) get every active permission.
/// 4. Otherwise start from the defaults of the user's active roles.
/// 5. A member-level Deny removes a permission. 6. A member-level Allow adds a permission only when that
/// permission allows member overrides (security-administration permissions do not).
/// 7. A permission whose parent (the module's tab access) is not effective is not effective.
/// 8. Everything else is denied.
/// Results are memoized for the current request only (scoped), so revocations apply on the next request.
/// </summary>
public sealed class PermissionService(ApplicationDbContext db) : IPermissionService
{
    private readonly Dictionary<Guid, Resolution> _cache = [];

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        (await ResolveAsync(userId, cancellationToken)).Permissions.Contains(permission);

    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await ResolveAsync(userId, cancellationToken)).Permissions;

    public async Task<bool> IsAdministratorAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await ResolveAsync(userId, cancellationToken)).IsAdministrator;

    public void Invalidate() => _cache.Clear();

    /// <summary>Pure resolution rules, shared by the service and by previews of member overrides.</summary>
    public static IReadOnlySet<string> Resolve(
        IReadOnlyCollection<Permission> activePermissions,
        bool isAdministrator,
        IReadOnlySet<int> roleGrantedIds,
        IReadOnlyDictionary<int, PermissionEffect> overrides)
    {
        if (isAdministrator)
        {
            return activePermissions.Select(p => p.Key).ToHashSet();
        }

        var granted = new HashSet<int>();
        foreach (var permission in activePermissions)
        {
            var allowed = roleGrantedIds.Contains(permission.Id);
            if (overrides.TryGetValue(permission.Id, out var effect))
            {
                allowed = effect switch
                {
                    PermissionEffect.Deny => false,
                    PermissionEffect.Allow => allowed || permission.AllowMemberOverride,
                    _ => allowed,
                };
            }
            if (allowed)
            {
                granted.Add(permission.Id);
            }
        }

        // A permission only counts when its parent (the module's tab access) is effective too.
        return activePermissions
            .Where(p => granted.Contains(p.Id) && (p.ParentPermissionId is null || granted.Contains(p.ParentPermissionId.Value)))
            .Select(p => p.Key)
            .ToHashSet();
    }

    private async Task<Resolution> ResolveAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(userId, out var cached))
        {
            return cached;
        }

        var resolution = Resolution.None;
        var isActiveUser = await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken);
        if (isActiveUser)
        {
            var roles = await db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => new { r.Id, r.IsActive, r.IsSystem })
                .Where(r => r.IsActive)
                .ToListAsync(cancellationToken);
            var activePermissions = await db.Permissions.AsNoTracking().Where(p => p.IsActive).ToListAsync(cancellationToken);
            var isAdministrator = roles.Any(r => r.IsSystem);
            var roleIds = roles.Select(r => r.Id).ToList();
            var roleGranted = isAdministrator
                ? new HashSet<int>()
                : (await db.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync(cancellationToken)).ToHashSet();
            var overrides = isAdministrator
                ? new Dictionary<int, PermissionEffect>()
                : await db.MemberPermissionOverrides.Where(o => o.UserId == userId).ToDictionaryAsync(o => o.PermissionId, o => o.Effect, cancellationToken);
            resolution = new Resolution(isAdministrator, Resolve(activePermissions, isAdministrator, roleGranted, overrides));
        }

        _cache[userId] = resolution;
        return resolution;
    }

    private sealed record Resolution(bool IsAdministrator, IReadOnlySet<string> Permissions)
    {
        public static Resolution None { get; } = new(false, new HashSet<string>());
    }
}
