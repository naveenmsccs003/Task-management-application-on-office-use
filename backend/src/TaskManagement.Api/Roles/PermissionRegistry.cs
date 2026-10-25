using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Roles;

/// <summary>Read access to the registered permissions (database rows seeded from <see cref="PermissionCatalog"/>).</summary>
public sealed class PermissionRegistry(ApplicationDbContext db)
{
    private List<Permission>? _all;

    public async Task<IReadOnlyList<Permission>> GetAllAsync() =>
        _all ??= await db.Permissions.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync();

    public async Task<IReadOnlyList<PermissionModuleResponse>> GetModulesAsync()
    {
        var permissions = await GetAllAsync();
        return PermissionCatalog.Modules
            .OrderBy(m => m.SortOrder)
            .Select(module => new PermissionModuleResponse(
                module.Key, module.Name, module.Icon, module.IsImplemented,
                permissions.Where(p => p.ModuleKey == module.Key).Select(ToResponse).ToList()))
            .Where(m => m.Permissions.Count > 0)
            .ToList();
    }

    public static PermissionResponse ToResponse(Permission p) =>
        new(p.Id, p.Key, p.DisplayName, p.Description, p.ParentPermissionId, p.IsActive, p.AllowMemberOverride);
}
