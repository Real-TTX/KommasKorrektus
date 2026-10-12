using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.TaskTemplates;

/// <summary>A task template. The tasks are typed one per line ("Titel; Minuten; Tage nach Beginn"): faster than a form per task.</summary>
public sealed class EditModel(KkDbContext db) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib der Vorlage einen Namen.")]
        [Display(Name = "Name")]
        public string Name { get; set; } = "";

        [Display(Name = "Beschreibung")]
        public string? Description { get; set; }

        [Display(Name = "Leistung")]
        public ServiceType? ServiceType { get; set; }

        [Display(Name = "Aufgaben")]
        public string Items { get; set; } = "";
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var t = await db.TaskTemplate.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
            if (t is null)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                Name = t.Name, Description = t.Description, ServiceType = t.ServiceType,
                Items = string.Join('\n', t.Items.OrderBy(i => i.SortOrder).Select(i => $"{i.Title}; {i.EstimatedMinutes}; {i.DueOffsetDays}".TrimEnd(' ', ';'))),
            };
        }

        Prepare();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var lines = ParseItems(Input.Items, out var problem);
        if (problem is not null)
        {
            ModelState.AddModelError("Input.Items", problem);
        }

        if (!ModelState.IsValid)
        {
            Prepare();
            return Page();
        }

        TaskTemplate template;
        if (Id is { } id)
        {
            template = await db.TaskTemplate.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id) ?? throw new InvalidOperationException("Die Vorlage existiert nicht mehr.");
            db.TaskTemplateItem.RemoveRange(template.Items);
            template.Items.Clear();
        }
        else
        {
            template = new TaskTemplate();
            db.TaskTemplate.Add(template);
        }

        template.Name = Input.Name.Trim();
        template.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        template.ServiceType = Input.ServiceType;
        var order = 0;
        foreach (var (title, minutes, days) in lines)
        {
            template.Items.Add(new TaskTemplateItem { Title = title, EstimatedMinutes = minutes, DueOffsetDays = days, SortOrder = ++order });
        }

        await db.SaveChangesAsync();
        Flash("Die Vorlage ist gespeichert.");
        return RedirectToPage("/TaskTemplates/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var template = await db.TaskTemplate.FirstOrDefaultAsync(t => t.Id == Id);
        if (template is not null)
        {
            db.TaskTemplate.Remove(template);
            await db.SaveChangesAsync();
            Flash("Die Vorlage ist gelöscht.");
        }

        return RedirectToPage("/TaskTemplates/Index");
    }

    /// <summary>One task per line: <c>Titel; Minuten; Tage nach Beginn</c>. The numbers are optional. Empty lines are skipped.</summary>
    public static List<(string Title, int? Minutes, int? Days)> ParseItems(string? text, out string? problem)
    {
        problem = null;
        var result = new List<(string, int?, int?)>();
        var number = 0;
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(';', StringSplitOptions.TrimEntries);
            int? minutes = null, days = null;
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                if (!int.TryParse(parts[1], out var m) || m < 0)
                {
                    problem = $"Zeile {number}: Die Minuten müssen eine Zahl sein.";
                    return result;
                }

                minutes = m;
            }

            if (parts.Length > 2 && parts[2].Length > 0)
            {
                if (!int.TryParse(parts[2], out var d) || d < 0)
                {
                    problem = $"Zeile {number}: Die Tage müssen eine Zahl sein.";
                    return result;
                }

                days = d;
            }

            if (parts[0].Length == 0)
            {
                problem = $"Zeile {number}: Es fehlt der Titel der Aufgabe.";
                return result;
            }

            result.Add((parts[0], minutes, days));
        }

        return result;
    }

    private void Prepare()
    {
        var title = Id is null ? "Neue Aufgabenvorlage" : "Aufgabenvorlage bearbeiten";
        Head(title, null, ("Projekte", "/Projects"), ("Aufgabenvorlagen", "/TaskTemplates"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/TaskTemplates";
    }
}
