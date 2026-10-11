using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.TextBlocks;

public sealed class EditModel(KkDbContext db) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib dem Textbaustein einen Namen.")]
        [Display(Name = "Name")]
        public string Name { get; set; } = "";

        [Display(Name = "Art")]
        public TextBlockKind Kind { get; set; } = TextBlockKind.Intro;

        [Display(Name = "Für")]
        public DocType? DocType { get; set; }

        [Required(ErrorMessage = "Bitte schreibe den Text.")]
        [Display(Name = "Text")]
        public string Text { get; set; } = "";

        public bool IsDefault { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<(string Name, string Description)> Placeholders => TextPlaceholders.Known;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var t = await db.TextBlock.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t is null)
            {
                return NotFound();
            }

            Input = new InputModel { Name = t.Name, Kind = t.Kind, DocType = t.DocType, Text = t.Text, IsDefault = t.IsDefault };
        }

        Prepare();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Prepare();
            return Page();
        }

        TextBlock block;
        if (Id is { } id)
        {
            block = await db.TextBlock.FirstOrDefaultAsync(t => t.Id == id) ?? throw new InvalidOperationException("Der Textbaustein existiert nicht mehr.");
        }
        else
        {
            block = new TextBlock();
            db.TextBlock.Add(block);
        }

        block.Name = Input.Name.Trim();
        block.Kind = Input.Kind;
        block.DocType = Input.DocType;
        block.Text = Input.Text.Replace("\r\n", "\n").Trim();
        block.IsDefault = Input.IsDefault;
        if (block.IsDefault)
        {
            // Only one default per kind and document type: the one that new documents start with.
            foreach (var other in await db.TextBlock.Where(t => t.Id != block.Id && t.IsDefault && t.Kind == block.Kind && t.DocType == block.DocType).ToListAsync())
            {
                other.IsDefault = false;
            }
        }

        await db.SaveChangesAsync();
        Flash("Der Textbaustein ist gespeichert.");
        return RedirectToPage("/TextBlocks/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var block = await db.TextBlock.FirstOrDefaultAsync(t => t.Id == Id);
        if (block is not null)
        {
            db.TextBlock.Remove(block);
            await db.SaveChangesAsync();
            Flash("Der Textbaustein ist gelöscht.");
        }

        return RedirectToPage("/TextBlocks/Index");
    }

    private void Prepare()
    {
        var title = Id is null ? "Neuer Textbaustein" : "Textbaustein bearbeiten";
        Head(title, null, ("Rechnungen", "/Invoices"), ("Textbausteine", "/TextBlocks"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/TextBlocks";
    }
}
