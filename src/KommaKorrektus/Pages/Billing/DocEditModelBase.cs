using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Billing;

/// <summary>
/// The page that writes an offer or an invoice (or the credit note that is its draft). A form on one side, the PDF as it would be printed on the other,
/// made from the form as it is. Only a draft can be edited; issuing locks it.
/// </summary>
public abstract class DocEditModelBase(KkDbContext db, BillingService billing, ConfigStore config, ActivityLogger log, TimeProvider clock) : PageModelBase
{
    public sealed class LineModel
    {
        public long? Id { get; set; }

        [Required(ErrorMessage = "Bitte gib die Bezeichnung ein.")]
        public string Title { get; set; } = "";

        public string? Description { get; set; }

        public decimal Quantity { get; set; } = 1;

        public string Unit { get; set; } = "Stk.";

        public decimal UnitPrice { get; set; }

        public decimal VatRate { get; set; } = 19;

        public decimal DiscountPercent { get; set; }
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte wähle den Kunden.")]
        [Display(Name = "Kunde")]
        public long? CustomerId { get; set; }

        [Display(Name = "Projekt")]
        public long? ProjectId { get; set; }

        [Display(Name = "Vorlage")]
        public long? TemplateId { get; set; }

        [Required(ErrorMessage = "Bitte gib das Datum ein.")]
        [Display(Name = "Datum")]
        public DateOnly? IssueDate { get; set; }

        [Display(Name = "Leistung von")]
        public DateOnly? ServiceFrom { get; set; }

        [Display(Name = "Leistung bis")]
        public DateOnly? ServiceTo { get; set; }

        [Range(0, 365, ErrorMessage = "Das Zahlungsziel liegt zwischen 0 und 365 Tagen.")]
        [Display(Name = "Zahlungsziel in Tagen")]
        public int PaymentTermDays { get; set; } = 14;

        [Display(Name = "Fällig am")]
        public DateOnly? DueDate { get; set; }

        [Display(Name = "Gültig bis")]
        public DateOnly? ValidUntil { get; set; }

        [Display(Name = "Betreff")]
        public string? Subject { get; set; }

        [Display(Name = "Einleitung")]
        public string? IntroText { get; set; }

        [Display(Name = "Schlusstext")]
        public string? OutroText { get; set; }

        [Range(0, 100, ErrorMessage = "Der Rabatt liegt zwischen 0 und 100 Prozent.")]
        [Display(Name = "Rabatt auf alles in Prozent")]
        public decimal DiscountPercent { get; set; }

        public bool SmallBusiness { get; set; }

        public bool ReverseCharge { get; set; }

        [Display(Name = "Leitweg-ID")]
        public string? BuyerReference { get; set; }

        [Display(Name = "Ihre Referenz")]
        public string? CustomerReference { get; set; }

        [Display(Name = "Interne Notiz")]
        public string? InternalNote { get; set; }

        public List<LineModel> Lines { get; set; } = [];
    }

    protected abstract DocType Type { get; }

    public bool IsOffer => Type == DocType.Offer;

    public string Base => IsOffer ? "/Offers" : "/Invoices";

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? CustomerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? ProjectId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Invoice? Draft { get; private set; }

    public string? Number { get; private set; }

    public List<SelectListItem> Customers { get; private set; } = [];

    public List<(long Id, long CustomerId, string Title)> Projects { get; private set; } = [];

    public List<SelectListItem> Templates { get; private set; } = [];

    public List<Product> Products { get; private set; } = [];

    public string Currency { get; private set; } = "EUR";

    public decimal TimeHours { get; private set; }

    public bool SmallBusinessDefault => config.Current.Company.SmallBusiness;

