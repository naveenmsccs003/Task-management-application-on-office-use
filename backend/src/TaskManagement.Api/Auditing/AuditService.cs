using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Auditing;

public static class AuditTargets
{
    public const string Role = "Role";
    public const string Member = "Member";
}

public sealed record AuditEntryResponse(long Id, DateTime TimestampUtc, Guid? ActorUserId, string? ActorName, string Action, string Summary);

/// <summary>Records role, permission and member administration events. Callers must never pass secrets or personal values.</summary>
public sealed class AuditService(ApplicationDbContext db, TimeProvider timeProvider)
{
    /// <summary>Adds an entry to the current unit of work; it is saved with the caller's next SaveChanges.</summary>
    public void Add(Guid? actorUserId, string action, string targetType, string targetId, string summary) =>
        db.AuditLogs.Add(new AuditLogEntry
        {
            TimestampUtc = timeProvider.GetUtcNow().UtcDateTime,
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Summary = summary.Length > 2000 ? summary[..2000] : summary,
        });

    public async Task RecordAsync(Guid? actorUserId, string action, string targetType, string targetId, string summary)
    {
        Add(actorUserId, action, targetType, targetId, summary);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<AuditEntryResponse>> GetForTargetAsync(string targetType, string targetId, int take = 50)
    {
        var entries = await db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == targetType && a.TargetId == targetId)
            .OrderByDescending(a => a.TimestampUtc).ThenByDescending(a => a.Id)
            .Take(take)
            .ToListAsync();
        var actorIds = entries.Where(e => e.ActorUserId.HasValue).Select(e => e.ActorUserId!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, Name = u.DisplayName ?? u.UserName })
            .ToDictionaryAsync(u => u.Id, u => u.Name);
        return entries.Select(e => new AuditEntryResponse(
            e.Id, e.TimestampUtc, e.ActorUserId,
            e.ActorUserId is { } id ? names.GetValueOrDefault(id) ?? "Deleted user" : "System",
            e.Action, e.Summary)).ToList();
    }
}
