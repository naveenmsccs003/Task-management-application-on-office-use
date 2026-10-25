using System.Text;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Auditing;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Common;
using TaskManagement.Api.Members;

namespace TaskManagement.Api.Controllers;

/// <summary>
/// Member management. Attributes gate entry by permission (tab access is implied by the resolver); the service
/// re-checks field-level rights, delegation scope and protected-account rules on every call.
/// </summary>
[Route("api/members")]
public class MembersController(MemberService members, AuditService audit) : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionKeys.Members.View)]
    public Task<PagedResult<MemberSummaryResponse>> List([FromQuery] MemberListQuery query) => members.ListAsync(query);

    [HttpGet("export")]
    [RequirePermission(PermissionKeys.Members.Export)]
    public async Task<FileContentResult> Export([FromQuery] MemberListQuery query)
    {
        var csv = await members.ExportCsvAsync(ActorId, query);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "members.csv");
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionKeys.Members.ViewDetails)]
    public async Task<ActionResult> Get(Guid id) => ToResult(await members.GetAsync(ActorId, id));

    [HttpPost]
    [RequirePermission(PermissionKeys.Members.Create)]
    public async Task<ActionResult> Create(CreateMemberRequest request) =>
        ToResult(await members.CreateAsync(ActorId, request), member => CreatedAtAction(nameof(Get), new { id = member.Id }, member));

    /// <summary>Field-level permissions are enforced per section (see <see cref="UpdateMemberRequest"/>).</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionKeys.Members.ViewDetails)]
    public async Task<ActionResult> Update(Guid id, UpdateMemberRequest request) => ToResult(await members.UpdateAsync(ActorId, id, request));

    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionKeys.Members.Delete)]
    public async Task<ActionResult> Delete(Guid id) => ToResult(await members.DeleteAsync(ActorId, id));

    [HttpPost("{id:guid}/change-role")]
    [RequirePermission(PermissionKeys.Members.ChangeRole)]
    public async Task<ActionResult> ChangeRole(Guid id, ChangeMemberRoleRequest request) => ToResult(await members.ChangeRoleAsync(ActorId, id, request));

    /// <summary>Activation needs members.activate, deactivation members.deactivate (checked by the service).</summary>
    [HttpPost("{id:guid}/status")]
    [RequirePermission(PermissionKeys.Members.Access)]
    public async Task<ActionResult> ChangeStatus(Guid id, ChangeMemberStatusRequest request) => ToResult(await members.ChangeStatusAsync(ActorId, id, request));

    [HttpPost("{id:guid}/reset-password")]
    [RequirePermission(PermissionKeys.Members.ResetPassword)]
    public async Task<ActionResult> ResetPassword(Guid id) =>
        ToResult(await members.ResetPasswordAsync(ActorId, id), message => Accepted(new { message }));

    [HttpGet("{id:guid}/permissions")]
    [RequirePermission(PermissionKeys.Members.ViewDetails)]
    public async Task<ActionResult> GetPermissions(Guid id) => ToResult(await members.GetPermissionsAsync(ActorId, id));

    [HttpPut("{id:guid}/permissions")]
    [RequirePermission(PermissionKeys.Members.ManagePermissions)]
    public async Task<ActionResult> UpdatePermissions(Guid id, UpdateMemberPermissionsRequest request) =>
        ToResult(await members.UpdatePermissionsAsync(ActorId, id, request));

    [HttpGet("{id:guid}/audit")]
    [RequirePermission(PermissionKeys.Members.ViewAudit)]
    public Task<IReadOnlyList<AuditEntryResponse>> Audit(Guid id) => audit.GetForTargetAsync(AuditTargets.Member, id.ToString());

    [HttpGet("{id:guid}/photo")]
    [RequirePermission(PermissionKeys.Members.ViewDetails)]
    public async Task<ActionResult> GetPhoto(Guid id) =>
        await members.GetPhotoAsync(id) is { } photo ? File(photo.Content, photo.ContentType) : NotFound();

    [HttpPut("{id:guid}/photo")]
    [RequirePermission(PermissionKeys.Members.EditPersonalInformation)]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult> SetPhoto(Guid id, IFormFile? file) => ToResult(await members.SetPhotoAsync(ActorId, id, file));

    [HttpDelete("{id:guid}/photo")]
    [RequirePermission(PermissionKeys.Members.EditPersonalInformation)]
    public async Task<ActionResult> DeletePhoto(Guid id) => ToResult(await members.DeletePhotoAsync(ActorId, id));
}
