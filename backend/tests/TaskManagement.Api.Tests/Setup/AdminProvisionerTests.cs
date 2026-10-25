using System.Net;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Api.Authorization;
using TaskManagement.Api.Setup;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Setup;

public class AdminProvisionerTests : ApiTestBase
{
    private async Task<AdminProvisioningResult> ProvisionAsync(string userName, string email, string password)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AdminProvisioner>()
            .CreateInitialAdministratorAsync(userName, email, "Administrator", password);
    }

    [Fact]
    public async Task Creates_the_first_administrator_who_can_sign_in_with_administrator_access()
    {
        var result = await ProvisionAsync("admin", "admin@example.com", "first operator passphrase");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var client = await (await Factory.CreateApiClientAsync()).LoggedInAsync("admin", "first operator passphrase");
        Assert.Contains(AppRoles.Administrator, (await client.GetStatusAsync()).User!.Roles);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/api/test/admin")).StatusCode);
    }

    [Fact]
    public async Task Refuses_to_run_again_once_setup_is_completed()
    {
        Assert.True((await ProvisionAsync("admin", "admin@example.com", "first operator passphrase")).Succeeded);

        var second = await ProvisionAsync("intruder", "intruder@example.com", "second operator passphrase");

        Assert.False(second.Succeeded);
        Assert.Contains(AdminProvisioner.AlreadyCompletedMessage, second.Errors);
        Assert.Equal(1, await Factory.WithDbAsync(db => Task.FromResult(db.Users.Count())));
    }

    [Fact]
    public async Task Rejects_a_weak_password_and_leaves_setup_incomplete()
    {
        var result = await ProvisionAsync("admin", "admin@example.com", "short");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("at least 12 characters"));
        Assert.Equal(0, await Factory.WithDbAsync(db => Task.FromResult(db.Users.Count())));
        Assert.Equal(0, await Factory.WithDbAsync(db => Task.FromResult(db.Roles.Count())));
    }
}
