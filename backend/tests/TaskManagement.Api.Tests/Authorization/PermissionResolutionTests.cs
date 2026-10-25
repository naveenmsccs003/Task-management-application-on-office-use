using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Data;
using TaskManagement.Api.Members;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authorization;

/// <summary>Pure rules of the central resolver.</summary>
public class PermissionResolverRuleTests
{
    private static readonly List<Permission> Catalog =
    [
        new() { Id = 1, Key = "m.access", DisplayName = "", Description = "", ModuleKey = "m", Category = "", AllowMemberOverride = true, IsActive = true },
        new() { Id = 2, Key = "m.view", DisplayName = "", Description = "", ModuleKey = "m", Category = "", ParentPermissionId = 1, AllowMemberOverride = true, IsActive = true },
        new() { Id = 3, Key = "m.secure", DisplayName = "", Description = "", ModuleKey = "m", Category = "", ParentPermissionId = 1, AllowMemberOverride = false, IsActive = true },
    ];

    private static IReadOnlySet<string> Resolve(int[] role, params (int Id, PermissionEffect Effect)[] overrides) =>
        PermissionService.Resolve(Catalog, false, role.ToHashSet(), overrides.ToDictionary(o => o.Id, o => o.Effect));

    [Fact]
    public void Inherit_uses_the_role_default() =>
        Assert.Equal(["m.access", "m.view"], Resolve([1, 2]).Order());

    [Fact]
    public void Deny_overrides_a_role_allow() =>
        Assert.Equal(["m.access"], Resolve([1, 2], (2, PermissionEffect.Deny)));

    [Fact]
    public void Allow_adds_a_delegable_permission() =>
        Assert.Contains("m.view", Resolve([1], (2, PermissionEffect.Allow)));

    [Fact]
    public void Allow_cannot_grant_a_non_delegable_permission() =>
        Assert.DoesNotContain("m.secure", Resolve([1], (3, PermissionEffect.Allow)));

    [Fact]
    public void Child_permissions_require_the_module_tab_access()
    {
        Assert.Empty(Resolve([2]));
        Assert.Empty(Resolve([1, 2], (1, PermissionEffect.Deny)));
    }

    [Fact]
    public void Nothing_is_granted_by_default() => Assert.Empty(Resolve([]));

    [Fact]
    public void Administrators_get_every_active_permission() =>
        Assert.Equal(3, PermissionService.Resolve(Catalog, true, new HashSet<int>(), new Dictionary<int, PermissionEffect>()).Count);
}

