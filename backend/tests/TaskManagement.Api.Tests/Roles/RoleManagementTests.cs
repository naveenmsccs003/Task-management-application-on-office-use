using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Common;
using TaskManagement.Api.Roles;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Roles;

public class RoleManagementTests : ApiTestBase
{
    private async Task<ApiClient> AdminAsync()
    {
        await Factory.CreateAdministratorAsync();
        return await Factory.LoginAsync("admin");
    }

    private static async Task<Guid> CreateRoleAsync(ApiClient client, string name, string? description = null, bool isActive = true)
    {
        var response = await client.PostAsync("/api/roles", new { name, description, isActive });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Authorized_user_creates_a_role_with_trimmed_name_and_audit_entry()
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsync("/api/roles", new { name = "  Team   Lead ", description = "  Leads a team  ", isActive = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var role = await response.ReadJsonAsync();
        Assert.Equal("Team Lead", role.GetProperty("name").GetString());
        Assert.Equal("Leads a team", role.GetProperty("description").GetString());
        Assert.Equal("admin", role.GetProperty("createdByName").GetString());
        var history = await admin.GetJsonAsync<List<JsonElement>>($"/api/roles/{role.GetProperty("id").GetGuid()}/audit");
        Assert.Contains(history, e => e.GetProperty("action").GetString() == "role.created");
    }

    [Fact]
    public async Task Users_without_permission_cannot_create_roles()
    {
        await Factory.CreateUserWithPermissionsAsync("viewer", PermissionKeys.Roles.Access, PermissionKeys.Roles.View);
        var viewer = await Factory.LoginAsync("viewer");
        var anonymous = await Factory.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/roles", new { name = "Sneaky" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/roles", new { name = "Sneaky" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/roles")).StatusCode);
    }

    [Theory]
    [InlineData("developer")]
    [InlineData("DEVELOPER")]
    [InlineData(" Developer ")]
    public async Task Duplicate_role_names_are_rejected_case_insensitively(string duplicate)
    {
        var admin = await AdminAsync();
        await CreateRoleAsync(admin, "Developer");

        var response = await admin.PostAsync("/api/roles", new { name = duplicate });

        Assert.Contains(await response.ValidationErrorsAsync("name"), e => e.Contains("already exists"));
    }

    [Theory]
    [InlineData("", "Role name is required.")]
    [InlineData("x", "Role name must be 2 to 50 characters.")]
    [InlineData("<script>", "Role names may contain")]
    public async Task Invalid_role_names_are_rejected(string name, string expected)
    {
        var admin = await AdminAsync();

        var response = await admin.PostAsync("/api/roles", new { name });

        Assert.Contains(await response.ValidationErrorsAsync("name"), e => e.StartsWith(expected));
    }

    [Fact]
    public async Task Role_permissions_can_be_saved_and_retrieved()
    {
        var admin = await AdminAsync();
        var roleId = await CreateRoleAsync(admin, "Tester");
        var ids = new[]
        {
            await Factory.PermissionIdAsync(PermissionKeys.Members.Access),
            await Factory.PermissionIdAsync(PermissionKeys.Members.View),
        };

        var save = await admin.PutAsync($"/api/roles/{roleId}/permissions", new { permissionIds = ids });

        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var change = await save.ReadJsonAsync();
        Assert.Equal(2, change.GetProperty("added").GetArrayLength());
        var stored = await admin.GetJsonAsync<RolePermissionsResponse>($"/api/roles/{roleId}/permissions");
        Assert.Equal(ids.Order(), stored.GrantedPermissionIds.Order());
        Assert.True(stored.IsEditable);
    }

    [Fact]
    public async Task Unknown_duplicate_and_unimplemented_permission_ids_are_rejected()
    {
        var admin = await AdminAsync();
        var roleId = await CreateRoleAsync(admin, "Tester");
        var view = await Factory.PermissionIdAsync(PermissionKeys.Members.View);
        var futureTask = await Factory.PermissionIdAsync("tasks.create");

        var unknown = await admin.PutAsync($"/api/roles/{roleId}/permissions", new { permissionIds = new[] { 99999 } });
        var duplicate = await admin.PutAsync($"/api/roles/{roleId}/permissions", new { permissionIds = new[] { view, view } });
        var future = await admin.PutAsync($"/api/roles/{roleId}/permissions", new { permissionIds = new[] { futureTask } });

        Assert.Contains(await unknown.ValidationErrorsAsync("permissionIds"), e => e.Contains("Unknown permission ids: 99999"));
        Assert.Contains(await duplicate.ValidationErrorsAsync("permissionIds"), e => e.Contains("Duplicate"));
        Assert.Contains(await future.ValidationErrorsAsync("permissionIds"), e => e.Contains("not available yet"));
        Assert.Equal(0, await Factory.WithDbAsync(db => db.RolePermissions.CountAsync(rp => rp.RoleId == roleId)));
    }

    [Fact]
    public async Task System_administrator_role_cannot_be_deleted_renamed_deactivated_or_have_permissions_changed()
    {
        var admin = await AdminAsync();
        var adminRoleId = await Factory.WithDbAsync(db => db.Roles.Where(r => r.IsSystem).Select(r => r.Id).SingleAsync());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/roles/{adminRoleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/roles/{adminRoleId}", new { name = "Boss", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/roles/{adminRoleId}", new { name = "Administrator", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsync($"/api/roles/{adminRoleId}/permissions", new { permissionIds = Array.Empty<int>() })).StatusCode);
        var description = await admin.PutAsync($"/api/roles/{adminRoleId}", new { name = "Administrator", description = "Full access", isActive = true });
        Assert.Equal(HttpStatusCode.OK, description.StatusCode);
    }

    [Fact]
    public async Task Role_with_members_cannot_be_deleted_without_reassignment()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer", PermissionKeys.Members.Access);
        var other = await Factory.CreateRoleAsync("Tester");
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);

        var blocked = await admin.DeleteAsync($"/api/roles/{role.Id}");
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("1 member(s)", (await blocked.ReadJsonAsync()).GetProperty("detail").GetString());

        var reassigned = await admin.DeleteAsync($"/api/roles/{role.Id}?reassignToRoleId={other.Id}");
        Assert.Equal(HttpStatusCode.NoContent, reassigned.StatusCode);
        Assert.False(await Factory.WithDbAsync(db => db.Roles.AnyAsync(r => r.Id == role.Id)));
        Assert.Equal(other.Id, await Factory.WithDbAsync(db => db.UserRoles.Where(ur => ur.UserId == member.Id).Select(ur => ur.RoleId).SingleAsync()));
    }

    [Fact]
    public async Task Empty_role_can_be_deleted()
    {
        var admin = await AdminAsync();
        var roleId = await CreateRoleAsync(admin, "Temporary");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/roles/{roleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/roles/{roleId}")).StatusCode);
    }

    [Fact]
    public async Task Role_list_supports_search_status_filter_sorting_and_paging()
    {
        var admin = await AdminAsync();
        foreach (var name in new[] { "Alpha", "Bravo", "Charlie", "Delta" })
        {
            await CreateRoleAsync(admin, name, isActive: name != "Delta");
        }

        var search = await admin.GetJsonAsync<PagedResult<RoleSummaryResponse>>("/api/roles?search=rav");
        var inactive = await admin.GetJsonAsync<PagedResult<RoleSummaryResponse>>("/api/roles?status=inactive");
        var paged = await admin.GetJsonAsync<PagedResult<RoleSummaryResponse>>("/api/roles?sort=name&direction=desc&page=2&pageSize=2");

        Assert.Equal(["Bravo"], search.Items.Select(r => r.Name));
        Assert.Equal(["Delta"], inactive.Items.Select(r => r.Name));
        Assert.Equal(5, paged.TotalCount);
        Assert.Equal(["Bravo", "Alpha"], paged.Items.Select(r => r.Name));
    }

    [Fact]
    public async Task Role_edits_are_validated_and_audited()
    {
        var admin = await AdminAsync();
        var roleId = await CreateRoleAsync(admin, "QA");
        await CreateRoleAsync(admin, "Support");

        var duplicate = await admin.PutAsync($"/api/roles/{roleId}", new { name = "support", isActive = true });
        var rename = await admin.PutAsync($"/api/roles/{roleId}", new { name = "Quality", isActive = false });

        Assert.Contains(await duplicate.ValidationErrorsAsync("name"), e => e.Contains("already exists"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var history = await admin.GetJsonAsync<List<JsonElement>>($"/api/roles/{roleId}/audit");
        Assert.Contains(history, e => e.GetProperty("summary").GetString()!.Contains("'QA' → 'Quality'"));
    }
}
