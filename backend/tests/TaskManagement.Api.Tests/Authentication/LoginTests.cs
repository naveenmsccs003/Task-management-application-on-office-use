using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Controllers;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authentication;

public class LoginTests : ApiTestBase
{
    [Fact]
    public async Task Valid_credentials_by_user_name_establish_a_session()
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var response = await client.LoginAsync("alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(user.Id, body.GetProperty("id").GetGuid());
        Assert.Equal("alice", body.GetProperty("userName").GetString());
        var status = await client.GetStatusAsync();
        Assert.True(status.IsAuthenticated);
        Assert.Equal(user.Id, status.User!.Id);
    }

    [Fact]
    public async Task Valid_credentials_by_email_establish_a_session()
    {
        await Factory.CreateUserAsync("alice", "Alice@Example.com");
        var client = await Factory.CreateApiClientAsync();

        var response = await client.LoginAsync("alice@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await client.GetStatusAsync()).IsAuthenticated);
    }

    [Fact]
    public async Task Login_response_does_not_expose_sensitive_fields()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var text = await (await client.LoginAsync("alice")).Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "passwordHash", "securityStamp", "concurrencyStamp", AuthApiFactory.DefaultPassword })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Invalid_password_and_unknown_user_get_the_same_generic_401()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var wrongPassword = await client.LoginAsync("alice", "not the right password");
        var unknownUser = await client.LoginAsync("nobody", "not the right password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        var wrongDetail = (await wrongPassword.ReadJsonAsync()).GetProperty("detail").GetString();
        var unknownDetail = (await unknownUser.ReadJsonAsync()).GetProperty("detail").GetString();
        Assert.Equal(AuthController.InvalidCredentialsMessage, wrongDetail);
        Assert.Equal(wrongDetail, unknownDetail);
        Assert.False((await client.GetStatusAsync()).IsAuthenticated);
    }

    [Fact]
    public async Task Empty_user_name_is_rejected_by_server_validation()
    {
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/login", new { userNameOrEmail = "", password = "whatever password" });

        Assert.Contains("Username or email is required.", await response.ValidationErrorsAsync("userNameOrEmail"));
    }

    [Fact]
    public async Task Empty_password_is_rejected_by_server_validation()
    {
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/login", new { userNameOrEmail = "alice", password = "" });

        Assert.Contains("Password is required.", await response.ValidationErrorsAsync("password"));
    }

    [Fact]
    public async Task Inactive_account_cannot_sign_in_and_gets_the_generic_message()
    {
        await Factory.CreateUserAsync("inactive", isActive: false);
        var client = await Factory.CreateApiClientAsync();

        var response = await client.LoginAsync("inactive");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AuthController.InvalidCredentialsMessage, (await response.ReadJsonAsync()).GetProperty("detail").GetString());
        Assert.Null(client.SessionCookie);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_and_block_even_the_correct_password()
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("alice", "wrong password attempt")).StatusCode);
        }
        var withCorrectPassword = await client.LoginAsync("alice");

        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);
        Assert.Equal(AuthController.InvalidCredentialsMessage, (await withCorrectPassword.ReadJsonAsync()).GetProperty("detail").GetString());
        var lockoutEnd = await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.LockoutEnd).SingleAsync());
        Assert.NotNull(lockoutEnd);
        Assert.True(lockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task Locked_account_can_sign_in_again_after_the_lockout_expires()
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.LoginAsync("alice", "wrong password attempt");
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("alice")).StatusCode);

        // Identity evaluates lockout against the system clock, so simulate the 15 minutes passing.
        await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.LockoutEnd, DateTimeOffset.UtcNow.AddSeconds(-1))));

        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("alice")).StatusCode);
    }

    [Fact]
    public async Task Successful_login_resets_the_failed_attempt_counter()
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await client.LoginAsync("alice", "wrong password attempt");
        }

        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("alice")).StatusCode);

        var failedCount = await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id).Select(u => u.AccessFailedCount).SingleAsync());
        Assert.Equal(0, failedCount);
    }

    [Fact]
    public async Task Remember_me_issues_a_persistent_cookie()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var response = await client.LoginAsync("alice", rememberMe: true);

        var sessionCookie = GetSetCookie(response, AuthenticationSetup.SessionCookieName);
        Assert.Contains("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_remember_me_the_cookie_is_a_browser_session_cookie()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var response = await client.LoginAsync("alice", rememberMe: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessionCookie = GetSetCookie(response, AuthenticationSetup.SessionCookieName);
        Assert.DoesNotContain("expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_persistent_session_expires_after_the_idle_timeout()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        Factory.Clock.Advance(TimeSpan.FromMinutes(121));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
    }

    [Fact]
    public async Task Remember_me_session_cannot_be_extended_past_its_absolute_lifetime()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();
        await client.LoginAsync("alice", rememberMe: true);

        // Daily activity keeps the sliding window alive...
        for (var day = 1; day < 30; day++)
        {
            Factory.Clock.Advance(TimeSpan.FromDays(1));
            Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
        }

        // ...but never beyond the configured maximum lifetime.
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
    }

    [Fact]
    public async Task Login_without_antiforgery_token_is_rejected()
    {
        await Factory.CreateUserAsync("alice");
        var client = await Factory.CreateApiClientAsync();

        var response = await client.PostAsync("/api/auth/login",
            new { userNameOrEmail = "alice", password = AuthApiFactory.DefaultPassword }, includeAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(client.SessionCookie);
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client()
    {
        var factory = await StartAsync(new AuthApiFactory { ConfigureAuth = o => o.LoginRateLimitPermits = 3 });
        var client = await factory.CreateApiClientAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync("nobody", "some password")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.LoginAsync("nobody", "some password")).StatusCode);
    }

    private static string GetSetCookie(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var values));
        return Assert.Single(values, v => v.StartsWith(name + "=", StringComparison.Ordinal));
    }
}
