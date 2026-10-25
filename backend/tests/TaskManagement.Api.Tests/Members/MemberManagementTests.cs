using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Common;
using TaskManagement.Api.Data;
using TaskManagement.Api.Members;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Members;

public class MemberManagementTests : ApiTestBase
{
    private const string MemberPassword = "member passphrase 123";

    private async Task<ApiClient> AdminAsync()
    {
        await Factory.CreateAdministratorAsync();
        return await Factory.LoginAsync("admin");
    }

    private static object NewMember(Guid roleId, string userName = "jane", string? email = null, object? personal = null, string setup = "Password") => new
    {
        displayName = "Jane Doe",
        email = email ?? $"{userName}@example.com",
        userName,
        roleId,
        isActive = true,
        setupMethod = setup,
        password = setup == "Password" ? MemberPassword : null,
        confirmPassword = setup == "Password" ? MemberPassword : null,
        personalInformation = personal,
    };

    [Fact]
    public async Task Authorized_user_creates_a_member_whose_password_is_stored_through_identity()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer", PermissionKeys.Members.Access);

        var response = await admin.PostAsync("/api/members", NewMember(role.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(MemberPassword, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        var member = JsonSerializer.Deserialize<MemberDetailResponse>(body, ApiClient.JsonOptions)!;
        Assert.Equal("Developer", member.Role!.Name);
        Assert.True(member.HasPassword);

        var hash = await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == member.Id).Select(u => u.PasswordHash).SingleAsync());
        Assert.NotEqual(MemberPassword, hash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<ApplicationUser>().VerifyHashedPassword(new ApplicationUser(), hash!, MemberPassword));
        Assert.Equal(HttpStatusCode.OK, (await (await Factory.CreateApiClientAsync()).LoginAsync("jane", MemberPassword)).StatusCode);
    }

    [Fact]
    public async Task Invitation_creates_a_member_without_password_who_sets_one_through_the_link_once()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");

