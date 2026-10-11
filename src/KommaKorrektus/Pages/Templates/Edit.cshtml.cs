using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Templates;

/// <summary>
/// The editor of a template. The form on the left sets the look; the page on the right is a real PDF of a sample invoice made from the form as it is, every
/// time something changes: what is shown is what is printed.
/// </summary>
public sealed class EditModel(KkDbContext db, FileStoreService files, BillingService billing, ConfigStore config, ActivityLogger log) : PageModelBase
{
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Bitte gib der Vorlage einen Namen.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [BindProperty]
    public bool IsDefault { get; set; }

    [BindProperty]
    public InvoiceTemplateSettings Settings { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    [BindProperty]
    public bool RemoveLogo { get; set; }

    public long? LogoFileId { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var t = await db.InvoiceTemplate.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t is null)
            {
                return NotFound();
            }

            Name = t.Name;
            IsDefault = t.IsDefault;
            Settings = InvoiceTemplateSettings.Parse(t.SettingsJson);
            LogoFileId = t.LogoFileId;
        }
        else
        {
            Name = "Neue Vorlage";
        }

        Prepare();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Validate();
        if (!ModelState.IsValid)
        {
            LogoFileId = Id is { } existing ? (await db.InvoiceTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.Id == existing))?.LogoFileId : null;
            Prepare();
            return Page();
        }

        InvoiceTemplate template;
        if (Id is { } id)
        {
            template = await db.InvoiceTemplate.FirstOrDefaultAsync(t => t.Id == id) ?? throw new InvalidOperationException("Die Vorlage existiert nicht mehr.");
        }
        else
        {
            template = new InvoiceTemplate();
            db.InvoiceTemplate.Add(template);
        }

        Settings.AccentColor = InvoiceTemplateSettings.SafeColor(Settings.AccentColor);
        template.Name = Name.Trim();
        template.SettingsJson = Settings.ToJson();
        template.IsDefault = IsDefault;
        if (RemoveLogo)
        {
            template.LogoFileId = null;
        }

        if (LogoFile is { Length: > 0 })
        {
            var bytes = await ReadAsync(LogoFile);
            var stored = await files.SaveAsync(bytes, LogoFile.FileName, LogoContentType(bytes) ?? "application/octet-stream", FileKind.Logo);
            await db.SaveChangesAsync();
            template.LogoFileId = stored.Id;
        }

        if (IsDefault)
        {
            foreach (var other in await db.InvoiceTemplate.Where(t => t.Id != template.Id && t.IsDefault).ToListAsync())
            {
                other.IsDefault = false;
            }
        }
        else if (!await db.InvoiceTemplate.AnyAsync(t => t.Id != template.Id && t.IsDefault))
        {
            // There is always one template that new documents start with.
            template.IsDefault = true;
        }

        await db.SaveChangesAsync();
        await log.LogAsync(Id is null ? "Vorlage angelegt" : "Vorlage geändert", "InvoiceTemplate", template.Id, template.Name);
        Flash("Die Vorlage ist gespeichert.");
        return RedirectToPage("/Templates/Index");
    }

    /// <summary>The preview: a PDF of a sample invoice with the look from the form. Nothing is saved.</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        Settings.AccentColor = InvoiceTemplateSettings.SafeColor(Settings.AccentColor);
        Settings.FontSize = Math.Clamp(Settings.FontSize, 8, 13);
        byte[]? logo = null;
        if (LogoFile is { Length: > 0 and <= MaxLogoBytes })
        {
            logo = await ReadAsync(LogoFile);
        }
        else if (!RemoveLogo && Id is { } id)
        {
            var template = await db.InvoiceTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
            logo = await billing.LogoBytesAsync(template?.LogoFileId);
        }

        var pdf = InvoicePdf.Render(SampleDocument(Settings, logo));
        Response.Headers.ContentDisposition = "inline; filename=\"vorschau.pdf\"";
        return File(pdf, "application/pdf");
    }

    /// <summary>The logo of a template, for the form (sent as an image, never as a page).</summary>
    public async Task<IActionResult> OnGetLogoAsync(long id)
    {
        var template = await db.InvoiceTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        var file = template?.LogoFileId is { } fileId ? await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId) : null;
        if (file is null)
        {
            return NotFound();
        }

        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
        return File(await files.ReadAllAsync(file, HttpContext.RequestAborted), file.ContentType);
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var template = await db.InvoiceTemplate.FirstOrDefaultAsync(t => t.Id == Id);
        if (template is null)
        {
            return NotFound();
        }

        if (await db.InvoiceTemplate.CountAsync() <= 1)
        {
            FlashError("Die letzte Vorlage kann nicht gelöscht werden.");
            return RedirectToPage(new { id = Id });
        }

        db.InvoiceTemplate.Remove(template);
        if (template.IsDefault && await db.InvoiceTemplate.Where(t => t.Id != template.Id).OrderBy(t => t.Id).FirstOrDefaultAsync() is { } next)
        {
            next.IsDefault = true;
        }

        await db.SaveChangesAsync();
        Flash("Die Vorlage ist gelöscht. Ausgestellte Dokumente bleiben unverändert.");
        return RedirectToPage("/Templates/Index");
    }

    private void Validate()
    {
        if (Settings.LogoHeightMm is < 8 or > 40)
        {
            ModelState.AddModelError("Settings.LogoHeightMm", "Die Höhe des Logos liegt zwischen 8 und 40 mm.");
        }

        if (Settings.FontSize is < 8 or > 13)
        {
            ModelState.AddModelError("Settings.FontSize", "Die Schriftgröße liegt zwischen 8 und 13 Punkt.");
        }

        if (Settings.MarginMm is < 10 or > 35)
        {
            ModelState.AddModelError("Settings.MarginMm", "Der Rand liegt zwischen 10 und 35 mm.");
        }

        if (LogoFile is { Length: > 0 } file)
        {
            if (file.Length > MaxLogoBytes)
            {
                ModelState.AddModelError(nameof(LogoFile), "Das Logo ist größer als 2 MB.");
            }
            else if (LogoContentType(ReadAllSync(file)) is null)
            {
                ModelState.AddModelError(nameof(LogoFile), "Das Logo muss ein PNG, JPEG oder SVG sein.");
            }
        }
    }

    private static byte[] ReadAllSync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static string? LogoContentType(byte[] bytes)
    {
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 400)).TrimStart();
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase)) ? "image/svg+xml" : null;
    }

    /// <summary>A sample invoice for the preview: the company as set up, a made-up customer, two typical lines.</summary>
    private InvoiceDocument SampleDocument(InvoiceTemplateSettings settings, byte[]? logo)
    {
        var small = config.Current.Company.SmallBusiness;
        var vat = small ? 0m : config.Current.Company.DefaultVatRate;
        var lines = new List<DocumentLine>
        {
            new(1, "Lektorat „Die Hexe vom Rabenwald“", "Stil, Ausdruck und Logik, 75 Normseiten", 75, "Normseite", 4.50m, vat, 0, 337.50m),
            new(2, "Stilblatt und Namensliste", null, 1.5m, "Stunde", 55m, vat, 0, 82.50m),
        };
        var totals = InvoiceCalculator.Calculate(lines.Select(l => new LineInput(l.Quantity, l.UnitPrice, l.VatRate)), 0, small);
        var today = Fmt.Today(TimeProvider.System);
        return new InvoiceDocument
        {
            Kind = InvoiceKind.Invoice,
            Number = "RE-2026-0042",
            IssueDate = today,
            ServiceFrom = today.AddDays(-30),
            ServiceTo = today.AddDays(-2),
            DueDate = today.AddDays(config.Current.Company.DefaultPaymentDays),
            Subject = "Lektorat Ihres Romans",
            Intro = "Sehr geehrte Frau Beispiel,\n\nvielen Dank für die gute Zusammenarbeit. Für meine Leistungen stelle ich Ihnen folgende Rechnung:",
            Outro = "Bitte überweisen Sie den Rechnungsbetrag innerhalb von 14 Tagen unter Angabe der Rechnungsnummer.\n\nHerzlichen Dank!",
            Seller = billing.SellerParty(),
            Buyer = new Party("Anna Beispiel", null, "Musterstraße 12", "20095", "Hamburg", "DE"),
            BuyerNumber = "K-0001",
            ProjectTitle = "Die Hexe vom Rabenwald",
            Lines = lines,
            Totals = totals,
            SmallBusiness = small,
            PaymentTermDays = config.Current.Company.DefaultPaymentDays,
            Bank = billing.Bank() ?? new BankInfo("Max Beispiel", "Beispielbank", "DE89370400440532013000", "COBADEFFXXX"),
            Settings = settings,
            Logo = logo,
        };
    }

    private void Prepare()
    {
        var title = Id is null ? "Neue Vorlage" : "Vorlage bearbeiten";
        Head(title, "Links stellst du das Aussehen ein, rechts siehst du das fertige PDF.", ("Rechnungen", "/Invoices"), ("Vorlagen", "/Templates"), (title, null));
        ViewData["Width"] = "wide";
        ViewData["ActiveHref"] = "/Templates";
    }

    public static List<SelectListItem> Fonts => [new("Serifenlos (Lato)", "Sans"), new("Mit Serifen", "Serif")];

    public static List<SelectListItem> Positions => [new("Rechts", "Right"), new("Links", "Left")];

    public static List<SelectListItem> Papers => [new("DIN A4", "A4"), new("Letter", "Letter")];
}
