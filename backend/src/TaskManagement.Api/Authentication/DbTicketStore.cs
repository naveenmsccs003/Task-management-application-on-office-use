using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authentication;

/// <summary>
/// Stores authentication tickets in SQL Server so the cookie only holds an opaque session key.
/// Deleting a row revokes that session immediately, which plain cookie authentication cannot do.
/// </summary>
public sealed class DbTicketStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider dataProtection, TimeProvider timeProvider)
    : ITicketStore
{
    /// <summary>Authentication property item carrying the session's absolute expiry (round-trip "O" format).</summary>
    public const string AbsoluteExpiresItem = "tm.absoluteExpiresUtc";

    private const string CurrentSessionIdItem = "tm.currentSessionId";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("TaskManagement.UserSessions.v1");

    public Task<string> StoreAsync(AuthenticationTicket ticket) => StoreAsync(ticket, CancellationToken.None);

    public async Task<string> StoreAsync(AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var userId = GetUserId(ticket);
        if (userId is null)
        {
            throw new InvalidOperationException("Cannot store an authentication session without a user id.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserSessions.Add(new UserSession
        {
            Id = HashKey(key),
            UserId = userId.Value,
            Ticket = Protect(ticket),
            CreatedAtUtc = now,
            LastRenewedAtUtc = now,
            ExpiresAtUtc = GetExpiry(ticket, now),
            AbsoluteExpiresAtUtc = GetAbsoluteExpiry(ticket, now),
        });
        await db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => RenewAsync(key, ticket, CancellationToken.None);

    public async Task RenewAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await db.UserSessions.FindAsync([HashKey(key)], cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Ticket = Protect(ticket);
        session.LastRenewedAtUtc = now;
        var expiry = GetExpiry(ticket, now);
        session.ExpiresAtUtc = expiry < session.AbsoluteExpiresAtUtc ? expiry : session.AbsoluteExpiresAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key) => RetrieveAsync(key, CancellationToken.None);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var ticket = await RetrieveAsync(key, cancellationToken);
        if (ticket is not null)
        {
            httpContext.Items[CurrentSessionIdItem] = HashKey(key);
        }
        return ticket;
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var id = HashKey(key);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (IsExpired(session, now))
        {
            await db.UserSessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
            return null;
        }

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (CryptographicException)
        {
            // Data-protection keys were rotated away or lost; treat the session as invalid.
            return null;
        }
    }

    public Task RemoveAsync(string key) => RemoveAsync(key, CancellationToken.None);

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var id = HashKey(key);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.UserSessions.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Id of the session that authenticated the current request, if any.</summary>
    public static string? GetCurrentSessionId(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(CurrentSessionIdItem, out var id) ? id as string : null;

    public static bool IsExpired(UserSession session, DateTime nowUtc) =>
        session.ExpiresAtUtc <= nowUtc || session.AbsoluteExpiresAtUtc <= nowUtc;

    private byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static Guid? GetUserId(AuthenticationTicket ticket) =>
        Guid.TryParse(ticket.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private static DateTime GetExpiry(AuthenticationTicket ticket, DateTime now) =>
        ticket.Properties.ExpiresUtc?.UtcDateTime ?? now.AddMinutes(30);

    private static DateTime GetAbsoluteExpiry(AuthenticationTicket ticket, DateTime now)
    {
        if (ticket.Properties.Items.TryGetValue(AbsoluteExpiresItem, out var value)
            && DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var absolute))
        {
            return absolute.UtcDateTime;
        }

        return GetExpiry(ticket, now);
    }
}
