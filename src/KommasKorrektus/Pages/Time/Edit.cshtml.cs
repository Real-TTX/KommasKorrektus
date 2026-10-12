using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Time;

/// <summary>A time typed in afterwards, or the correction of one that was measured. Billed time cannot be changed: it is on an invoice.</summary>
public sealed class EditModel(KkDbContext db, ICurrentUser current, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte wähle das Projekt.")]
        [Display(Name = "Projekt")]
        public long? ProjectId { get; set; }

        [Required(ErrorMessage = "Bitte gib den Tag ein.")]
        [Display(Name = "Tag")]
        public DateOnly? Day { get; set; }

        [Required(ErrorMessage = "Bitte gib die Dauer an.")]
        [Display(Name = "Dauer")]
        public string? Duration { get; set; }

        [Display(Name = "Beginn (Uhrzeit)")]
        public string? StartTime { get; set; }

        [Display(Name = "Notiz")]
        public string? Note { get; set; }

        public bool IsBillable { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ProjectId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool Billed { get; private set; }

    public List<SelectListItem> Projects { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var e = await db.TimeEntry.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            if (e is null)
            {
                return NotFound();
            }

            var local = Fmt.Local(e.StartDate);
            Input = new InputModel
            {
                ProjectId = e.ProjectId, Day = DateOnly.FromDateTime(local), Duration = FormatDuration(e.Seconds), StartTime = local.ToString("HH:mm"),
                Note = e.Note, IsBillable = e.IsBillable,
            };
            Billed = e.InvoiceLineId is not null;
        }
        else
        {
            Input.ProjectId = ProjectId;
            Input.Day = Fmt.Today(clock);
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var seconds = ParseDuration(Input.Duration);
        if (Input.Duration is not null && seconds is null)
        {
            ModelState.AddModelError("Input.Duration", "Schreibe die Dauer zum Beispiel als 1:30, 90 Minuten oder 1,5 Stunden.");
        }

        TimeOnly start = new(9, 0);
        if (!string.IsNullOrWhiteSpace(Input.StartTime) && !TimeOnly.TryParse(Input.StartTime, out start))
        {
            ModelState.AddModelError("Input.StartTime", "Bitte gib die Uhrzeit so ein: 09:30.");
        }

        TimeEntry? existing = null;
        if (Id is { } id)
        {
            existing = await db.TimeEntry.FirstOrDefaultAsync(t => t.Id == id);
            if (existing is null)
            {
                return NotFound();
            }

            if (existing.InvoiceLineId is not null)
            {
                ModelState.AddModelError(string.Empty, "Diese Zeit ist schon abgerechnet und kann nicht mehr geändert werden.");
            }
        }

        if (!ModelState.IsValid)
        {
            Billed = existing?.InvoiceLineId is not null;
            await PrepareAsync();
            return Page();
        }

        var entry = existing ?? new TimeEntry { UserId = current.UserId!.Value, Kind = TimeEntryKind.Manual };
        if (existing is null)
        {
            db.TimeEntry.Add(entry);
        }

        var startUtc = Fmt.UtcFromLocal(Input.Day!.Value.ToDateTime(start));
        entry.ProjectId = Input.ProjectId!.Value;
        entry.StartDate = startUtc;
        entry.Seconds = seconds!.Value;
        entry.EndDate = startUtc.AddSeconds(seconds.Value);
        entry.Note = string.IsNullOrWhiteSpace(Input.Note) ? null : Input.Note.Trim();
        entry.IsBillable = Input.IsBillable;
        await db.SaveChangesAsync();
        await log.LogAsync(existing is null ? "Zeit eingetragen" : "Zeit geändert", "TimeEntry", entry.Id, Fmt.Duration(entry.Seconds));
        Flash("Die Zeit ist gespeichert.");
        return RedirectToPage("/Time/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var entry = await db.TimeEntry.FirstOrDefaultAsync(t => t.Id == Id);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.InvoiceLineId is not null)
        {
            FlashError("Diese Zeit ist schon abgerechnet und kann nicht gelöscht werden.");
            return RedirectToPage(new { id = entry.Id });
        }

        db.TimeEntry.Remove(entry);
        await db.SaveChangesAsync();
        Flash("Die Zeit ist gelöscht.");
        return RedirectToPage("/Time/Index");
    }

    /// <summary>"1:30", "90", "90 min", "1,5 h", "1.5 Stunden": the ways people write a duration. Minutes are the default for a bare number above 12, hours below.</summary>
    public static int? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim().ToLowerInvariant().Replace(',', '.');
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length is 2 or 3 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) && h >= 0 && m is >= 0 and < 60)
            {
                var s = parts.Length == 3 && int.TryParse(parts[2], out var sec) ? sec : 0;
                return (h * 3600) + (m * 60) + s;
            }

            return null;
        }

        var unit = t.EndsWith("min") || t.EndsWith("m") ? "m" : t.EndsWith("std") || t.EndsWith("h") || t.EndsWith("stunden") || t.EndsWith("stunde") ? "h" : "";
        var number = new string(t.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        if (!double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0 || value > 24 * 60)
        {
            return null;
        }

        var minutes = unit == "h" || (unit == "" && value <= 12) ? value * 60 : value;
        return (int)Math.Round(minutes * 60);
    }

    private static string FormatDuration(int seconds) => $"{seconds / 3600}:{seconds % 3600 / 60:00}";

    private async Task PrepareAsync()
    {
        var title = Id is null ? "Zeit eintragen" : "Zeit bearbeiten";
        Head(title, null, ("Projekte", "/Projects"), ("Zeiterfassung", "/Time"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Time";
        var current = Input.ProjectId;
        Projects = (await db.Project.AsNoTracking().Where(p => (p.Status != ProjectStatus.Archived && p.Status != ProjectStatus.Cancelled) || p.Id == current).OrderBy(p => p.Title).ToListAsync())
            .Select(p => new SelectListItem(p.Title, p.Id.ToString())).ToList();
    }
}
