using System.Net;
using TaskManagement.Api.Authentication;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authentication;

public class LogoutTests : ApiTestBase
{
    [Fact]
    public async Task Logout_ends_the_session_and_clears_the_cookie()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");
        Assert.NotNull(client.SessionCookie);

        var response = await client.LogoutAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(client.SessionCookie);
        Assert.False((await client.GetStatusAsync()).IsAuthenticated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
    }

    [Fact]
    public async Task Session_cookie_replayed_after_logout_is_rejected_server_side()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");
        var stolenCookie = client.SessionCookie!;

        await client.LogoutAsync();

        var attacker = new ApiClient(Factory);
        attacker.Cookies.Add(ApiClient.BaseAddress, new Cookie(AuthenticationSetup.SessionCookieName, stolenCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.Http.GetAsync("/api/test/protected")).StatusCode);
        Assert.Equal(0, await Factory.WithDbAsync(db => Task.FromResult(db.UserSessions.Count())));
    }

    [Fact]
    public async Task Repeated_logout_requests_are_handled_safely()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        Assert.Equal(HttpStatusCode.NoContent, (await client.LogoutAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.LogoutAsync()).StatusCode);
    }

    [Fact]
    public async Task Logout_without_a_session_succeeds()
    {
        var client = await Factory.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.LogoutAsync()).StatusCode);
    }
}
