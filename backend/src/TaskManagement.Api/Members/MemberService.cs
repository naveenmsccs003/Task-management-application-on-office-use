using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManagement.Api.Auditing;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Common;
using TaskManagement.Api.Data;
using TaskManagement.Api.Email;
using TaskManagement.Api.Roles;

namespace TaskManagement.Api.Members;

/// <summary>Member administration. Every operation re-checks the caller's rights; controllers only gate entry.</summary>
public sealed class MemberService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IPermissionService permissions,
    AccessPolicy policy,
    PermissionRegistry registry,
    AuditService audit,
    IEmailSender<ApplicationUser> identityEmail,
    IMemberEmailSender memberEmail,
    IOptions<FrontendOptions> frontend,
    TimeProvider timeProvider,
    ILogger<MemberService> logger)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    // ---------- Queries ----------

    public async Task<PagedResult<MemberSummaryResponse>> ListAsync(MemberListQuery query)
    {
        var projected = Filter(query).Select(u => new
        {
            User = u,
            RoleId = db.UserRoles.Where(ur => ur.UserId == u.Id).Select(ur => (Guid?)ur.RoleId).FirstOrDefault(),
            RoleName = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name).FirstOrDefault(),
            HasPhoto = db.MemberProfilePhotos.Any(p => p.UserId == u.Id),
        });
        projected = (query.Sort?.ToLowerInvariant(), query.Descending) switch
        {
            ("email", false) => projected.OrderBy(x => x.User.Email),
            ("email", true) => projected.OrderByDescending(x => x.User.Email),
            ("role", false) => projected.OrderBy(x => x.RoleName).ThenBy(x => x.User.DisplayName),
            ("role", true) => projected.OrderByDescending(x => x.RoleName).ThenBy(x => x.User.DisplayName),
            ("status", false) => projected.OrderByDescending(x => x.User.IsActive).ThenBy(x => x.User.DisplayName),
            ("status", true) => projected.OrderBy(x => x.User.IsActive).ThenBy(x => x.User.DisplayName),
            ("created", false) => projected.OrderBy(x => x.User.CreatedAtUtc),
            ("created", true) => projected.OrderByDescending(x => x.User.CreatedAtUtc),
            ("lastlogin", false) => projected.OrderBy(x => x.User.LastLoginAtUtc),
            ("lastlogin", true) => projected.OrderByDescending(x => x.User.LastLoginAtUtc),
            (_, true) => projected.OrderByDescending(x => x.User.DisplayName),
            _ => projected.OrderBy(x => x.User.DisplayName),
        };

        var total = await projected.CountAsync();
        var page = await projected.Skip((query.SafePage - 1) * query.SafePageSize).Take(query.SafePageSize).ToListAsync();
        var roles = await LoadRolesAsync(page.Where(x => x.RoleId.HasValue).Select(x => x.RoleId!.Value));
        var items = page.Select(x => new MemberSummaryResponse(
            x.User.Id, DisplayNameOf(x.User), x.User.UserName!, x.User.Email,
            x.RoleId is { } roleId && roles.TryGetValue(roleId, out var role) ? role : null,
            x.User.IsActive, IsLockedOut(x.User), x.HasPhoto, x.User.CreatedAtUtc, x.User.LastLoginAtUtc)).ToList();
        return new PagedResult<MemberSummaryResponse>(items, query.SafePage, query.SafePageSize, total);
    }

    /// <summary>CSV of account fields (no personal information). Cells are neutralized against spreadsheet formula injection.</summary>
    public async Task<string> ExportCsvAsync(Guid actorId, MemberListQuery query)
    {
        var rows = await Filter(query)
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id, u.DisplayName, u.UserName, u.Email, u.IsActive, u.CreatedAtUtc, u.LastLoginAtUtc,
                RoleName = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name).FirstOrDefault(),
            })
            .ToListAsync();

        var csv = new StringBuilder("Member ID,Name,Username,Email,Role,Status,Created (UTC),Last login (UTC)\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',', new[]
            {
                row.Id.ToString(), row.DisplayName ?? row.UserName, row.UserName, row.Email, row.RoleName,
                row.IsActive ? "Active" : "Inactive",
                row.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                row.LastLoginAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            }.Select(CsvCell)).Append("\r\n");
        }
        await audit.RecordAsync(actorId, "member.exported", AuditTargets.Member, "*", $"Exported {rows.Count} member(s) to CSV.");
        return csv.ToString();
    }

    public async Task<ServiceResult<MemberDetailResponse>> GetAsync(Guid actorId, Guid id)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        return user is null ? ServiceError.NotFound("Member not found.") : ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
    }

    // ---------- Create ----------

    public async Task<ServiceResult<MemberDetailResponse>> CreateAsync(Guid actorId, CreateMemberRequest request)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId);
        if (role is null)
        {
            return ServiceError.Invalid("roleId", "Select a valid role.");
        }
        if (await policy.CheckRoleAssignableAsync(actorId, role) is { } reason)
        {
            return role.IsActive ? ServiceError.Forbidden(reason) : ServiceError.Invalid("roleId", reason);
        }
        if (HasValues(request.PersonalInformation)
            && !await permissions.HasPermissionAsync(actorId, PermissionKeys.Members.EditPersonalInformation))
        {
            return ServiceError.Forbidden("You do not have permission to enter personal information.");
        }

        var user = new ApplicationUser
        {
            DisplayName = request.DisplayName.Trim(),
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            IsActive = request.IsActive,
            CreatedByUserId = actorId,
            PhoneNumber = Clean(request.PersonalInformation?.PhoneNumber),
        };

        await using var transaction = await db.Database.BeginTransactionAsync();
        var created = request.SetupMethod == MemberSetupMethod.Password
            ? await userManager.CreateAsync(user, request.Password!)
            : await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            return ToValidationError(created);
        }
        var roleResult = await userManager.AddToRoleAsync(user, role.Name!);
        if (!roleResult.Succeeded)
        {
            return ToValidationError(roleResult);
        }
        if (HasValues(request.PersonalInformation))
        {
            db.MemberPersonalInformation.Add(ToEntity(user.Id, request.PersonalInformation!));
        }
        audit.Add(actorId, "member.created", AuditTargets.Member, user.Id.ToString(),
            $"Created member '{user.UserName}' with role '{role.Name}' ({(user.IsActive ? "active" : "inactive")}, " +
            $"{(request.SetupMethod == MemberSetupMethod.Password ? "password set by administrator" : "invitation")}).");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        if (request.SetupMethod == MemberSetupMethod.Invitation && user.IsActive)
        {
            await SendInvitationAsync(user);
        }
        return ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
    }

    // ---------- Update ----------

    public async Task<ServiceResult<MemberDetailResponse>> UpdateAsync(Guid actorId, Guid id, UpdateMemberRequest request)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can change administrator accounts.");
        }

        // Each requested section needs its own permission; a forged request cannot reach other fields.
        var required = new List<string>();
        if (request.DisplayName is not null || request.UserName is not null) required.Add(PermissionKeys.Members.Edit);
        if (request.Email is not null) required.Add(PermissionKeys.Members.ChangeEmail);
        if (request.PersonalInformation is not null) required.Add(PermissionKeys.Members.EditPersonalInformation);
        if (required.Count == 0)
        {
            return ServiceError.Invalid("", "No changes were submitted.");
        }
        var held = await permissions.GetEffectivePermissionsAsync(actorId);
        var missing = required.Where(p => !held.Contains(p)).ToList();
        if (missing.Count > 0)
        {
            return ServiceError.Forbidden($"You do not have permission to change these fields ({string.Join(", ", missing)}).");
        }

        var changed = new List<string>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (request.DisplayName is { } displayName && displayName.Trim() != user.DisplayName)
        {
            user.DisplayName = displayName.Trim();
            changed.Add("full name");
        }
        if (request.UserName is { } userName && !string.Equals(userName.Trim(), user.UserName, StringComparison.Ordinal))
        {
            var result = await userManager.SetUserNameAsync(user, userName.Trim());
            if (!result.Succeeded) return ToValidationError(result);
            changed.Add("username");
        }
        if (request.Email is { } email && !string.Equals(email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var result = await userManager.SetEmailAsync(user, email.Trim());
            if (!result.Succeeded) return ToValidationError(result);
            changed.Add("email");
        }
        if (request.PersonalInformation is { } personal)
        {
            var fields = await ApplyPersonalInformationAsync(user, personal);
            if (fields.Count > 0) changed.Add($"personal information ({string.Join(", ", fields)})");
        }

        if (changed.Count > 0)
        {
            var update = await userManager.UpdateAsync(user);
            if (!update.Succeeded) return ToValidationError(update);
            // Values are not recorded: only which fields changed.
            audit.Add(actorId, "member.updated", AuditTargets.Member, id.ToString(), $"Updated {string.Join(", ", changed)}.");
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
    }

    public async Task<ServiceResult<MemberDetailResponse>> ChangeRoleAsync(Guid actorId, Guid id, ChangeMemberRoleRequest request)
    {
        if (id == actorId)
        {
            return ServiceError.Forbidden("You cannot change your own role.");
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can change administrator accounts.");
        }
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId);
        if (role is null)
        {
            return ServiceError.Invalid("roleId", "Select a valid role.");
        }
        if (await policy.CheckRoleAssignableAsync(actorId, role) is { } reason)
        {
            return role.IsActive ? ServiceError.Forbidden(reason) : ServiceError.Invalid("roleId", reason);
        }

        var currentRoles = await db.UserRoles.Where(ur => ur.UserId == id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r).ToListAsync();
        if (currentRoles.Count == 1 && currentRoles[0].Id == role.Id)
        {
            return ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
        }
        if (user.IsActive && currentRoles.Any(r => r.IsSystem) && !role.IsSystem && await policy.CountActiveAdministratorsAsync() <= 1)
        {
            return ServiceError.Conflict("This is the last active administrator. Assign another administrator first.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.UserRoles.Where(ur => ur.UserId == id).ExecuteDeleteAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = id, RoleId = role.Id });
        audit.Add(actorId, "member.role_changed", AuditTargets.Member, id.ToString(),
            $"Role changed from '{(currentRoles.Count == 0 ? "none" : string.Join(", ", currentRoles.Select(r => r.Name)))}' to '{role.Name}'.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        permissions.Invalidate();
        return ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
    }

    public async Task<ServiceResult<MemberDetailResponse>> ChangeStatusAsync(Guid actorId, Guid id, ChangeMemberStatusRequest request)
    {
        var activate = request.IsActive!.Value;
        if (id == actorId)
        {
            return ServiceError.Forbidden("You cannot change the status of your own account.");
        }
        var needed = activate ? PermissionKeys.Members.Activate : PermissionKeys.Members.Deactivate;
        if (!await permissions.HasPermissionAsync(actorId, needed))
        {
            return ServiceError.Forbidden($"You do not have permission to {(activate ? "activate" : "deactivate")} members.");
        }
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can change administrator accounts.");
        }
        if (user.IsActive == activate)
        {
            return ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
        }
        if (!activate && await policy.IsSystemRoleMemberAsync(id) && await policy.CountActiveAdministratorsAsync() <= 1)
        {
            return ServiceError.Conflict("This is the last active administrator and cannot be deactivated.");
        }

        user.IsActive = activate;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return ToValidationError(result);
        if (!activate)
        {
            // End every session immediately rather than waiting for the next security-stamp check.
            await userManager.UpdateSecurityStampAsync(user);
            await db.UserSessions.Where(s => s.UserId == id).ExecuteDeleteAsync();
        }
        await audit.RecordAsync(actorId, activate ? "member.activated" : "member.deactivated", AuditTargets.Member, id.ToString(),
            activate ? "Account activated." : "Account deactivated; all sessions ended.");
        permissions.Invalidate();
        return ServiceResult<MemberDetailResponse>.Ok(await ToDetailAsync(actorId, user));
    }

    /// <summary>Account lifecycle: members must be deactivated before they can be permanently deleted.</summary>
    public async Task<ServiceResult<Unit>> DeleteAsync(Guid actorId, Guid id)
    {
        if (id == actorId)
        {
            return ServiceError.Forbidden("You cannot delete your own account.");
        }
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can delete administrator accounts.");
        }
        if (user.IsActive)
        {
            return ServiceError.Conflict("Deactivate the member before deleting the account.");
        }

        var userName = user.UserName;
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded) return ToValidationError(result);
        await audit.RecordAsync(actorId, "member.deleted", AuditTargets.Member, id.ToString(), $"Deleted member '{userName}'.");
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<ServiceResult<string>> ResetPasswordAsync(Guid actorId, Guid id)
    {
        if (id == actorId)
        {
            return ServiceError.Conflict("Use Change password for your own account.");
        }
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can reset administrator passwords.");
        }
        if (!user.IsActive || string.IsNullOrEmpty(user.Email))
        {
            return ServiceError.Conflict("Only active members with an email address can receive a password link.");
        }

        string message;
        if (await userManager.HasPasswordAsync(user))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            await identityEmail.SendPasswordResetLinkAsync(user, user.Email, HtmlEncoder.Default.Encode(BuildLink(user, token, invitation: false)));
            message = "A password reset link has been sent to the member.";
        }
        else
        {
            await SendInvitationAsync(user);
            message = "The invitation to set a password has been sent again.";
        }
        await audit.RecordAsync(actorId, "member.password_reset_requested", AuditTargets.Member, id.ToString(), "Password link sent to the member.");
        return ServiceResult<string>.Ok(message);
    }

    // ---------- Member-specific permission overrides ----------

    public async Task<ServiceResult<MemberPermissionsResponse>> GetPermissionsAsync(Guid actorId, Guid id)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }

        var active = (await registry.GetAllAsync()).Where(p => p.IsActive).ToList();
        var roles = await db.UserRoles.Where(ur => ur.UserId == id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r).ToListAsync();
        var isAdministrator = roles.Any(r => r.IsSystem && r.IsActive);
        var activeRoleIds = roles.Where(r => r.IsActive).Select(r => r.Id).ToList();
        var roleGranted = (await db.RolePermissions.Where(rp => activeRoleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionId).ToListAsync()).ToHashSet();
        var overrides = await db.MemberPermissionOverrides.Where(o => o.UserId == id).ToDictionaryAsync(o => o.PermissionId, o => o.Effect);
        var effective = user.IsActive
            ? PermissionService.Resolve(active, isAdministrator, roleGranted, isAdministrator ? new Dictionary<int, PermissionEffect>() : overrides)
            : new HashSet<string>();
        var modules = PermissionCatalog.Modules.ToDictionary(m => m.Key, m => m.Name);

        var items = active.Select(p =>
        {
            var roleDefault = isAdministrator || roleGranted.Contains(p.Id);
            PermissionEffect? memberOverride = !isAdministrator && overrides.TryGetValue(p.Id, out var e) ? e : null;
            var isEffective = effective.Contains(p.Key);
            var source = (isAdministrator, memberOverride, isEffective) switch
            {
                (true, _, _) => "Administrator role",
                (_, PermissionEffect.Deny, _) => "Explicitly denied",
                (_, PermissionEffect.Allow, true) => roleDefault ? "Inherited from role" : "Explicitly allowed",
                (_, _, true) => "Inherited from role",
                _ when !user.IsActive => "Account inactive",
                _ when roleDefault || memberOverride == PermissionEffect.Allow => "Requires tab access",
                _ => "Not granted",
            };
            return new MemberPermissionItem(p.Id, p.Key, p.DisplayName, p.Description, p.ModuleKey, modules[p.ModuleKey],
                roleDefault, memberOverride, isEffective, source, p.AllowMemberOverride);
        }).ToList();

        var editable = !isAdministrator && id != actorId
            && await permissions.HasPermissionAsync(actorId, PermissionKeys.Members.ManagePermissions)
            && await policy.CanManageMemberAsync(actorId, id);
        return ServiceResult<MemberPermissionsResponse>.Ok(new MemberPermissionsResponse(
            id, roles.Select(r => r.Name).FirstOrDefault(), isAdministrator, editable, items));
    }

    /// <summary>Replaces the member's overrides. Absent permissions inherit from the role.</summary>
    public async Task<ServiceResult<MemberPermissionsResponse>> UpdatePermissionsAsync(Guid actorId, Guid id, UpdateMemberPermissionsRequest request)
    {
        if (id == actorId)
        {
            return ServiceError.Forbidden("You cannot change your own permissions.");
        }
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (await policy.IsSystemRoleMemberAsync(id))
        {
            return ServiceError.Conflict("Administrators always have every permission; overrides do not apply to them.");
        }

        var ids = request.Overrides.Select(o => o.PermissionId!.Value).ToList();
        var duplicates = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            return ServiceError.Invalid("overrides", $"Each permission may only be listed once (duplicates: {string.Join(", ", duplicates)}).");
        }
        var all = (await registry.GetAllAsync()).ToDictionary(p => p.Id);
        var invalid = ids.Where(pid => !all.TryGetValue(pid, out var p) || !p.IsActive).ToList();
        if (invalid.Count > 0)
        {
            return ServiceError.Invalid("overrides", $"Unknown or unavailable permission ids: {string.Join(", ", invalid)}.");
        }
        var notOverridable = request.Overrides
            .Where(o => o.Effect == PermissionEffect.Allow && !all[o.PermissionId!.Value].AllowMemberOverride)
            .Select(o => all[o.PermissionId!.Value].Key).ToList();
        if (notOverridable.Count > 0)
        {
            return ServiceError.Invalid("overrides",
                $"These permissions can only be granted through a role, not to an individual member: {string.Join(", ", notOverridable)}.");
        }
        var ungrantable = await policy.GetUngrantableAsync(actorId,
            request.Overrides.Where(o => o.Effect == PermissionEffect.Allow).Select(o => all[o.PermissionId!.Value].Key));
        if (ungrantable.Count > 0)
        {
            return ServiceError.Forbidden($"You cannot grant permissions you do not hold yourself: {string.Join(", ", ungrantable)}.");
        }

        var existing = await db.MemberPermissionOverrides.Where(o => o.UserId == id).ToListAsync();
        var requested = request.Overrides.ToDictionary(o => o.PermissionId!.Value, o => o.Effect!.Value);
        var changes = new List<string>();
        var now = Now;
        foreach (var current in existing)
        {
            if (!requested.TryGetValue(current.PermissionId, out var effect))
            {
                db.MemberPermissionOverrides.Remove(current);
                changes.Add($"{all[current.PermissionId].Key}: {current.Effect} → Inherit");
            }
            else if (effect != current.Effect)
            {
                changes.Add($"{all[current.PermissionId].Key}: {current.Effect} → {effect}");
                current.Effect = effect;
                current.UpdatedAtUtc = now;
                current.UpdatedByUserId = actorId;
            }
        }
        foreach (var (permissionId, effect) in requested.Where(r => existing.All(e => e.PermissionId != r.Key)))
        {
            db.MemberPermissionOverrides.Add(new MemberPermissionOverride
            {
                UserId = id, PermissionId = permissionId, Effect = effect,
                CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = actorId, UpdatedByUserId = actorId,
            });
            changes.Add($"{all[permissionId].Key}: Inherit → {effect}");
        }
        if (changes.Count > 0)
        {
            audit.Add(actorId, "member.permissions_changed", AuditTargets.Member, id.ToString(), $"Permission overrides changed: {string.Join("; ", changes)}.");
            await db.SaveChangesAsync();
            permissions.Invalidate();
        }
        return await GetPermissionsAsync(actorId, id);
    }

    // ---------- Profile photo ----------

    public async Task<MemberProfilePhoto?> GetPhotoAsync(Guid id) =>
        await db.MemberProfilePhotos.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == id);

    public async Task<ServiceResult<Unit>> SetPhotoAsync(Guid actorId, Guid id, IFormFile? file)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id))
        {
            return ServiceError.NotFound("Member not found.");
        }
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can change administrator accounts.");
        }
        if (file is null || file.Length == 0)
        {
            return ServiceError.Invalid("file", "Choose an image to upload.");
        }
        if (file.Length > MemberProfilePhoto.MaxBytes)
        {
            return ServiceError.Invalid("file", "The image must be 1 MB or smaller.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        // Trust the file signature, not the client-supplied content type or file name.
        var contentType = DetectImageType(bytes);
        if (contentType is null)
        {
            return ServiceError.Invalid("file", "Only JPEG and PNG images are supported.");
        }

        var photo = await db.MemberProfilePhotos.FirstOrDefaultAsync(p => p.UserId == id);
        if (photo is null)
        {
            db.MemberProfilePhotos.Add(new MemberProfilePhoto { UserId = id, Content = bytes, ContentType = contentType, UpdatedAtUtc = Now });
        }
        else
        {
            photo.Content = bytes;
            photo.ContentType = contentType;
            photo.UpdatedAtUtc = Now;
        }
        audit.Add(actorId, "member.photo_updated", AuditTargets.Member, id.ToString(), "Profile photo updated.");
        await db.SaveChangesAsync();
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    public async Task<ServiceResult<Unit>> DeletePhotoAsync(Guid actorId, Guid id)
    {
        if (!await policy.CanManageMemberAsync(actorId, id))
        {
            return ServiceError.Forbidden("Only administrators can change administrator accounts.");
        }
        var removed = await db.MemberProfilePhotos.Where(p => p.UserId == id).ExecuteDeleteAsync();
        if (removed > 0)
        {
            await audit.RecordAsync(actorId, "member.photo_removed", AuditTargets.Member, id.ToString(), "Profile photo removed.");
        }
        return ServiceResult<Unit>.Ok(Unit.Value);
    }

    // ---------- Helpers ----------

    private IQueryable<ApplicationUser> Filter(MemberListQuery query)
    {
        var users = db.Users.AsNoTracking().AsQueryable();
        if (query.Term is { } term)
        {
            var upper = term.ToUpperInvariant();
            users = users.Where(u =>
                (u.DisplayName != null && u.DisplayName.ToUpper().Contains(upper))
                || u.NormalizedEmail!.Contains(upper)
                || u.NormalizedUserName!.Contains(upper));
        }
        if (query.RoleId is { } roleId)
        {
            users = users.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == roleId));
        }
        return query.Status?.ToLowerInvariant() switch
        {
            "active" => users.Where(u => u.IsActive),
            "inactive" => users.Where(u => !u.IsActive),
            _ => users,
        };
    }

    private async Task<MemberDetailResponse> ToDetailAsync(Guid actorId, ApplicationUser user)
    {
        var role = (await LoadRolesAsync(await db.UserRoles.Where(ur => ur.UserId == user.Id).Select(ur => ur.RoleId).ToListAsync()))
            .Values.FirstOrDefault();
        var canSeePersonal = await permissions.HasPermissionAsync(actorId, PermissionKeys.Members.ViewPersonalInformation);
        PersonalInformationResponse? personal = null;
        if (canSeePersonal)
        {
            var info = await db.MemberPersonalInformation.AsNoTracking().FirstOrDefaultAsync(i => i.UserId == user.Id);
            personal = new PersonalInformationResponse(
                info?.DateOfBirth, info?.DateOfBirth is { } dob ? AgeOn(dob, DateOnly.FromDateTime(Now)) : null,
                user.PhoneNumber, info?.AddressLine, info?.City, info?.StateOrProvince, info?.PostalCode, info?.Country);
        }
        var createdBy = user.CreatedByUserId is { } creator
            ? await db.Users.Where(u => u.Id == creator).Select(u => u.DisplayName ?? u.UserName).FirstOrDefaultAsync()
            : null;

        return new MemberDetailResponse(
            user.Id, DisplayNameOf(user), user.UserName!, user.Email, role, user.IsActive, IsLockedOut(user), user.LockoutEnd,
            user.PasswordHash is not null, await db.MemberProfilePhotos.AnyAsync(p => p.UserId == user.Id),
            user.CreatedAtUtc, user.UpdatedAtUtc, user.LastLoginAtUtc, createdBy, user.Id == actorId,
            role?.IsSystem == true, await policy.CanManageMemberAsync(actorId, user.Id), canSeePersonal, personal);
    }

    private async Task<Dictionary<Guid, RoleReference>> LoadRolesAsync(IEnumerable<Guid> roleIds)
    {
        var ids = roleIds.Distinct().ToList();
        return await db.Roles.Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => new RoleReference(r.Id, r.Name!, r.IsSystem, r.IsActive));
    }

    private async Task<List<string>> ApplyPersonalInformationAsync(ApplicationUser user, PersonalInformationRequest request)
    {
        var info = await db.MemberPersonalInformation.FirstOrDefaultAsync(i => i.UserId == user.Id);
        var next = ToEntity(user.Id, request);
        var changed = new List<string>();
        void Track(string name, object? before, object? after)
        {
            if (!Equals(before, after)) changed.Add(name);
        }
        Track("date of birth", info?.DateOfBirth, next.DateOfBirth);
        Track("address", info?.AddressLine, next.AddressLine);
        Track("city", info?.City, next.City);
        Track("state", info?.StateOrProvince, next.StateOrProvince);
        Track("postal code", info?.PostalCode, next.PostalCode);
        Track("country", info?.Country, next.Country);
        Track("phone", user.PhoneNumber, Clean(request.PhoneNumber));

        // Phone lives in Identity's own column; set directly so the member's sessions are not reset.
        user.PhoneNumber = Clean(request.PhoneNumber);
        if (info is null)
        {
            db.MemberPersonalInformation.Add(next);
        }
        else
        {
            db.Entry(info).CurrentValues.SetValues(next);
        }
        return changed;
    }

    private MemberPersonalInformation ToEntity(Guid userId, PersonalInformationRequest request) => new()
    {
        UserId = userId,
        DateOfBirth = request.DateOfBirth,
        AddressLine = Clean(request.AddressLine),
        City = Clean(request.City),
        StateOrProvince = Clean(request.StateOrProvince),
        PostalCode = Clean(request.PostalCode),
        Country = Clean(request.Country),
        UpdatedAtUtc = Now,
    };

    private async Task SendInvitationAsync(ApplicationUser user)
    {
        try
        {
            var token = await userManager.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);
            await memberEmail.SendInvitationAsync(user, user.Email!, HtmlEncoder.Default.Encode(BuildLink(user, token, invitation: true)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invitation email could not be sent for member {UserId}.", user.Id);
        }
    }

    private string BuildLink(ApplicationUser user, string token, bool invitation) =>
        $"{frontend.Value.BaseUrl.TrimEnd('/')}/reset-password?uid={Uri.EscapeDataString(user.Id.ToString())}" +
        $"&token={Uri.EscapeDataString(WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)))}" +
        (invitation ? "&invite=1" : string.Empty);

    private static ServiceError ToValidationError(IdentityResult result)
    {
        var errors = new Dictionary<string, List<string>>();
        foreach (var error in result.Errors)
        {
            var field = error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.InvalidEmail) => "email",
                nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.InvalidUserName) => "userName",
                var code when code.StartsWith("Password", StringComparison.Ordinal) => "password",
                _ => string.Empty,
            };
            var message = error.Code == nameof(IdentityErrorDescriber.DuplicateEmail) ? "This email address is already in use." : error.Description;
            (errors.TryGetValue(field, out var list) ? list : errors[field] = []).Add(message);
        }
        return ServiceError.Invalid(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    public static int AgeOn(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        return dateOfBirth > today.AddYears(-age) ? age - 1 : age;
    }

    private static string DisplayNameOf(ApplicationUser user) => user.DisplayName ?? user.UserName!;

    private bool IsLockedOut(ApplicationUser user) => user.LockoutEnd is { } end && end > timeProvider.GetUtcNow();

    private static bool HasValues(PersonalInformationRequest? info) =>
        info is not null && (info.DateOfBirth is not null || new[]
        {
            info.PhoneNumber, info.AddressLine, info.City, info.StateOrProvince, info.PostalCode, info.Country,
        }.Any(v => !string.IsNullOrWhiteSpace(v)));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? DetectImageType(byte[] bytes) =>
        bytes is [0xFF, 0xD8, 0xFF, ..] ? "image/jpeg"
        : bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] ? "image/png"
        : null;

    private static string CsvCell(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
