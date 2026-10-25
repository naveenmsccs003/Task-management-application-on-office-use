using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authorization;

/// <summary>A delegated "member manager" must not be able to raise anyone's privileges beyond their own.</summary>
public class PrivilegeEscalationTests : ApiTestBase
{
    private static readonly string[] ManagerPermissions =
    [
        PermissionKeys.Members.Access, PermissionKeys.Members.View, PermissionKeys.Members.ViewDetails,
        PermissionKeys.Members.Create, PermissionKeys.Members.Edit, PermissionKeys.Members.ChangeRole,
        PermissionKeys.Members.ManagePermissions, PermissionKeys.Members.Deactivate,
        PermissionKeys.Roles.Access, PermissionKeys.Roles.View, PermissionKeys.Roles.ManagePermissions,
    ];

    private async Task<(ApiClient Manager, ApplicationUser ManagerUser, ApplicationRole ManagerRole)> ManagerAsync()
    {
        var role = await Factory.CreateRoleAsync("Manager", ManagerPermissions);
        var user = await Factory.CreateUserAsync("manager", roles: role.Name!);
        return (await Factory.LoginAsync("manager"), user, role);
    }

    private static object Member(Guid roleId, string userName = "newbie") => new
    {
        displayName = "New Member", email = $"{userName}@example.com", userName, roleId,
        setupMethod = "Password", password = "member passphrase 123", confirmPassword = "member passphrase 123",
    };

    [Fact]
    public async Task Cannot_create_a_member_with_the_administrator_role()
    {
        var (manager, _, _) = await ManagerAsync();
        await Factory.CreateAdministratorAsync();
        var adminRole = await Factory.WithDbAsync(db => db.Roles.SingleAsync(r => r.IsSystem));

        var response = await manager.PostAsync("/api/members", Member(adminRole.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cannot_assign_a_role_with_more_permissions_than_the_assigner_holds()
    {
        var (manager, _, _) = await ManagerAsync();
        var powerful = await Factory.CreateRoleAsync("Powerful", [.. ManagerPermissions, PermissionKeys.Members.Delete]);
        var modest = await Factory.CreateRoleAsync("Modest", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var member = await Factory.CreateUserAsync("someone", roles: modest.Name!);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync("/api/members", Member(powerful.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/members/{member.Id}/change-role", new { roleId = powerful.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await manager.PostAsync("/api/members", Member(modest.Id))).StatusCode);
        var assignable = await manager.GetJsonAsync<List<TaskManagement.Api.Roles.RoleOptionResponse>>("/api/roles/assignable");
        Assert.False(assignable.Single(r => r.Name == "Powerful").IsAssignable);
        Assert.True(assignable.Single(r => r.Name == "Modest").IsAssignable);
    }

    [Fact]
    public async Task Cannot_change_own_role_permissions_or_overrides()
    {
        var (manager, managerUser, managerRole) = await ManagerAsync();
        var bigger = await Factory.CreateRoleAsync("Bigger", PermissionKeys.Members.Access);
        var deleteId = await Factory.PermissionIdAsync(PermissionKeys.Members.Delete);
        var current = await Factory.WithDbAsync(db => db.RolePermissions.Where(rp => rp.RoleId == managerRole.Id).Select(rp => rp.PermissionId).ToListAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/members/{managerUser.Id}/change-role", new { roleId = bigger.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsync($"/api/roles/{managerRole.Id}/permissions", new { permissionIds = current.Append(deleteId) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsync($"/api/members/{managerUser.Id}/permissions",
            new { overrides = new[] { new { permissionId = deleteId, effect = "Allow" } } })).StatusCode);
        Assert.DoesNotContain(PermissionKeys.Members.Delete, (await manager.GetStatusAsync()).User!.Permissions);
    }

    [Fact]
    public async Task Cannot_grant_permissions_they_do_not_hold_through_roles_or_overrides()
    {
        var (manager, _, _) = await ManagerAsync();
        var other = await Factory.CreateRoleAsync("Other", PermissionKeys.Members.Access);
        var member = await Factory.CreateUserAsync("someone", roles: other.Name!);
        var deleteId = await Factory.PermissionIdAsync(PermissionKeys.Members.Delete);
        var viewId = await Factory.PermissionIdAsync(PermissionKeys.Members.View);
        var accessId = await Factory.PermissionIdAsync(PermissionKeys.Members.Access);

        var roleGrant = await manager.PutAsync($"/api/roles/{other.Id}/permissions", new { permissionIds = new[] { accessId, deleteId } });
        var overrideGrant = await manager.PutAsync($"/api/members/{member.Id}/permissions",
            new { overrides = new[] { new { permissionId = deleteId, effect = "Allow" } } });
        var allowedGrant = await manager.PutAsync($"/api/members/{member.Id}/permissions",
            new { overrides = new[] { new { permissionId = viewId, effect = "Allow" } } });

        Assert.Equal(HttpStatusCode.Forbidden, roleGrant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, overrideGrant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowedGrant.StatusCode);
    }

    [Fact]
    public async Task Cannot_manage_administrator_accounts()
    {
        var (manager, _, _) = await ManagerAsync();
        var admin = await Factory.CreateAdministratorAsync();
        var plain = await Factory.CreateRoleAsync("Plain");

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/members/{admin.Id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync($"/api/members/{admin.Id}/change-role", new { roleId = plain.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PutAsync($"/api/members/{admin.Id}", new { displayName = "Hacked" })).StatusCode);
        Assert.True(await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == admin.Id).Select(u => u.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Member_specific_allow_does_not_bypass_security_administration_boundaries()
    {
        await Factory.CreateAdministratorAsync();
        var admin = await Factory.LoginAsync("admin");
        var role = await Factory.CreateRoleAsync("Developer", PermissionKeys.Roles.Access);
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);
        // Even if an override row is forced into the database, the resolver ignores Allow for non-delegable permissions.
        var manageId = await Factory.PermissionIdAsync(PermissionKeys.Roles.ManagePermissions);
        await Factory.WithDbAsync(async db =>
        {
            db.MemberPermissionOverrides.Add(new MemberPermissionOverride { UserId = member.Id, PermissionId = manageId, Effect = PermissionEffect.Allow });
            return await db.SaveChangesAsync();
        });

        var dev = await Factory.LoginAsync("dev");

        Assert.DoesNotContain(PermissionKeys.Roles.ManagePermissions, (await dev.GetStatusAsync()).User!.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.PutAsync($"/api/roles/{role.Id}/permissions", new { permissionIds = Array.Empty<int>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/roles")).StatusCode);
    }
}
