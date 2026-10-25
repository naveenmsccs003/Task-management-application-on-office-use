using Microsoft.EntityFrameworkCore;
using TaskManagement.Api.Data;

namespace TaskManagement.Api.Authentication;

/// <summary>Periodically deletes expired rows from the server-side session store.</summary>
public sealed class SessionCleanupService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<SessionCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = timeProvider.GetUtcNow().UtcDateTime;
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var removed = await db.UserSessions
                    .Where(s => s.ExpiresAtUtc <= now || s.AbsoluteExpiresAtUtc <= now)
                    .ExecuteDeleteAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} expired authentication sessions.", removed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Expired session cleanup failed.");
            }
        }
    }
}