    public string Title => IsOffer ? "Angebot" : Type == DocType.CreditNote ? "Gutschrift" : "Rechnung";

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var invoice = await db.Invoice.AsNoTracking().Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice is null)
            {
                return NotFound();
            }

            if (invoice.Status != DocStatus.Draft)
            {
                return RedirectToPage($"{Base}/Details", new { id });
            }

            Draft = invoice;
            Type2(invoice);
            Input = ToInput(invoice);
        }
        else
        {
            Customer? customer = CustomerId is { } cid ? await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid) : null;
            if (customer is null)
            {
                Input = new InputModel { IssueDate = Fmt.Today(clock), PaymentTermDays = config.Current.Company.DefaultPaymentDays, SmallBusiness = config.Current.Company.SmallBusiness };
                var fresh = await billing.NewDraftAsync(Type, new Customer { Id = 0 }, null);
                Input.IntroText = fresh.IntroText;
                Input.OutroText = fresh.OutroText;
                Input.TemplateId = fresh.TemplateId;
            }
            else
            {
                var fresh = await billing.NewDraftAsync(Type, customer, ProjectId);
                Input = ToInput(fresh);
                if (ProjectId is { } pid && await db.Project.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid) is { } project)
                {
                    fresh.Subject = project.Title;
                    Input.Subject = project.Title;
                    Input.Lines = [.. (await billing.SuggestLinesAsync(project, customer)).Select(ToLine)];
                }
            }
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var invoice = await SaveAsync();
        if (invoice is null)
        {
            await PrepareAsync();
            return Page();
        }

        Flash("Der Entwurf ist gespeichert.");
        return RedirectToPage($"{Base}/Edit", new { id = invoice.Id });
    }

    /// <summary>Saves and issues in one go: the document gets its number and is locked.</summary>
    public async Task<IActionResult> OnPostIssueAsync()
    {
        var invoice = await SaveAsync();
        if (invoice is null)
        {
            await PrepareAsync();
            return Page();
        }

        try
        {
            await billing.IssueAsync(invoice);
        }
        catch (BillingException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Id = invoice.Id;
            await PrepareAsync();
            return Page();
        }

        await log.LogAsync($"{Title} ausgestellt", "Invoice", invoice.Id, invoice.Number);
        Flash($"{Title} {invoice.Number} ist ausgestellt.");
        return RedirectToPage($"{Base}/Details", new { id = invoice.Id });
    }

    /// <summary>Adds the working time that has not been billed yet as a line.</summary>
    public async Task<IActionResult> OnPostAddTimeAsync()
    {
        var invoice = await SaveAsync();
        if (invoice is null)
        {
            await PrepareAsync();
            return Page();
        }

        if (invoice.ProjectId is not { } projectId)
        {
            FlashError("Wähle zuerst ein Projekt.");
            return RedirectToPage($"{Base}/Edit", new { id = invoice.Id });
        }

        var customer = await db.Customer.AsNoTracking().FirstAsync(c => c.Id == invoice.CustomerId);
        if (await billing.TimeLineAsync(projectId, customer) is { } result)
        {
            result.Line.InvoiceId = invoice.Id;
            result.Line.Position = invoice.Lines.Count + 1;
            invoice.Lines.Add(result.Line);
            BillingService.Recalculate(invoice);
            await db.SaveChangesAsync();
            await billing.LinkTimeAsync(result.EntryIds, result.Line);
            Flash($"Die Arbeitszeit ist als Position übernommen ({result.Line.Quantity:0.##} Stunden).");
        }
        else
        {
            FlashError("Es gibt keine Arbeitszeit, die noch nicht abgerechnet ist.");
        }

        return RedirectToPage($"{Base}/Edit", new { id = invoice.Id });
    }

    /// <summary>Suggests the positions of the project from its price and the size of its documents.</summary>
    public async Task<IActionResult> OnPostSuggestAsync()
    {
        var invoice = await SaveAsync();
        if (invoice is null)
        {
            await PrepareAsync();
            return Page();
        }

        var project = invoice.ProjectId is { } pid ? await db.Project.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid) : null;
        if (project is null)
        {
            FlashError("Wähle zuerst ein Projekt.");
            return RedirectToPage($"{Base}/Edit", new { id = invoice.Id });
        }

        var customer = await db.Customer.AsNoTracking().FirstAsync(c => c.Id == invoice.CustomerId);
        var lines = await billing.SuggestLinesAsync(project, customer);
        if (lines.Count == 0)
        {
            FlashError("Für dieses Projekt lässt sich nichts vorschlagen: Es fehlen der Preis oder der Umfang.");
        }
        else
        {
            foreach (var line in lines)
            {
                line.InvoiceId = invoice.Id;
                line.Position = invoice.Lines.Count + 1;
                invoice.Lines.Add(line);
            }

            BillingService.Recalculate(invoice);
            await db.SaveChangesAsync();
            Flash("Die Positionen aus dem Projekt sind eingefügt.");
        }

        return RedirectToPage($"{Base}/Edit", new { id = invoice.Id });
    }

    /// <summary>The preview: a PDF made from the form as it is, not saved.</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (Input.CustomerId is not { } customerId || await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId) is not { } customer)
        {
            // 204: nothing to draw yet; the page says so next to the preview (an error status would only fill the console)
            return StatusCode(StatusCodes.Status204NoContent);
        }

        var invoice = new Invoice { Type = Type, CustomerId = customer.Id, Customer = customer, Number = Id is { } id ? await db.Invoice.AsNoTracking().Where(i => i.Id == id).Select(i => i.Number).FirstOrDefaultAsync() : null };
        Apply(invoice, Input);
        BillingService.Recalculate(invoice);
        var document = await billing.BuildDraftAsync(invoice, HttpContext.RequestAborted);
        Response.Headers.ContentDisposition = "inline; filename=\"vorschau.pdf\"";
        return File(InvoicePdf.Render(document), "application/pdf");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var invoice = await db.Invoice.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == Id && i.Status == DocStatus.Draft);
        if (invoice is null)
        {
            return NotFound();
        }

        await billing.ReleaseTimeAsync(invoice.Lines.Select(l => l.Id));
        db.Invoice.Remove(invoice);
        await db.SaveChangesAsync();
        await log.LogAsync($"{Title}sentwurf gelöscht", "Invoice", invoice.Id);
        Flash("Der Entwurf ist gelöscht.");
        return RedirectToPage($"{Base}/Index");
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private void Type2(Invoice invoice) => Number = invoice.Number;

    private async Task<Invoice?> SaveAsync()
    {
        Validate();
        if (!ModelState.IsValid)
        {
            return null;
        }

        Invoice invoice;
        if (Id is { } id)
        {
            invoice = await db.Invoice.Include(i => i.Lines).FirstOrDefaultAsync(i => i.Id == id && i.Status == DocStatus.Draft) ?? throw new InvalidOperationException("Der Entwurf existiert nicht mehr oder ist schon ausgestellt.");
        }
        else
        {
            invoice = new Invoice { Type = Type, Status = DocStatus.Draft };
            db.Invoice.Add(invoice);
        }

        invoice.CustomerId = Input.CustomerId!.Value;
        var removed = invoice.Lines.Where(l => l.Id != 0 && Input.Lines.All(x => x.Id != l.Id)).ToList();
        await billing.ReleaseTimeAsync(removed.Select(l => l.Id));
        Apply(invoice, Input);
        foreach (var gone in removed)
        {
            db.InvoiceLine.Remove(gone);
        }

        BillingService.Recalculate(invoice);
        await db.SaveChangesAsync();
        Id = invoice.Id;
        return invoice;
    }

    /// <summary>Puts the form into the entity: the lines are matched by id, new ones are added, the ones that are not in the form are left to the caller to remove.</summary>
    private static void Apply(Invoice invoice, InputModel input)
    {
        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        invoice.ProjectId = input.ProjectId;
        invoice.TemplateId = input.TemplateId;
        invoice.IssueDate = input.IssueDate ?? invoice.IssueDate;
        invoice.ServiceFrom = input.ServiceFrom;
        invoice.ServiceTo = input.ServiceTo;
        invoice.PaymentTermDays = input.PaymentTermDays;
        invoice.DueDate = invoice.Type == DocType.Invoice ? input.DueDate ?? invoice.IssueDate.AddDays(input.PaymentTermDays) : null;
        invoice.ValidUntil = invoice.Type == DocType.Offer ? input.ValidUntil : null;
        invoice.Subject = Clean(input.Subject);
        invoice.IntroText = Clean(input.IntroText);
        invoice.OutroText = Clean(input.OutroText);
        invoice.DiscountPercent = input.DiscountPercent;
        invoice.SmallBusiness = input.SmallBusiness;
        invoice.ReverseCharge = input.ReverseCharge && !input.SmallBusiness;
        invoice.BuyerReference = Clean(input.BuyerReference);
        invoice.CustomerReference = Clean(input.CustomerReference);
        invoice.InternalNote = Clean(input.InternalNote);

        var position = 0;
        foreach (var line in input.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Title) || l.UnitPrice != 0))
        {
            position++;
            var entity = line.Id is { } lineId ? invoice.Lines.FirstOrDefault(l => l.Id == lineId) : null;
            if (entity is null)
            {
                entity = new InvoiceLine();
                invoice.Lines.Add(entity);
            }

            entity.Position = position;
            entity.Title = (line.Title ?? string.Empty).Trim();
            entity.Description = Clean(line.Description);
            entity.Quantity = line.Quantity;
            entity.Unit = string.IsNullOrWhiteSpace(line.Unit) ? "Stk." : line.Unit.Trim();
            entity.UnitPrice = line.UnitPrice;
            entity.VatRate = input.SmallBusiness ? 0 : line.VatRate;
            entity.DiscountPercent = line.DiscountPercent;
        }
    }

    private void Validate()
    {
        for (var i = 0; i < Input.Lines.Count; i++)
        {
            var line = Input.Lines[i];
            if (string.IsNullOrWhiteSpace(line.Title) && line.UnitPrice == 0)
            {
                ModelState.Remove($"Input.Lines[{i}].Title");
                continue;
            }

            if (line.Quantity == 0)
            {
                ModelState.AddModelError($"Input.Lines[{i}].Quantity", $"Position {i + 1}: Die Menge darf nicht null sein.");
            }

            if (line.VatRate is < 0 or > 30)
            {
                ModelState.AddModelError($"Input.Lines[{i}].VatRate", $"Position {i + 1}: Der Steuersatz liegt zwischen 0 und 30 Prozent.");
            }

            if (line.DiscountPercent is < 0 or > 100)
            {
                ModelState.AddModelError($"Input.Lines[{i}].DiscountPercent", $"Position {i + 1}: Der Rabatt liegt zwischen 0 und 100 Prozent.");
            }
        }

        if (Input.ServiceFrom is { } from && Input.ServiceTo is { } to && to < from)
        {
            ModelState.AddModelError("Input.ServiceTo", "Das Ende der Leistung liegt vor dem Beginn.");
        }
    }

    private static InputModel ToInput(Invoice i) => new()
    {
        CustomerId = i.CustomerId == 0 ? null : i.CustomerId,
        ProjectId = i.ProjectId,
        TemplateId = i.TemplateId,
        IssueDate = i.IssueDate == default ? null : i.IssueDate,
        ServiceFrom = i.ServiceFrom,
        ServiceTo = i.ServiceTo,
        PaymentTermDays = i.PaymentTermDays,
        DueDate = i.DueDate,
        ValidUntil = i.ValidUntil,
        Subject = i.Subject,
        IntroText = i.IntroText,
        OutroText = i.OutroText,
        DiscountPercent = i.DiscountPercent,
        SmallBusiness = i.SmallBusiness,
        ReverseCharge = i.ReverseCharge,
        BuyerReference = i.BuyerReference,
        CustomerReference = i.CustomerReference,
        InternalNote = i.InternalNote,
        Lines = [.. i.Lines.OrderBy(l => l.Position).Select(ToLine)],
    };

    private static LineModel ToLine(InvoiceLine l) => new()
    {
        Id = l.Id == 0 ? null : l.Id, Title = l.Title, Description = l.Description, Quantity = l.Quantity, Unit = l.Unit, UnitPrice = l.UnitPrice, VatRate = l.VatRate, DiscountPercent = l.DiscountPercent,
    };

    private async Task PrepareAsync()
    {
        var title = Id is null ? (IsOffer ? "Neues Angebot" : "Neue Rechnung") : $"{Title}sentwurf bearbeiten";
        Head(title, null, ("Rechnungen", "/Invoices"), (IsOffer ? "Angebote" : "Rechnungen", Base), (title, null));
        ViewData["Width"] = "wide";
        ViewData["ActiveHref"] = Base;
        Currency = config.Current.Company.Currency;

        var currentCustomer = Input.CustomerId;
        Customers = (await db.Customer.AsNoTracking().Where(c => !c.IsArchived || c.Id == currentCustomer).ToListAsync())
            .OrderBy(c => c.DisplayName).Select(c => new SelectListItem($"{c.DisplayName} ({c.Number})", c.Id.ToString())).ToList();
        Projects = (await db.Project.AsNoTracking().Where(p => p.Status != ProjectStatus.Archived && p.Status != ProjectStatus.Cancelled || p.Id == Input.ProjectId).OrderBy(p => p.Title).Select(p => new { p.Id, p.CustomerId, p.Title }).ToListAsync())
            .Select(p => (p.Id, p.CustomerId, p.Title)).ToList();
        Templates = (await db.InvoiceTemplate.AsNoTracking().OrderBy(t => t.Name).ToListAsync()).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToList();
        Products = await db.Product.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync();

        if (Input.ProjectId is { } pid)
        {
            var seconds = await db.TimeEntry.AsNoTracking().Where(t => t.ProjectId == pid && t.InvoiceLineId == null && t.IsBillable && !t.IsRunning).SumAsync(t => (int?)t.Seconds) ?? 0;
            TimeHours = seconds / 3600m;
        }

        if (Id is { } id && Number is null)
        {
            Number = await db.Invoice.AsNoTracking().Where(i => i.Id == id).Select(i => i.Number).FirstOrDefaultAsync();
        }
    }
}
