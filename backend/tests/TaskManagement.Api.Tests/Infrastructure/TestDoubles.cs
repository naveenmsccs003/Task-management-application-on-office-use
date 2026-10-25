using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Identity;
using TaskManagement.Api.Data;
using TaskManagement.Api.Email;

namespace TaskManagement.Api.Tests.Infrastructure;

public sealed record SentEmail(string To, string Link, string Kind = "reset");

public sealed class CapturingEmailSender : IEmailSender<ApplicationUser>, IMemberEmailSender
{
    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        Sent.Enqueue(new SentEmail(email, WebUtility.HtmlDecode(resetLink)));
        return Task.CompletedTask;
    }

    public Task SendInvitationAsync(ApplicationUser user, string email, string setupLink)
    {
        Sent.Enqueue(new SentEmail(email, WebUtility.HtmlDecode(setupLink), "invitation"));
        return Task.CompletedTask;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) => Task.CompletedTask;

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) => Task.CompletedTask;
}

/// <summary>Controllable clock shared by the cookie handler, session store and Identity.</summary>
public sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
