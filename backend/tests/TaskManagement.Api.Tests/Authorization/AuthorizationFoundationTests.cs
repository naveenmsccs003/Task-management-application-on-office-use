using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authorization;

public class AuthorizationFoundationTests : ApiTestBase
{
    [Theory]
    [InlineData("/api/test/protected")]
    [InlineData("/api/test/admin")]
    [InlineData("/api/test/tasks-create")]
    public async Task Unauthenticated_requests_get_401(string path)
    {
        var client = await Factory.CreateApiClientAsync();

        var response = await client.Http.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Authenticated_users_do_not_automatically_get_administrator_access()
    {
        await Factory.CreateUserAsync("alice");
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("alice");

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/test/protected")).StatusCode);
        // 403 (authenticated but not allowed) is distinct from 401 (not authenticated).
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.GetAsync("/api/test/admin")).StatusCode);
        Assert.Empty((await client.GetStatusAsync()).User!.Roles);
    }

    [Fact]
    public async Task Administrator_role_satisfies_the_administrator_policy()
    {
        await Factory.CreateAdministratorAsync();
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("admin");

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/test/admin")).StatusCode);
        Assert.Contains(AppRoles.Administrator, (await client.GetStatusAsync()).User!.Roles);
    }

    [Fact]
    public async Task Permissions_are_denied_by_default_even_for_administrators()
    {
        await Factory.CreateAdministratorAsync();
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.GetAsync("/api/test/tasks-create")).StatusCode);
    }

    [Fact]
    public async Task A_permission_service_can_be_plugged_in_without_changing_authentication()
    {
        var factory = await StartAsync(new AuthApiFactory
        {
            ConfigureServices = services =>
            {
                services.RemoveAll<IPermissionService>();
                services.AddSingleton<IPermissionService>(new GrantingPermissionService("tasks.create"));
            },
        });
        await factory.CreateUserAsync("alice");
        var client = await (await factory.CreateApiClientAsync()).LoggedInAsync("alice");

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/test/tasks-create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.Http.GetAsync("/api/test/admin")).StatusCode);
    }

    [Fact]
    public async Task Unknown_api_routes_return_404_not_the_spa()
    {
        var client = await Factory.CreateApiClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.Http.GetAsync("/api/does-not-exist")).StatusCode);
    }

    private sealed class GrantingPermissionService(params string[] granted) : IPermissionService
    {
        public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
            Task.FromResult(granted.Contains(permission));
    }
}
