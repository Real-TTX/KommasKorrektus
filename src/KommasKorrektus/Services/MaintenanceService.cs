using KommasKorrektus.Data;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Services;

/// <summary>What was cleaned up: how many rows of each kind were removed.</summary>
public sealed record MaintenanceResult(int Sessions, int LoginCodes, int ActivityEntries, int MailEntries, int BackupRuns, int ShareAccesses)
{
    public int Total => Sessions + LoginCodes + ActivityEntries + MailEntries + BackupRuns + ShareAccesses;
}

/// <summary>
/// Keeps the tables that only grow small: sessions that ended long ago, used or expired codes, and the logs after a year. Runs once a day (and on request on
/// the system page). It deletes with single statements and never touches what customers and projects are made of.
/// </summary>
public sealed class MaintenanceService(IServiceScopeFactory scopes, ILogger<MaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var result = await RunAsync(scope.ServiceProvider.GetRequiredService<KkDbContext>(), scope.ServiceProvider.GetRequiredService<TimeProvider>(), stoppingToken);
                    if (result.Total > 0)
                    {
                        logger.LogInformation("Maintenance removed {Total} old rows.", result.Total);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "The maintenance run failed.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // the application is stopping
        }
    }

    public static async Task<MaintenanceResult> RunAsync(KkDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var month = now.AddDays(-30);
        var day = now.AddDays(-1);
        var year = now.AddDays(-365);

        var sessions = await db.UserSession.Where(s => s.ExpiresDate < month || (s.RevokedDate != null && s.RevokedDate < month)).ExecuteDeleteAsync(ct);
        var codes = await db.LoginCode.Where(c => c.ExpiresDate < day || (c.UsedDate != null && c.UsedDate < day)).ExecuteDeleteAsync(ct);
        var activity = await db.ActivityLog.Where(a => a.OccurredDate < year).ExecuteDeleteAsync(ct);
        var mails = await db.MailLog.Where(m => m.SentDate < year).ExecuteDeleteAsync(ct);
        var runs = await db.BackupRun.Where(r => r.EndDate < year).ExecuteDeleteAsync(ct);
        var accesses = await db.ShareAccess.Where(a => a.AccessDate < year).ExecuteDeleteAsync(ct);
        return new MaintenanceResult(sessions, codes, activity, mails, runs, accesses);
    }
}
