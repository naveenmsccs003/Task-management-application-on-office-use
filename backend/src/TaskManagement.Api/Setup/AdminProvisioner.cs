using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Auditing;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Setup;

public sealed record AdminProvisioningResult(bool Succeeded, ApplicationUser? User, IReadOnlyList<string> Errors)
{
    public static AdminProvisioningResult Failed(params string[] errors) => new(false, null, errors);
}

/// <summary>
/// Operator-only administrator provisioning (never exposed over HTTP). Creates the first administrator once,
/// and offers a recovery path that promotes an existing account when no active administrator remains.
/// </summary>
public sealed class AdminProvisioner(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    AuditService audit,
    ILogger<AdminProvisioner> logger)
{
    public const string AlreadyCompletedMessage = "An administrator already exists. Initial setup has already been completed.";

    public async Task<bool> IsSetupCompletedAsync() =>
        await roleManager.FindByNameAsync(AppRoles.Administrator) is not null
        && (await userManager.GetUsersInRoleAsync(AppRoles.Administrator)).Count > 0;

    public async Task<AdminProvisioningResult> CreateInitialAdministratorAsync(string userName, string email, string? displayName, string password)
    {
        if (await IsSetupCompletedAsync())
        {
            return AdminProvisioningResult.Failed(AlreadyCompletedMessage);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var role = await EnsureSystemRoleAsync();
        if (role is null)
        {
            return AdminProvisioningResult.Failed("The Administrator role could not be created.");
        }

        var user = new ApplicationUser { UserName = userName, Email = email, DisplayName = displayName ?? userName, IsActive = true };
        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            return Failed(createResult);
        }

        var addRoleResult = await userManager.AddToRoleAsync(user, AppRoles.Administrator);
        if (!addRoleResult.Succeeded)
        {
            return Failed(addRoleResult);
        }

        audit.Add(null, "member.created", AuditTargets.Member, user.Id.ToString(), $"Initial administrator '{user.UserName}' created by the setup command.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        SecurityLog.AdministratorCreated(logger, user.Id);
        return new AdminProvisioningResult(true, user, []);
    }

    /// <summary>
    /// Recovery: makes an existing account an active administrator. Refused while another active administrator
    /// exists, so it cannot be used to bypass normal role management.
    /// </summary>
    public async Task<AdminProvisioningResult> GrantAdministratorAsync(string email)
    {
        var activeAdmins = await db.UserRoles
            .Join(db.Roles.Where(r => r.IsSystem), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.IsActive), id => id, u => u.Id, (id, _) => id)
            .CountAsync();
        if (activeAdmins > 0)
        {
            return AdminProvisioningResult.Failed("An active administrator exists; use role management instead.");
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return AdminProvisioningResult.Failed("No account with that email address exists.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var role = await EnsureSystemRoleAsync();
        if (role is null)
        {
            return AdminProvisioningResult.Failed("The Administrator role could not be created.");
        }
        await db.UserRoles.Where(ur => ur.UserId == user.Id).ExecuteDeleteAsync();
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role.Id });
        user.IsActive = true;
        await userManager.UpdateAsync(user);
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        audit.Add(null, "member.role_changed", AuditTargets.Member, user.Id.ToString(), "Administrator access restored by the recovery command.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        SecurityLog.AdministratorCreated(logger, user.Id);
        return new AdminProvisioningResult(true, user, []);
    }

    /// <summary>Creates the system role, or marks a pre-existing Administrator role as the protected system role.</summary>
    private async Task<ApplicationRole?> EnsureSystemRoleAsync()
    {
        var role = await roleManager.FindByNameAsync(AppRoles.Administrator);
        if (role is null)
        {
            role = new ApplicationRole(AppRoles.Administrator)
            {
                Description = "Full access to every module. Protected system role.",
                IsSystem = true,
                IsActive = true,
            };
            return (await roleManager.CreateAsync(role)).Succeeded ? role : null;
        }
        if (!role.IsSystem || !role.IsActive)
        {
            role.IsSystem = true;
            role.IsActive = true;
            await roleManager.UpdateAsync(role);
        }
        return role;
    }

    private static AdminProvisioningResult Failed(IdentityResult result) =>
        AdminProvisioningResult.Failed([.. result.Errors.Select(e => e.Description)]);
}
