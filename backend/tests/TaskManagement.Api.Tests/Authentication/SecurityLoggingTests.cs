using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskManagement.Api.Tests.Infrastructure;

namespace TaskManagement.Api.Tests.Authentication;

public class SecurityLoggingTests : ApiTestBase
{
    private readonly CapturingLoggerProvider _logs = new();

    [Fact]
    public async Task Authentication_events_are_logged_with_user_ids_and_never_with_secrets()
    {
        var factory = await StartAsync(new AuthApiFactory { ConfigureServices = s => s.AddSingleton<ILoggerProvider>(_logs) });
        var user = await factory.CreateUserAsync("alice", "alice@example.com");
        var client = await factory.CreateApiClientAsync();
        const string secretIdentifier = "unknown.person.secret";
        const string wrongPassword = "wrong password value";
        const string newPassword = "a brand new passphrase";
        const string resetPassword = "a reset passphrase here";

        await client.LoginAsync(secretIdentifier, wrongPassword);
        await client.LoginAsync("alice", wrongPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync("alice")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/change-password",
            new { currentPassword = AuthApiFactory.DefaultPassword, newPassword, confirmNewPassword = newPassword })).StatusCode);
        await client.LogoutAsync();
        await client.PostAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });
        await client.PostAsync("/api/auth/forgot-password", new { email = "alice@example.com" });
        var link = new Uri(Assert.Single(factory.Emails.Sent).Link);
        var token = QueryHelpers.ParseQuery(link.Query)["token"].ToString();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/reset-password",
            new { userId = user.Id.ToString(), token, newPassword = resetPassword, confirmNewPassword = resetPassword })).StatusCode);
        await client.PostAsync("/api/auth/reset-password",
            new { userId = user.Id.ToString(), token, newPassword = resetPassword, confirmNewPassword = resetPassword });

        var security = _logs.Entries.Where(e => e.EventId.Id is >= 1000 and < 1100).ToList();
        foreach (var expectedEvent in new[] { 1000, 1001, 1002, 1010, 1020, 1030, 1031, 1032, 1033 })
        {
            Assert.Contains(security, e => e.EventId.Id == expectedEvent);
        }
        Assert.All(security.Where(e => e.EventId.Id is 1000 or 1001 or 1010 or 1020 or 1030 or 1032),
            e => Assert.Contains(user.Id.ToString(), e.Message));

        var everything = string.Join("\n", _logs.Entries.Select(e => e.Message));
        foreach (var secret in new[] { AuthApiFactory.DefaultPassword, wrongPassword, newPassword, resetPassword, token, secretIdentifier, client.SessionCookie ?? "no-cookie" })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }
}
