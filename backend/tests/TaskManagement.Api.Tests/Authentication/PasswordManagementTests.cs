using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Controllers;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authentication;

public class PasswordManagementTests : ApiTestBase
{
    private const string NewPassword = "a brand new passphrase";

    [Fact]
    public async Task Change_password_with_correct_current_password_succeeds_and_revokes_other_sessions()
    {
        await Factory.CreateUserAsync("alice");
        var current = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");
        var otherDevice = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        var response = await current.PostAsync("/api/auth/change-password", new
        {
            currentPassword = AuthApiFactory.DefaultPassword,
            newPassword = NewPassword,
            confirmNewPassword = NewPassword,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.Http.GetAsync("/api/test/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherDevice.Http.GetAsync("/api/test/protected")).StatusCode);
        Assert.Equal(1, await Factory.WithDbAsync(db => Task.FromResult(db.UserSessions.Count())));
        var fresh = await Factory.CreateApiClientAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.LoginAsync("alice")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.LoginAsync("alice", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Change_password_with_incorrect_current_password_fails()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        var response = await client.PostAsync("/api/auth/change-password", new
        {
            currentPassword = "definitely not it",
            newPassword = NewPassword,
            confirmNewPassword = NewPassword,
        });

        Assert.Contains("The current password is incorrect.", await response.ValidationErrorsAsync("currentPassword"));
    }

    [Theory]
    [InlineData("short", "Passwords must be at least 12 characters.")]
    [InlineData("my name is alice ok", "Passwords must not contain your user name or email address.")]
    public async Task Change_password_rejects_passwords_that_violate_the_policy(string password, string expectedError)
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        var response = await client.PostAsync("/api/auth/change-password", new
        {
            currentPassword = AuthApiFactory.DefaultPassword,
            newPassword = password,
            confirmNewPassword = password,
        });

        Assert.Contains(expectedError, await response.ValidationErrorsAsync("newPassword"));
    }

    [Fact]
    public async Task Change_password_requires_matching_confirmation()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        var response = await client.PostAsync("/api/auth/change-password", new
        {
            currentPassword = AuthApiFactory.DefaultPassword,
            newPassword = NewPassword,
            confirmNewPassword = NewPassword + "x",
        });

        Assert.NotEmpty(await response.ValidationErrorsAsync("confirmNewPassword"));
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/change-password", new
        {
            currentPassword = AuthApiFactory.DefaultPassword,
            newPassword = NewPassword,
            confirmNewPassword = NewPassword,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_does_not_disclose_whether_the_account_exists()
    {
        await Factory.CreateUserAsync("alice", "alice@example.com");
        var client = await Factory.CreateApiClientAsync();

        var known = await client.PostAsync("/api/auth/forgot-password", new { email = "alice@example.com" });
        var unknown = await client.PostAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(AuthController.ForgotPasswordMessage, (await known.ReadJsonAsync()).GetProperty("message").GetString());
        var email = Assert.Single(Factory.Emails.Sent);
        Assert.Equal("alice@example.com", email.To);
        Assert.StartsWith("https://app.test/reset-password?uid=", email.Link);
    }

    [Fact]
    public async Task Forgot_password_sends_nothing_for_inactive_accounts()
    {
        await Factory.CreateUserAsync("inactive", isActive: false);
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/forgot-password", new { email = "inactive@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(Factory.Emails.Sent);
    }

    [Fact]
    public async Task Reset_password_with_valid_token_succeeds_revokes_sessions_and_cannot_be_reused()
    {
        await Factory.CreateUserAsync("alice");
        var session = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");
        var client = await Factory.CreateApiClientAsync();
        var (userId, token) = await RequestResetAsync(client, "alice@example.com");

        var response = await client.PostAsync("/api/auth/reset-password", ResetBody(userId, token, NewPassword));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Http.GetAsync("/api/test/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("alice", NewPassword)).StatusCode);

        var reuse = await (await Factory.CreateApiClientAsync()).PostAsync("/api/auth/reset-password", ResetBody(userId, token, "yet another passphrase"));
        Assert.Contains(AuthenticationService.InvalidResetLinkMessage, await reuse.ValidationErrorsAsync("token"));
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("!!!")]
    public async Task Reset_password_with_invalid_token_fails(string token)
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/reset-password", ResetBody(user.Id.ToString(), token, NewPassword));

        Assert.Contains(AuthenticationService.InvalidResetLinkMessage, await response.ValidationErrorsAsync("token"));
    }

    [Fact]
    public async Task Reset_password_for_unknown_user_gets_the_same_invalid_token_error()
    {
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/reset-password", ResetBody(Guid.NewGuid().ToString(), "dG9rZW4", NewPassword));

        Assert.Contains(AuthenticationService.InvalidResetLinkMessage, await response.ValidationErrorsAsync("token"));
    }

    [Fact]
    public async Task Reset_password_with_expired_token_fails()
    {
        var factory = await StartAsync(new AuthApiFactory
        {
            ConfigureServices = services => services.PostConfigure<DataProtectionTokenProviderOptions>(
                o => o.TokenLifespan = TimeSpan.FromMilliseconds(1)),
        });
        await factory.CreateUserAsync("alice");
        var client = await factory.CreateApiClientAsync();
        var (userId, token) = await RequestResetAsync(client, "alice@example.com", factory);
        await Task.Delay(50);

        var response = await client.PostAsync("/api/auth/reset-password", ResetBody(userId, token, NewPassword));

        Assert.Contains(AuthenticationService.InvalidResetLinkMessage, await response.ValidationErrorsAsync("token"));
    }

    [Fact]
    public async Task Reset_password_with_valid_token_still_enforces_the_password_policy()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();
        var (userId, token) = await RequestResetAsync(client, "alice@example.com");

        var response = await client.PostAsync("/api/auth/reset-password", ResetBody(userId, token, "too short"));

        Assert.Contains("Passwords must be at least 12 characters.", await response.ValidationErrorsAsync("newPassword"));
    }

    private async Task<(string UserId, string Token)> RequestResetAsync(ApiClient client, string email, AuthApiFactory? factory = null)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/auth/forgot-password", new { email })).StatusCode);
        var link = new Uri(Assert.Single((factory ?? Factory).Emails.Sent).Link);
        var query = QueryHelpers.ParseQuery(link.Query);
        return (query["uid"].ToString(), query["token"].ToString());
    }

    private static object ResetBody(string userId, string token, string password) =>
        new { userId, token, newPassword = password, confirmNewPassword = password };
}
