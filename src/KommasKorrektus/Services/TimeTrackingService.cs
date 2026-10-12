using KommasKorrektus.Configuration;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Services;

/// <summary>
/// The working time of the editor, made from what the editor reports while a document is open: how many seconds were active, how many keys were pressed,
/// how many words were corrected (the server knows that from the changes it saved, the browser is not asked). A block of time ends when there is no report
/// for longer than the idle limit (a long pause); the next report opens a new block. Nothing is measured while the editor is open and nobody does anything.
/// </summary>
public sealed class TimeTrackingService(KkDbContext db, ConfigStore config, TimeProvider clock)
{
    /// <summary>The browser reports about every half minute; a report claims at most this many active seconds.</summary>
    public const int MaxSecondsPerReport = 90;

    public async Task RecordAsync(long userId, Document document, int activeSeconds, int keystrokes, DiffStats delta, CancellationToken ct = default)
    {
        activeSeconds = Math.Clamp(activeSeconds, 0, MaxSecondsPerReport);
        keystrokes = Math.Clamp(keystrokes, 0, 100_000);
        if (activeSeconds == 0 && !delta.HasChanges)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var idle = TimeSpan.FromMinutes(Math.Max(1, config.Current.Tracking.IdleMinutes));
        var entry = await db.TimeEntry
            .Where(t => t.UserId == userId && t.DocumentId == document.Id && t.Kind == TimeEntryKind.Auto && t.InvoiceLineId == null && t.EndDate >= now - idle)
            .OrderByDescending(t => t.EndDate).FirstOrDefaultAsync(ct);

        if (entry is null)
        {
            entry = new TimeEntry
            {
                ProjectId = document.ProjectId,
                DocumentId = document.Id,
                UserId = userId,
                StartDate = now.AddSeconds(-activeSeconds),
                EndDate = now,
                Kind = TimeEntryKind.Auto,
                Note = null,
            };
            db.TimeEntry.Add(entry);
        }

        entry.Seconds += activeSeconds;
        entry.EndDate = now;
        await db.SaveChangesAsync(ct);

        db.WorkActivity.Add(new WorkActivity
        {
            UserId = userId,
            ProjectId = document.ProjectId,
            DocumentId = document.Id,
            TimeEntryId = entry.Id,
            At = now,
            ActiveSeconds = activeSeconds,
            WordsAdded = delta.WordsAdded,
            WordsRemoved = delta.WordsRemoved,
            WordsChanged = delta.WordsChanged,
            Keystrokes = keystrokes,
        });
        await db.SaveChangesAsync(ct);
    }
}
