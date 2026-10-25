using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Auditing;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Common;
using TaskManagement.Api.Roles;

namespace TaskManagement.Api.Controllers;

/// <summary>Role management. Every action requires the Roles tab access plus its own operation permission.</summary>
[Route("api/roles")]
public class RolesController(RoleService roles, AuditService audit) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.Roles.View)]
    public Task<PagedResult<RoleSummaryResponse>> List([FromQuery] RoleListQuery query) => roles.ListAsync(query);

    /// <summary>Active roles with whether the caller may assign each one (used by member forms).</summary>
    [HttpGet("assignable")]
    [RequirePermission(PermissionKeys.Members.Access)]
    public Task<IReadOnlyList<RoleOptionResponse>> Assignable() => roles.GetAssignableOptionsAsync(ActorId);

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionKeys.Roles.View)]
    public async Task<ActionResult> Get(Guid id) => ToResult(await roles.GetAsync(id));

    [HttpPost]
    [RequirePermission(PermissionKeys.Roles.Create)]
    public async Task<ActionResult> Create(SaveRoleRequest request) =>
        ToResult(await roles.CreateAsync(ActorId, request), role => CreatedAtAction(nameof(Get), new { id = role.Id }, role));

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.Roles.Edit)]
    public async Task<ActionResult> Update(Guid id, SaveRoleRequest request) => ToResult(await roles.UpdateAsync(ActorId, id, request));

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.Roles.Delete)]
    public async Task<ActionResult> Delete(Guid id, [FromQuery] DeleteRoleQuery query) =>
        ToResult(await roles.DeleteAsync(ActorId, id, query.ReassignToRoleId));

    [HttpGet("{id:guid}/permissions")]
    [RequirePermission(PermissionKeys.Roles.View)]
    public async Task<ActionResult> GetPermissions(Guid id) => ToResult(await roles.GetPermissionsAsync(ActorId, id));

    [HttpPut("{id:guid}/permissions")]
    [RequirePermission(PermissionKeys.Roles.ManagePermissions)]
    public async Task<ActionResult> UpdatePermissions(Guid id, UpdateRolePermissionsRequest request) =>
        ToResult(await roles.UpdatePermissionsAsync(ActorId, id, request));

    [HttpGet("{id:guid}/audit")]
    [RequirePermission(PermissionKeys.Roles.ViewAudit)]
    public Task<IReadOnlyList<AuditEntryResponse>> Audit(Guid id) => audit.GetForTargetAsync(AuditTargets.Role, id.ToString());
}

/// <summary>The registered permission catalog, grouped by module. Read-only: permissions cannot be created by clients.</summary>
[Route("api/permissions")]
public class PermissionsController(PermissionRegistry registry) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.Roles.View)]
    public Task<IReadOnlyList<PermissionModuleResponse>> Get() => registry.GetModulesAsync();
}
