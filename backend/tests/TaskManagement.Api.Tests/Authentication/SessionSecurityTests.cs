using System.Net;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authentication;

public class SessionSecurityTests : ApiTestBase
{
    [Fact]
    public async Task Deactivated_user_loses_existing_sessions_at_the_next_security_check()
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)));
        Factory.Clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
        Assert.False((await client.GetStatusAsync()).IsAuthenticated);
    }

    [Fact]
    public async Task Me_reports_anonymous_for_a_deactivated_account_immediately()
    {
        var user = await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false)));

        Assert.False((await client.GetStatusAsync()).IsAuthenticated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_endpoints_are_rate_limited_per_client()
    {
        var factory = await StartAsync(new AuthApiFactory { ConfigureAuth = o => o.PasswordResetRateLimitPermits = 2 });
        var client = await factory.CreateApiClientAsync();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/auth/forgot-password", new { email = "a@example.com" })).StatusCode);
        }

        var limited = await client.PostAsync("/api/auth/forgot-password", new { email = "a@example.com" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Too many attempts", (await limited.ReadJsonAsync()).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Api_responses_carry_security_headers_and_are_not_cached()
    {
        var client = await Factory.CreateApiClientAsync();

        var response = await client.Http.GetAsync("/api/auth/me");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }
}