public class PermissionResolutionTests : ApiTestBase
{
    private async Task<IReadOnlySet<string>> EffectiveAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetEffectivePermissionsAsync(userId);
    }

    [Fact]
    public async Task Members_receive_their_role_permissions_and_me_reports_them()
    {
        await Factory.CreateUserWithPermissionsAsync("dev", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var client = await Factory.LoginAsync("dev");

        var me = (await client.GetStatusAsync()).User!;

        Assert.Equal([PermissionKeys.Members.Access, PermissionKeys.Members.View], me.Permissions.Order());
        Assert.False(me.IsAdministrator);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/members")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/roles")).StatusCode);
    }

    [Fact]
    public async Task Role_permission_changes_apply_to_members_on_their_next_request()
    {
        await Factory.CreateAdministratorAsync();
        var admin = await Factory.LoginAsync("admin");
        var role = await Factory.CreateRoleAsync("Developer", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        await Factory.CreateUserAsync("dev", roles: role.Name!);
        var dev = await Factory.LoginAsync("dev");
        Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync("/api/members")).StatusCode);

        var accessOnly = new[] { await Factory.PermissionIdAsync(PermissionKeys.Members.Access) };
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync($"/api/roles/{role.Id}/permissions", new { permissionIds = accessOnly })).StatusCode);

        // No cache survives the change: the very next request is denied.
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/members")).StatusCode);
        Assert.DoesNotContain(PermissionKeys.Members.View, (await dev.GetStatusAsync()).User!.Permissions);
    }

    [Fact]
    public async Task Role_changes_update_effective_permissions()
    {
        await Factory.CreateAdministratorAsync();
        var admin = await Factory.LoginAsync("admin");
        var viewer = await Factory.CreateRoleAsync("Viewer", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var nobody = await Factory.CreateRoleAsync("Nobody");
        var member = await Factory.CreateUserAsync("dev", roles: viewer.Name!);
        var dev = await Factory.LoginAsync("dev");

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/members/{member.Id}/change-role", new { roleId = nobody.Id })).StatusCode);

        Assert.Empty(await EffectiveAsync(member.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/members")).StatusCode);
    }

    [Fact]
    public async Task Tab_access_is_enforced_on_the_backend()
    {
        await Factory.CreateUserWithPermissionsAsync("dev", PermissionKeys.Members.View, PermissionKeys.Members.ViewDetails);
        var dev = await Factory.LoginAsync("dev");

        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/members")).StatusCode);
        Assert.Empty((await dev.GetStatusAsync()).User!.Permissions);
    }

    [Fact]
    public async Task Inactive_roles_grant_nothing()
    {
        var role = await Factory.CreateRoleAsync("Developer", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);

        await Factory.WithDbAsync(db => db.Roles.Where(r => r.Id == role.Id).ExecuteUpdateAsync(r => r.SetProperty(x => x.IsActive, false)));

        Assert.Empty(await EffectiveAsync(member.Id));
    }

    [Fact]
    public async Task Unknown_and_unimplemented_permissions_are_denied_even_for_administrators()
    {
        var admin = await Factory.CreateAdministratorAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        Assert.False(await service.HasPermissionAsync(admin.Id, "members.launch_rockets"));
        Assert.False(await service.HasPermissionAsync(admin.Id, "tasks.create"));
        Assert.True(await service.HasPermissionAsync(admin.Id, PermissionKeys.Roles.ManagePermissions));
    }

    [Theory]
    [InlineData("GET", "/api/members")]
    [InlineData("GET", "/api/members/export")]
    [InlineData("POST", "/api/members")]
    [InlineData("GET", "/api/roles")]
    [InlineData("POST", "/api/roles")]
    [InlineData("GET", "/api/permissions")]
    [InlineData("GET", "/api/roles/assignable")]
    public async Task Authenticated_users_without_permissions_cannot_reach_management_endpoints(string method, string path)
    {
        await Factory.CreateUserAsync("plain");
        var plain = await Factory.LoginAsync("plain");
        var anonymous = await Factory.CreateApiClientAsync();

        var asPlain = await plain.SendAsync(new HttpMethod(method), path, method == "GET" ? null : new { });
        var asAnonymous = await anonymous.SendAsync(new HttpMethod(method), path, method == "GET" ? null : new { });

        Assert.Equal(HttpStatusCode.Forbidden, asPlain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }

    [Fact]
    public async Task Guessing_member_sub_resources_without_permission_is_forbidden()
    {
        var role = await Factory.CreateRoleAsync("Developer");
        var target = await Factory.CreateUserAsync("target", roles: role.Name!);
        await Factory.CreateUserWithPermissionsAsync("viewer", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var viewer = await Factory.LoginAsync("viewer");

        foreach (var (method, path) in new[]
        {
            ("GET", $"/api/members/{target.Id}"), ("GET", $"/api/members/{target.Id}/permissions"),
            ("PUT", $"/api/members/{target.Id}/permissions"), ("GET", $"/api/members/{target.Id}/audit"),
            ("POST", $"/api/members/{target.Id}/change-role"), ("POST", $"/api/members/{target.Id}/reset-password"),
            ("DELETE", $"/api/members/{target.Id}"), ("POST", $"/api/members/{target.Id}/status"),
        })
        {
            var body = method == "POST" && path.EndsWith("status") ? new { isActive = false } : method is "GET" or "DELETE" ? null : (object)new { };
            var response = await viewer.SendAsync(new HttpMethod(method), path, body);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} returned {(int)response.StatusCode}");
        }
    }
}

public class MemberOverrideTests : ApiTestBase
{
    private async Task<(ApiClient Admin, ApplicationRole Role, ApplicationUser A, ApplicationUser B)> SetupAsync()
    {
        await Factory.CreateAdministratorAsync();
        var admin = await Factory.LoginAsync("admin");
        var role = await Factory.CreateRoleAsync("Developer", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var a = await Factory.CreateUserAsync("member-a", roles: role.Name!);
        var b = await Factory.CreateUserAsync("member-b", roles: role.Name!);
        return (admin, role, a, b);
    }

    private async Task<HttpResponseMessage> SetOverridesAsync(ApiClient client, Guid memberId, params (string Key, string Effect)[] overrides)
    {
        var list = new List<object>();
        foreach (var (key, effect) in overrides)
        {
            list.Add(new { permissionId = await Factory.PermissionIdAsync(key), effect });
        }
        return await client.PutAsync($"/api/members/{memberId}/permissions", new { overrides = list });
    }

    private async Task<MemberPermissionItem> ItemAsync(ApiClient client, Guid memberId, string key) =>
        (await client.GetJsonAsync<MemberPermissionsResponse>($"/api/members/{memberId}/permissions")).Items.Single(i => i.Key == key);

    [Fact]
    public async Task Explicit_allow_affects_only_that_member_and_not_the_role()
    {
        var (admin, role, a, b) = await SetupAsync();

        Assert.Equal(HttpStatusCode.OK, (await SetOverridesAsync(admin, b.Id, (PermissionKeys.Members.ViewDetails, "Allow"))).StatusCode);

        var forB = await ItemAsync(admin, b.Id, PermissionKeys.Members.ViewDetails);
        var forA = await ItemAsync(admin, a.Id, PermissionKeys.Members.ViewDetails);
        Assert.True(forB.Effective);
        Assert.Equal(PermissionEffect.Allow, forB.Override);
        Assert.Equal("Explicitly allowed", forB.Source);
        Assert.False(forB.RoleDefault);
        Assert.False(forA.Effective);
        Assert.Equal(2, await Factory.WithDbAsync(db => db.RolePermissions.CountAsync(rp => rp.RoleId == role.Id)));
        Assert.Equal(HttpStatusCode.OK, (await (await Factory.LoginAsync("member-b")).GetAsync($"/api/members/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Factory.LoginAsync("member-a")).GetAsync($"/api/members/{b.Id}")).StatusCode);
    }

    [Fact]
    public async Task Explicit_deny_overrides_role_allow_and_removing_it_restores_inheritance()
    {
        var (admin, _, a, _) = await SetupAsync();
        var member = await Factory.LoginAsync("member-a");

        await SetOverridesAsync(admin, a.Id, (PermissionKeys.Members.View, "Deny"));
        var denied = await ItemAsync(admin, a.Id, PermissionKeys.Members.View);
        Assert.True(denied.RoleDefault);
        Assert.False(denied.Effective);
        Assert.Equal("Explicitly denied", denied.Source);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/members")).StatusCode);

        await SetOverridesAsync(admin, a.Id);
        var inherited = await ItemAsync(admin, a.Id, PermissionKeys.Members.View);
        Assert.Null(inherited.Override);
        Assert.True(inherited.Effective);
        Assert.Equal("Inherited from role", inherited.Source);
        Assert.Equal(0, await Factory.WithDbAsync(db => db.MemberPermissionOverrides.CountAsync()));
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/members")).StatusCode);
    }

    [Fact]
    public async Task Non_delegable_permissions_cannot_be_granted_to_individuals()
    {
        var (admin, _, a, _) = await SetupAsync();

        var response = await SetOverridesAsync(admin, a.Id, (PermissionKeys.Roles.ManagePermissions, "Allow"));

        Assert.Contains(await response.ValidationErrorsAsync("overrides"), e => e.Contains("only be granted through a role"));
    }

    [Fact]
    public async Task Invalid_override_requests_are_rejected()
    {
        var (admin, _, a, _) = await SetupAsync();
        var view = await Factory.PermissionIdAsync(PermissionKeys.Members.View);

        var unknown = await admin.PutAsync($"/api/members/{a.Id}/permissions", new { overrides = new[] { new { permissionId = 4242, effect = "Allow" } } });
        var duplicate = await admin.PutAsync($"/api/members/{a.Id}/permissions", new
        {
            overrides = new[] { new { permissionId = view, effect = "Allow" }, new { permissionId = view, effect = "Deny" } },
        });
        var future = await SetOverridesAsync(admin, a.Id, ("tasks.create", "Allow"));

        Assert.NotEmpty(await unknown.ValidationErrorsAsync("overrides"));
        Assert.NotEmpty(await duplicate.ValidationErrorsAsync("overrides"));
        Assert.NotEmpty(await future.ValidationErrorsAsync("overrides"));
    }

    [Fact]
    public async Task Overrides_do_not_apply_to_administrators()
    {
        var (admin, _, _, _) = await SetupAsync();
        var other = await Factory.CreateUserAsync("admin2", roles: AppRoles.Administrator);

        Assert.Equal(HttpStatusCode.Conflict, (await SetOverridesAsync(admin, other.Id, (PermissionKeys.Members.View, "Deny"))).StatusCode);
    }

    [Fact]
    public async Task Unauthorized_users_cannot_change_overrides()
    {
        var (_, _, a, _) = await SetupAsync();
        var member = await Factory.LoginAsync("member-b");

        Assert.Equal(HttpStatusCode.Forbidden, (await SetOverridesAsync(member, a.Id, (PermissionKeys.Members.View, "Deny"))).StatusCode);
    }

    [Fact]
    public async Task Override_changes_are_audited()
    {
        var (admin, _, a, _) = await SetupAsync();

        await SetOverridesAsync(admin, a.Id, (PermissionKeys.Members.View, "Deny"));

        var history = await admin.GetJsonAsync<List<System.Text.Json.JsonElement>>($"/api/members/{a.Id}/audit");
        Assert.Contains(history, e => e.GetProperty("summary").GetString()!.Contains("members.view: Inherit → Deny"));
    }
}
