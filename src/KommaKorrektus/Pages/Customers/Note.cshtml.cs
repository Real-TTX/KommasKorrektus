using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Customers;

/// <summary>A note in the history of a customer: a call, a mail, a talk. What the program does by itself is written too, but cannot be edited.</summary>
public sealed class NoteModel(KkDbContext db, TimeProvider clock) : PageModelBase
{
    public sealed class InputModel
    {
        [Display(Name = "Art")]
        public HistoryKind Kind { get; set; } = HistoryKind.Note;

        [Required(ErrorMessage = "Bitte gib eine Überschrift ein.")]
        [Display(Name = "Überschrift")]
        public string Title { get; set; } = "";

        [Display(Name = "Text")]
        public string? Text { get; set; }

        [Display(Name = "Datum")]
        public DateOnly Date { get; set; }

        [Display(Name = "Projekt")]
        public long? ProjectId { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long CustomerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Customer Customer { get; private set; } = null!;

    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Projects { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (Id is { } id)
        {
            var entry = await db.HistoryEntry.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id && h.CustomerId == CustomerId && h.Kind != HistoryKind.System);
            if (entry is null)
            {
                return NotFound();
            }

            Input = new InputModel { Kind = entry.Kind, Title = entry.Title, Text = entry.Text, ProjectId = entry.ProjectId };
            Input.Date = DateOnly.FromDateTime(Fmt.Local(entry.OccurredDate));
        }
        else
        {
            Input.Date = Fmt.Today(clock);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        HistoryEntry entry;
        if (Id is { } id)
        {
            entry = await db.HistoryEntry.FirstOrDefaultAsync(h => h.Id == id && h.CustomerId == CustomerId && h.Kind != HistoryKind.System) ?? throw new InvalidOperationException("Die Notiz existiert nicht mehr.");
        }
        else
        {
            entry = new HistoryEntry { CustomerId = CustomerId };
            db.HistoryEntry.Add(entry);
        }

        entry.Kind = Input.Kind == HistoryKind.System ? HistoryKind.Note : Input.Kind;
        entry.Title = Input.Title.Trim();
        entry.Text = string.IsNullOrWhiteSpace(Input.Text) ? null : Input.Text.Trim();
        entry.ProjectId = Input.ProjectId;
        // The time of day stays when a note is edited; a new one gets the time of now on the chosen day.
        var time = Id is null ? TimeOnly.FromDateTime(Fmt.Local(clock.GetUtcNow().UtcDateTime)) : TimeOnly.FromDateTime(Fmt.Local(entry.OccurredDate));
        entry.OccurredDate = Fmt.UtcFromLocal(Input.Date.ToDateTime(time));
        await db.SaveChangesAsync();
        Flash("Die Notiz ist gespeichert.");
        return RedirectToPage("/Customers/Details", new { id = CustomerId, tab = "history" });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var entry = await db.HistoryEntry.FirstOrDefaultAsync(h => h.Id == Id && h.CustomerId == CustomerId && h.Kind != HistoryKind.System);
        if (entry is not null)
        {
            db.HistoryEntry.Remove(entry);
            await db.SaveChangesAsync();
            Flash("Die Notiz ist gelöscht.");
        }

        return RedirectToPage("/Customers/Details", new { id = CustomerId, tab = "history" });
    }

    private async Task<bool> PrepareAsync()
    {
        var customer = await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
        {
            return false;
        }

        Customer = customer;
        var title = Id is null ? "Neue Notiz" : "Notiz bearbeiten";
        Head(title, null, ("Kunden", "/Customers"), (customer.DisplayName, $"/Customers/Details?id={customer.Id}&tab=history"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Customers";
        Projects = await db.Project.AsNoTracking().Where(p => p.CustomerId == CustomerId).OrderBy(p => p.Title)
            .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(p.Title, p.Id.ToString())).ToListAsync();
        return true;
    }
}