        var response = await admin.PostAsync("/api/members", NewMember(role.Id, setup: "Invitation"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await response.ReadJsonAsync()).GetProperty("hasPassword").GetBoolean());
        var invitation = Assert.Single(Factory.Emails.Sent, e => e.Kind == "invitation");
        Assert.Contains("invite=1", invitation.Link);
        var query = QueryHelpers.ParseQuery(new Uri(invitation.Link).Query);
        var body = new
        {
            userId = query["uid"].ToString(), token = query["token"].ToString(),
            newPassword = MemberPassword, confirmNewPassword = MemberPassword, isInvitation = true,
        };
        var guest = await Factory.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await guest.PostAsync("/api/auth/reset-password", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsync("/api/auth/reset-password", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.LoginAsync("jane", MemberPassword)).StatusCode);
    }

    [Fact]
    public async Task Unauthorized_users_cannot_create_members()
    {
        var role = await Factory.CreateRoleAsync("Developer");
        await Factory.CreateUserWithPermissionsAsync("viewer", PermissionKeys.Members.Access, PermissionKeys.Members.View);
        var viewer = await Factory.LoginAsync("viewer");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/members", NewMember(role.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await Factory.CreateApiClientAsync()).PostAsync("/api/members", NewMember(role.Id))).StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_and_username_are_rejected()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        await admin.PostAsync("/api/members", NewMember(role.Id));

        var sameEmail = await admin.PostAsync("/api/members", NewMember(role.Id, "other", "JANE@example.com"));
        var sameUserName = await admin.PostAsync("/api/members", NewMember(role.Id, "jane", "new@example.com"));

        Assert.Contains("This email address is already in use.", await sameEmail.ValidationErrorsAsync("email"));
        Assert.NotEmpty(await sameUserName.ValidationErrorsAsync("userName"));
    }

    [Fact]
    public async Task Invalid_and_inactive_role_assignments_are_rejected()
    {
        var admin = await AdminAsync();
        var inactive = await Factory.CreateRoleAsync("Retired");
        await Factory.WithDbAsync(db => db.Roles.Where(r => r.Id == inactive.Id).ExecuteUpdateAsync(r => r.SetProperty(x => x.IsActive, false)));

        var unknown = await admin.PostAsync("/api/members", NewMember(Guid.NewGuid()));
        var retired = await admin.PostAsync("/api/members", NewMember(inactive.Id));

        Assert.Contains("Select a valid role.", await unknown.ValidationErrorsAsync("roleId"));
        Assert.Contains("Inactive roles cannot be assigned.", await retired.ValidationErrorsAsync("roleId"));
        Assert.Equal(1, await Factory.WithDbAsync(db => db.Users.CountAsync()));
    }

    [Fact]
    public async Task Server_validates_required_fields_passwords_and_personal_information()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");

        var missing = await admin.PostAsync("/api/members", new { email = "not-an-email", roleId = role.Id, setupMethod = "Password" });
        var weak = await admin.PostAsync("/api/members", new
        {
            displayName = "Jane", email = "jane@example.com", userName = "jane", roleId = role.Id,
            setupMethod = "Password", password = "short", confirmPassword = "short",
        });
        var future = await admin.PostAsync("/api/members", NewMember(role.Id, personal: new { dateOfBirth = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd") }));

        Assert.Contains("Full name is required.", await missing.ValidationErrorsAsync("displayName"));
        Assert.Contains("Enter a valid email address.", await missing.ValidationErrorsAsync("email"));
        // Cross-field rules run once the individual fields are valid.
        var noPassword = await admin.PostAsync("/api/members", new
        {
            displayName = "Jane", email = "jane@example.com", userName = "jane", roleId = role.Id, setupMethod = "Password",
        });
        Assert.Contains("Password is required.", await noPassword.ValidationErrorsAsync("password"));
        Assert.Contains(await weak.ValidationErrorsAsync("password"), e => e.Contains("at least 12"));
        Assert.Contains("Date of birth cannot be in the future.", await future.ValidationErrorsAsync("personalInformation.DateOfBirth"));
    }

    [Fact]
    public async Task Personal_information_is_only_returned_to_users_with_the_dedicated_permission()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        var created = await admin.PostAsync("/api/members", NewMember(role.Id, personal: new
        {
            dateOfBirth = "1990-05-17", phoneNumber = "+1 555 0100", addressLine = "1 Secret Lane", city = "Springfield", country = "US",
        }));
        var memberId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
        await Factory.CreateUserWithPermissionsAsync("viewer", PermissionKeys.Members.Access, PermissionKeys.Members.ViewDetails);
        var viewer = await Factory.LoginAsync("viewer");

        var restricted = await viewer.GetAsync($"/api/members/{memberId}");
        var full = await admin.GetJsonAsync<MemberDetailResponse>($"/api/members/{memberId}");

        var restrictedBody = await restricted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, restricted.StatusCode);
        Assert.Contains("\"personalInformation\":null", restrictedBody);
        foreach (var secret in new[] { "1 Secret Lane", "Springfield", "555 0100", "1990" })
        {
            Assert.DoesNotContain(secret, restrictedBody);
        }
        Assert.Equal("1 Secret Lane", full.PersonalInformation!.AddressLine);
        Assert.Equal(MemberService.AgeOn(new DateOnly(1990, 5, 17), DateOnly.FromDateTime(DateTime.UtcNow)), full.PersonalInformation.Age);
    }

    [Fact]
    public async Task Field_level_permissions_are_enforced_on_update_and_unknown_fields_are_ignored()
    {
        var role = await Factory.CreateRoleAsync("Developer");
        var target = await Factory.CreateUserAsync("target", roles: role.Name!);
        await Factory.CreateUserWithPermissionsAsync("editor",
            PermissionKeys.Members.Access, PermissionKeys.Members.ViewDetails, PermissionKeys.Members.Edit);
        var editor = await Factory.LoginAsync("editor");

        var email = await editor.PutAsync($"/api/members/{target.Id}", new { email = "changed@example.com" });
        var personal = await editor.PutAsync($"/api/members/{target.Id}", new { personalInformation = new { city = "Paris" } });
        var name = await editor.PutAsync($"/api/members/{target.Id}", new
        {
            displayName = "New Name", isActive = false, roleId = Guid.NewGuid(), passwordHash = "x", lockoutEnd = "2099-01-01",
        });

        Assert.Equal(HttpStatusCode.Forbidden, email.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, personal.StatusCode);
        Assert.Equal(HttpStatusCode.OK, name.StatusCode);
        var stored = await Factory.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == target.Id));
        Assert.Equal("New Name", stored.DisplayName);
        Assert.Equal("target@example.com", stored.Email);
        Assert.True(stored.IsActive);
        Assert.Null(stored.LockoutEnd);
        Assert.Equal(role.Id, await Factory.WithDbAsync(db => db.UserRoles.Where(ur => ur.UserId == target.Id).Select(ur => ur.RoleId).SingleAsync()));
    }

    [Fact]
    public async Task Deactivation_ends_sessions_blocks_login_and_reactivation_restores_access()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);
        var session = await Factory.LoginAsync("dev");

        var deactivate = await admin.PostAsync($"/api/members/{member.Id}/status", new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/test/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await Factory.CreateApiClientAsync()).LoginAsync("dev")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/members/{member.Id}/status", new { isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await Factory.CreateApiClientAsync()).LoginAsync("dev")).StatusCode);
    }

    [Fact]
    public async Task Activation_and_deactivation_need_their_own_permissions()
    {
        var role = await Factory.CreateRoleAsync("Developer");
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);
        await Factory.CreateUserWithPermissionsAsync("deactivator", PermissionKeys.Members.Access, PermissionKeys.Members.Deactivate);
        var deactivator = await Factory.LoginAsync("deactivator");

        Assert.Equal(HttpStatusCode.OK, (await deactivator.PostAsync($"/api/members/{member.Id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deactivator.PostAsync($"/api/members/{member.Id}/status", new { isActive = true })).StatusCode);
    }

    [Fact]
    public async Task Members_must_be_deactivated_before_deletion()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/members/{member.Id}")).StatusCode);
        await admin.PostAsync($"/api/members/{member.Id}/status", new { isActive = false });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/members/{member.Id}")).StatusCode);
        Assert.False(await Factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Id == member.Id)));
        var history = await admin.GetJsonAsync<List<JsonElement>>($"/api/members/{member.Id}/audit");
        Assert.Contains(history, e => e.GetProperty("action").GetString() == "member.deleted");
    }

    [Fact]
    public async Task Last_active_administrator_and_own_account_are_protected()
    {
        var admin = await AdminAsync();
        var adminId = await Factory.WithDbAsync(db => db.Users.Where(u => u.UserName == "admin").Select(u => u.Id).SingleAsync());
        var role = await Factory.CreateRoleAsync("Developer");

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/members/{adminId}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync($"/api/members/{adminId}/change-role", new { roleId = role.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/members/{adminId}")).StatusCode);

        // Another administrator may deactivate this one; afterwards the remaining administrator is the last
        // active one and, since nobody may act on their own account, cannot be removed through the API.
        var second = await Factory.CreateUserAsync("admin2", roles: AppRoles.Administrator);
        var secondClient = await Factory.LoginAsync("admin2");
        Assert.Equal(HttpStatusCode.OK, (await secondClient.PostAsync($"/api/members/{adminId}/status", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondClient.PostAsync($"/api/members/{second.Id}/status", new { isActive = false })).StatusCode);
        Assert.Equal(1, await Factory.WithDbAsync(db => db.Users.CountAsync(u => u.IsActive)));
    }

    [Fact]
    public async Task Member_list_supports_search_role_and_status_filters_and_paging()
    {
        var admin = await AdminAsync();
        var dev = await Factory.CreateRoleAsync("Developer");
        var qa = await Factory.CreateRoleAsync("Tester");
        await Factory.CreateUserAsync("alice", roles: dev.Name!);
        await Factory.CreateUserAsync("bob", roles: qa.Name!);
        var carol = await Factory.CreateUserAsync("carol", roles: dev.Name!);
        await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == carol.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)));

        var byRole = await admin.GetJsonAsync<PagedResult<MemberSummaryResponse>>($"/api/members?roleId={dev.Id}");
        var active = await admin.GetJsonAsync<PagedResult<MemberSummaryResponse>>($"/api/members?roleId={dev.Id}&status=active");
        var search = await admin.GetJsonAsync<PagedResult<MemberSummaryResponse>>("/api/members?search=BOB@EXAMPLE");
        var paged = await admin.GetJsonAsync<PagedResult<MemberSummaryResponse>>("/api/members?sort=name&page=2&pageSize=2");

        Assert.Equal(["alice", "carol"], byRole.Items.Select(m => m.UserName).Order());
        Assert.Equal(["alice"], active.Items.Select(m => m.UserName));
        Assert.Equal("Tester", Assert.Single(search.Items).Role!.Name);
        Assert.Equal(4, paged.TotalCount);
        Assert.Equal(2, paged.Items.Count);
    }

    [Fact]
    public async Task Export_requires_permission_and_neutralizes_formula_cells()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        var member = await Factory.CreateUserAsync("evil", roles: role.Name!);
        await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.DisplayName, "=HYPERLINK(\"x\")")));
        await Factory.CreateUserWithPermissionsAsync("viewer", PermissionKeys.Members.Access, PermissionKeys.Members.View);

        var csv = await admin.GetAsync("/api/members/export");
        var denied = await (await Factory.LoginAsync("viewer")).GetAsync("/api/members/export");

        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", text);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Reset_password_sends_a_link_and_requires_permission()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);

        var response = await admin.PostAsync($"/api/members/{member.Id}/reset-password");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("dev@example.com", Assert.Single(Factory.Emails.Sent).To);
        await Factory.CreateUserWithPermissionsAsync("viewer", PermissionKeys.Members.Access, PermissionKeys.Members.ViewDetails);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Factory.LoginAsync("viewer")).PostAsync($"/api/members/{member.Id}/reset-password")).StatusCode);
    }

    [Fact]
    public async Task Profile_photo_accepts_only_real_images_up_to_one_megabyte()
    {
        var admin = await AdminAsync();
        var role = await Factory.CreateRoleAsync("Developer");
        var member = await Factory.CreateUserAsync("dev", roles: role.Name!);
        static MultipartFormDataContent Upload(byte[] bytes, string type)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(type);
            return new MultipartFormDataContent { { file, "file", "photo" } };
        }
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        var fake = await admin.SendAsync(HttpMethod.Put, $"/api/members/{member.Id}/photo", Upload("<html>"u8.ToArray(), "image/png"));
        var tooBig = await admin.SendAsync(HttpMethod.Put, $"/api/members/{member.Id}/photo", Upload([.. png, .. new byte[1024 * 1024]], "image/png"));
        var ok = await admin.SendAsync(HttpMethod.Put, $"/api/members/{member.Id}/photo", Upload(png, "application/octet-stream"));

        Assert.Contains("Only JPEG and PNG images are supported.", await fake.ValidationErrorsAsync("file"));
        Assert.Contains("The image must be 1 MB or smaller.", await tooBig.ValidationErrorsAsync("file"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        var photo = await admin.GetAsync($"/api/members/{member.Id}/photo");
        Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
    }
}
