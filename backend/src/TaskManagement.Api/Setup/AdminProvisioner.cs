using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Setup;

public sealed record AdminProvisioningResult(bool Succeeded, ApplicationUser? User, IReadOnlyList<string> Errors)
{
    public static AdminProvisioningResult Failed(params string[] errors) => new(false, null, errors);
}

/// <summary>
/// Creates the first administrator. Refuses once any user holds the Administrator role, which is
/// the persistent record that initial setup has been completed.
/// </summary>
public sealed class AdminProvisioner(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ILogger<AdminProvisioner> logger)
{
    public const string AlreadyCompletedMessage = "An administrator already exists. Initial setup has already been completed.";

    public async Task<bool> IsSetupCompletedAsync() =>
        await roleManager.RoleExistsAsync(AppRoles.Administrator)
        && (await userManager.GetUsersInRoleAsync(AppRoles.Administrator)).Count > 0;

    public async Task<AdminProvisioningResult> CreateInitialAdministratorAsync(string userName, string email, string? displayName, string password)
    {
        if (await IsSetupCompletedAsync())
        {
            return AdminProvisioningResult.Failed(AlreadyCompletedMessage);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!await roleManager.RoleExistsAsync(AppRoles.Administrator))
        {
            var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Administrator));
            if (!roleResult.Succeeded)
            {
                return Failed(roleResult);
            }
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

        await transaction.CommitAsync();
        SecurityLog.AdministratorCreated(logger, user.Id);
        return new AdminProvisioningResult(true, user, []);
    }

    private static AdminProvisioningResult Failed(IdentityResult result) =>
        AdminProvisioningResult.Failed([.. result.Errors.Select(e => e.Description)]);
}
