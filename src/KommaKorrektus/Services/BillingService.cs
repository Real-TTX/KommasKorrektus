using System.Text.Json;
using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Core.Writing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Storage;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services;

public sealed class BillingException(string message) : Exception(message);

/// <summary>What an issued document looked like when it was issued: sender, recipient, look. It is stored with the document, so a changed address or logo later never changes an invoice that went out.</summary>
public sealed class IssuedSnapshot
{
    public Party Seller { get; set; } = null!;

    public Party Buyer { get; set; } = null!;

    public string? BuyerNumber { get; set; }

    public string? ProjectTitle { get; set; }

    public BankInfo? Bank { get; set; }

    public InvoiceTemplateSettings Settings { get; set; } = new();

    public long? LogoFileId { get; set; }

    public string Intro { get; set; } = "";

    public string Outro { get; set; } = "";
}

/// <summary>
/// Offers, invoices and credit notes: the draft (anything can change), the issue (a number, a locked document, a PDF that is kept), payments, the
/// cancellation by a credit note and the invoice made from an offer. The rules are the ones of a bookkeeping that has to hold up: numbers are given only
/// when a document is issued (so they have no gaps), an issued document is never changed, a mistake is corrected by a credit note.
/// </summary>
public sealed class BillingService(KkDbContext db, ConfigStore config, NumberService numbers, FileStoreService files, ProjectService projects, TimeProvider clock)
{
    public static InvoiceKind KindOf(DocType type) => type switch
    {
        DocType.Offer => InvoiceKind.Offer,
        DocType.CreditNote => InvoiceKind.CreditNote,
        _ => InvoiceKind.Invoice,
    };

    public static NumberKind NumberKindOf(DocType type) => type switch
    {
        DocType.Offer => NumberKind.Offer,
        DocType.CreditNote => NumberKind.CreditNote,
        _ => NumberKind.Invoice,
    };

    // ------------------------------------------------------------------------------------------------ the data of a document

    public Party SellerParty()
    {
        var c = config.Current.Company;
        return new Party(c.Name, string.IsNullOrWhiteSpace(c.Owner) ? null : c.Owner, c.Street, c.PostalCode, c.City, string.IsNullOrWhiteSpace(c.Country) ? "DE" : c.Country,
            c.Email, c.Phone, c.SmallBusiness ? null : EmptyToNull(c.VatId), EmptyToNull(c.TaxNumber), EmptyToNull(c.Website));
    }

    public BankInfo? Bank()
    {
        var c = config.Current.Company;
        return string.IsNullOrWhiteSpace(c.Iban) ? null : new BankInfo(string.IsNullOrWhiteSpace(c.AccountHolder) ? c.Name : c.AccountHolder, c.BankName, c.Iban, c.Bic);
    }

    public static Party BuyerParty(Customer c) => new(
        c.DisplayName,
        c.Kind == CustomerKind.Company && c.PersonName.Length > 0 ? $"{(string.IsNullOrWhiteSpace(c.Salutation) ? "" : c.Salutation + " ")}{c.PersonName}".Trim() : null,
        c.Street ?? "", c.PostalCode ?? "", c.City ?? "", string.IsNullOrWhiteSpace(c.Country) ? "DE" : c.Country, EmptyToNull(c.Email), EmptyToNull(c.Phone), EmptyToNull(c.VatId));

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<byte[]?> LogoBytesAsync(long? fileId, CancellationToken ct = default)
    {
        if (fileId is null)
        {
            return null;
        }

        var file = await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null)
        {
            return null;
        }

        try
        {
            return await files.ReadAllAsync(file, ct);
        }
        catch (StorageException)
        {
            return null;
        }
    }

    public static InvoiceTotals Totals(Invoice invoice)
        => InvoiceCalculator.Calculate(invoice.Lines.OrderBy(l => l.Position).Select(l => new LineInput(l.Quantity, l.UnitPrice, l.VatRate, l.DiscountPercent)), invoice.DiscountPercent, invoice.SmallBusiness || invoice.ReverseCharge);

    /// <summary>Works out the amounts of the lines and of the document and writes them into the rows.</summary>
    public static void Recalculate(Invoice invoice)
    {
        var totals = Totals(invoice);
        var lines = invoice.Lines.OrderBy(l => l.Position).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Position = i + 1;
            lines[i].NetAmount = totals.LineNets[i];
        }

        invoice.NetTotal = totals.Net;
        invoice.VatTotal = totals.Vat;
        invoice.GrossTotal = totals.Gross;
    }

    public Dictionary<string, string> PlaceholderValues(Invoice invoice, Customer customer, string? projectTitle, string? number)
    {
        var date = invoice.IssueDate;
        var due = invoice.DueDate ?? date.AddDays(invoice.PaymentTermDays);
        return new Dictionary<string, string>
        {
            ["Anrede"] = Salutations.Letter(customer.Salutation, customer.FirstName, customer.LastName, customer.Kind == CustomerKind.Company),
            ["Kunde"] = customer.DisplayName,
            ["Projekt"] = projectTitle ?? "",
            ["Nummer"] = number ?? invoice.Number ?? "(Nummer folgt)",
            ["Datum"] = GermanFormat.Date(date),
            ["Betrag"] = GermanFormat.Money(invoice.GrossTotal, invoice.Currency),
            ["Zahlungsziel"] = GermanFormat.Date(due),
            ["Tage"] = invoice.PaymentTermDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Gueltig"] = GermanFormat.Date(invoice.ValidUntil ?? date.AddDays(config.Current.Company.OfferValidDays)),
            ["Bezug"] = invoice.SourceDocumentId is { } id ? db.Invoice.Where(i => i.Id == id).Select(i => i.Number).FirstOrDefault() ?? "" : "",
        };
    }

    /// <summary>The printable form of a draft, as it would look if it were issued now (with the current company data, customer and template).</summary>
    public async Task<InvoiceDocument> BuildDraftAsync(Invoice invoice, CancellationToken ct = default)
    {
        var customer = await db.Customer.AsNoTracking().FirstAsync(c => c.Id == invoice.CustomerId, ct);
        var template = await TemplateAsync(invoice.TemplateId, ct);
        var projectTitle = invoice.ProjectId is { } pid ? await db.Project.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Title).FirstOrDefaultAsync(ct) : null;
        var values = PlaceholderValues(invoice, customer, projectTitle, invoice.Number);
        var logo = await LogoBytesAsync(template.LogoFileId, ct);
        return Compose(invoice, customer, projectTitle, template, logo, values);
    }

    public async Task<InvoiceTemplate> TemplateAsync(long? templateId, CancellationToken ct = default)
    {
        var template = templateId is { } id ? await db.InvoiceTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct) : null;
        template ??= await db.InvoiceTemplate.AsNoTracking().OrderByDescending(t => t.IsDefault).ThenBy(t => t.Id).FirstOrDefaultAsync(ct);
        return template ?? new InvoiceTemplate { Name = "Standard", SettingsJson = InvoiceTemplateSettings.Default.ToJson() };
    }

    private InvoiceDocument Compose(Invoice invoice, Customer customer, string? projectTitle, InvoiceTemplate template, byte[]? logo, IReadOnlyDictionary<string, string> values)
    {
        var lines = invoice.Lines.OrderBy(l => l.Position).Select((l, i) => new DocumentLine(i + 1, l.Title, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate, l.DiscountPercent,
            InvoiceCalculator.LineNet(new LineInput(l.Quantity, l.UnitPrice, l.VatRate, l.DiscountPercent)))).ToList();
        var totals = Totals(invoice);
        return new InvoiceDocument
        {
            Kind = KindOf(invoice.Type),
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            ServiceFrom = invoice.ServiceFrom,
            ServiceTo = invoice.ServiceTo,
            DueDate = invoice.Type == DocType.Invoice ? invoice.DueDate ?? invoice.IssueDate.AddDays(invoice.PaymentTermDays) : null,
            ValidUntil = invoice.Type == DocType.Offer ? invoice.ValidUntil ?? invoice.IssueDate.AddDays(config.Current.Company.OfferValidDays) : null,
            Subject = invoice.Subject,
            Intro = TextPlaceholders.Resolve(invoice.IntroText, values),
            Outro = TextPlaceholders.Resolve(invoice.OutroText, values),
            Currency = invoice.Currency,
            Seller = SellerParty() with { VatId = invoice.SmallBusiness ? null : SellerParty().VatId },
            Buyer = BuyerParty(customer),
            BuyerNumber = customer.Number,
            BuyerReference = EmptyToNull(invoice.BuyerReference),
            CustomerReference = EmptyToNull(invoice.CustomerReference),
            RelatedNumber = values.GetValueOrDefault("Bezug") is { Length: > 0 } related ? related : null,
            ProjectTitle = projectTitle,
            Lines = lines,
            DiscountPercent = invoice.DiscountPercent,
            Totals = totals,
            SmallBusiness = invoice.SmallBusiness,
            ReverseCharge = invoice.ReverseCharge,
            PaymentTermDays = invoice.PaymentTermDays,
            Bank = Bank(),
            Settings = InvoiceTemplateSettings.Parse(template.SettingsJson),
            Logo = logo,
        };
    }

    /// <summary>The document as it was issued, from the snapshot kept with it.</summary>
    public InvoiceDocument FromSnapshot(Invoice invoice, byte[]? logo)
    {
        var snapshot = JsonSerializer.Deserialize<IssuedSnapshot>(invoice.SnapshotJson ?? throw new BillingException("Das Dokument ist nicht ausgestellt."))!;
        var lines = invoice.Lines.OrderBy(l => l.Position).Select(l => new DocumentLine(l.Position, l.Title, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate, l.DiscountPercent, l.NetAmount)).ToList();
        return new InvoiceDocument
        {
            Kind = KindOf(invoice.Type),
            Number = invoice.Number,
            IssueDate = invoice.IssueDate,
            ServiceFrom = invoice.ServiceFrom,
            ServiceTo = invoice.ServiceTo,
            DueDate = invoice.DueDate,
            ValidUntil = invoice.ValidUntil,
            Subject = invoice.Subject,
            Intro = snapshot.Intro,
            Outro = snapshot.Outro,
            Currency = invoice.Currency,
            Seller = snapshot.Seller,
            Buyer = snapshot.Buyer,
            BuyerNumber = snapshot.BuyerNumber,
            BuyerReference = EmptyToNull(invoice.BuyerReference),
            CustomerReference = EmptyToNull(invoice.CustomerReference),
            RelatedNumber = invoice.SourceDocumentId is { } id ? db.Invoice.Where(i => i.Id == id).Select(i => i.Number).FirstOrDefault() : null,
            ProjectTitle = snapshot.ProjectTitle,
            Lines = lines,
            DiscountPercent = invoice.DiscountPercent,
            Totals = Totals(invoice),
            SmallBusiness = invoice.SmallBusiness,
            ReverseCharge = invoice.ReverseCharge,
            PaymentTermDays = invoice.PaymentTermDays,
            Bank = snapshot.Bank,
            Settings = snapshot.Settings,
            Logo = logo,
        };
    }

    public async Task<InvoiceDocument> BuildIssuedAsync(Invoice invoice, CancellationToken ct = default)
    {
        var snapshot = JsonSerializer.Deserialize<IssuedSnapshot>(invoice.SnapshotJson ?? "{}");
        return FromSnapshot(invoice, await LogoBytesAsync(snapshot?.LogoFileId, ct));
    }

    // ------------------------------------------------------------------------------------------------ drafts

    /// <summary>A new draft with the defaults of the company and the customer.</summary>
    public async Task<Invoice> NewDraftAsync(DocType type, Customer customer, long? projectId, CancellationToken ct = default)
    {
        var company = config.Current.Company;
        var today = Fmt.Today(clock);
        var template = await TemplateAsync(null, ct);
        var intro = await DefaultTextAsync(TextBlockKind.Intro, type, ct);
        var outro = await DefaultTextAsync(TextBlockKind.Outro, type, ct);
        var days = customer.PaymentTermDays ?? company.DefaultPaymentDays;
        return new Invoice
        {
            Type = type,
            CustomerId = customer.Id,
            Customer = customer,
            ProjectId = projectId,
            TemplateId = template.Id == 0 ? null : template.Id,
            IssueDate = today,
            PaymentTermDays = days,
            DueDate = type == DocType.Invoice ? today.AddDays(days) : null,
            ValidUntil = type == DocType.Offer ? today.AddDays(company.OfferValidDays) : null,
            Currency = string.IsNullOrWhiteSpace(company.Currency) ? "EUR" : company.Currency,
            SmallBusiness = company.SmallBusiness,
            BuyerReference = customer.BuyerReference,
            IntroText = intro,
            OutroText = outro,
            Status = DocStatus.Draft,
        };
    }

    private async Task<string?> DefaultTextAsync(TextBlockKind kind, DocType type, CancellationToken ct)
        => await db.TextBlock.AsNoTracking().Where(t => t.Kind == kind && (t.DocType == type || t.DocType == null)).OrderByDescending(t => t.IsDefault).ThenByDescending(t => t.DocType == type).Select(t => t.Text).FirstOrDefaultAsync(ct);

    /// <summary>Suggests lines for a project from its price and its size (and the time not yet billed).</summary>
    public async Task<List<InvoiceLine>> SuggestLinesAsync(Project project, Customer customer, CancellationToken ct = default)
    {
        var vat = config.Current.Company.SmallBusiness ? 0m : config.Current.Company.DefaultVatRate;
        var company = config.Current.Company;
        var chars = await db.Document.AsNoTracking().Where(d => d.ProjectId == project.Id)
            .SumAsync(d => d.Versions.Where(v => v.VersionNo == 1).Select(v => (int?)v.CharCount).FirstOrDefault() ?? 0, ct);
        var words = await db.Document.AsNoTracking().Where(d => d.ProjectId == project.Id)
            .SumAsync(d => d.Versions.Where(v => v.VersionNo == 1).Select(v => (int?)v.WordCount).FirstOrDefault() ?? 0, ct);
        var service = UiKit.Labels.Of(project.ServiceType);
        var title = $"{service}: {project.Title}";
        var lines = new List<InvoiceLine>();
        switch (project.PricingModel)
        {
            case PricingModel.Flat when project.FlatPrice is { } flat:
                lines.Add(new InvoiceLine { Title = title, Quantity = 1, Unit = "pauschal", UnitPrice = flat, VatRate = vat });
                break;
            case PricingModel.PerNormPage when project.Rate is { } rate:
                lines.Add(new InvoiceLine
                {
                    Title = title, Description = $"{Fmt.Number(TextMetrics.NormPages(chars, company.NormPageCharacters), 2)} Normseiten ({company.NormPageCharacters} Zeichen inkl. Leerzeichen)",
                    Quantity = Math.Max(TextMetrics.NormPages(chars, company.NormPageCharacters), 0.01m), Unit = "Normseite", UnitPrice = rate, VatRate = vat,
                });
                break;
            case PricingModel.PerWord when project.Rate is { } rate:
                lines.Add(new InvoiceLine { Title = title, Description = $"{Fmt.Integer(words)} Wörter", Quantity = Math.Max(words, 1), Unit = "Wort", UnitPrice = rate, VatRate = vat });
                break;
            case PricingModel.Hourly when project.Rate is { } rate:
                var seconds = await db.TimeEntry.AsNoTracking().Where(t => t.ProjectId == project.Id && t.InvoiceLineId == null && t.IsBillable && !t.IsRunning).SumAsync(t => (int?)t.Seconds, ct) ?? 0;
                lines.Add(new InvoiceLine { Title = title, Description = "Nach Aufwand", Quantity = Math.Round(Math.Max(seconds, 60) / 3600m, 2), Unit = "Stunde", UnitPrice = rate, VatRate = vat });
                break;
        }

        return lines;
    }

    // ------------------------------------------------------------------------------------------------ issuing

    /// <summary>What stops a draft from being issued, in words. Empty: it can be issued.</summary>
    public IReadOnlyList<string> IssueProblems(Invoice invoice, Customer customer)
    {
        var problems = new List<string>();
        var c = config.Current.Company;
        if (invoice.Type != DocType.Offer)
        {
            if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Street) || string.IsNullOrWhiteSpace(c.City)) { problems.Add("Name und Anschrift des Absenders fehlen (Verwaltung > Firma)."); }
            if (string.IsNullOrWhiteSpace(c.TaxNumber) && string.IsNullOrWhiteSpace(c.VatId)) { problems.Add("Steuernummer oder USt-IdNr. fehlen (Verwaltung > Firma): Sie sind auf einer Rechnung Pflicht."); }
            if (string.IsNullOrWhiteSpace(customer.Street) || string.IsNullOrWhiteSpace(customer.City)) { problems.Add("Die Anschrift des Kunden fehlt: Sie ist auf einer Rechnung Pflicht."); }
        }

        if (invoice.Lines.Count == 0) { problems.Add("Es gibt keine Positionen."); }
        if (invoice.Lines.Any(l => string.IsNullOrWhiteSpace(l.Title))) { problems.Add("Eine Position hat keine Bezeichnung."); }
        return problems;
    }

    /// <summary>Issues a draft: number, snapshot, locked, PDF kept. Saved by SaveChanges of the caller's context (this method saves too: the number must not be lost).</summary>
    public async Task IssueAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (invoice.Status != DocStatus.Draft)
        {
            throw new BillingException("Das Dokument ist schon ausgestellt.");
        }

        var customer = await db.Customer.FirstAsync(c => c.Id == invoice.CustomerId, ct);
        if (IssueProblems(invoice, customer) is { Count: > 0 } problems)
        {
            throw new BillingException(string.Join(' ', problems));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        invoice.Number = await numbers.NextAsync(NumberKindOf(invoice.Type), ct);
        invoice.Status = DocStatus.Issued;
        if (invoice.Type == DocType.Invoice)
        {
            invoice.DueDate ??= invoice.IssueDate.AddDays(invoice.PaymentTermDays);
        }

        Recalculate(invoice);
        var template = await TemplateAsync(invoice.TemplateId, ct);
        var projectTitle = invoice.ProjectId is { } pid ? await db.Project.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Title).FirstOrDefaultAsync(ct) : null;
        var values = PlaceholderValues(invoice, customer, projectTitle, invoice.Number);
        var snapshot = new IssuedSnapshot
        {
            Seller = SellerParty() with { VatId = invoice.SmallBusiness ? null : SellerParty().VatId },
            Buyer = BuyerParty(customer),
            BuyerNumber = customer.Number,
            ProjectTitle = projectTitle,
            Bank = Bank(),
            Settings = InvoiceTemplateSettings.Parse(template.SettingsJson),
            LogoFileId = template.LogoFileId,
            Intro = TextPlaceholders.Resolve(invoice.IntroText, values),
            Outro = TextPlaceholders.Resolve(invoice.OutroText, values),
        };
        invoice.SnapshotJson = JsonSerializer.Serialize(snapshot);
        await db.SaveChangesAsync(ct);

        var document = FromSnapshot(invoice, await LogoBytesAsync(template.LogoFileId, ct));
        var problemsForEInvoice = invoice.Type == DocType.Offer ? ["offer"] : EInvoice.Problems(document);
        var pdf = problemsForEInvoice.Count == 0 ? ZugferdPdf.Create(document, clock.GetUtcNow().UtcDateTime) : InvoicePdf.Render(document);
        var stored = await files.SaveAsync(pdf, $"{document.Title} {invoice.Number}.pdf", "application/pdf", FileKind.InvoicePdf, projectId: invoice.ProjectId, customerId: invoice.CustomerId, invoiceId: invoice.Id, ct: ct);
        await db.SaveChangesAsync(ct);
        invoice.PdfFileId = stored.Id;

        projects.AddHistory(invoice.CustomerId, $"{UiKit.Labels.Of(invoice.Type)} {invoice.Number} ausgestellt", GermanFormat.Money(invoice.GrossTotal, invoice.Currency), invoice.ProjectId, "Invoice", invoice.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>A line in the history of the customer about this document (saved with the next SaveChanges of the caller).</summary>
    public void AddHistoryNote(Invoice invoice, string title)
        => projects.AddHistory(invoice.CustomerId, title, null, invoice.ProjectId, "Invoice", invoice.Id);

    public async Task MarkSentAsync(Invoice invoice, CancellationToken ct = default)
    {
        invoice.SentDate = clock.GetUtcNow().UtcDateTime;
        projects.AddHistory(invoice.CustomerId, $"{UiKit.Labels.Of(invoice.Type)} {invoice.Number} versendet", null, invoice.ProjectId, "Invoice", invoice.Id);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------------------------------ payments and the life after issue

    public async Task AddPaymentAsync(Invoice invoice, decimal amount, DateOnly date, PaymentMethod method, string? reference, CancellationToken ct = default)
    {
        if (invoice.Type != DocType.Invoice || invoice.Status is DocStatus.Draft or DocStatus.Cancelled)
        {
            throw new BillingException("Für dieses Dokument kann keine Zahlung eingetragen werden.");
        }

        if (amount == 0)
        {
            throw new BillingException("Der Betrag darf nicht null sein.");
        }

        db.Payment.Add(new Payment { InvoiceId = invoice.Id, PaidDate = date, Amount = amount, Method = method, Reference = EmptyToNull(reference) });
        invoice.PaidTotal += amount;
        invoice.Status = invoice.PaidTotal >= invoice.GrossTotal ? DocStatus.Paid : invoice.PaidTotal > 0 ? DocStatus.PartiallyPaid : DocStatus.Issued;
        invoice.PaidDate = invoice.Status == DocStatus.Paid ? date : null;
        projects.AddHistory(invoice.CustomerId, $"Zahlung zu {invoice.Number}: {GermanFormat.Money(amount, invoice.Currency)}", UiKit.Labels.Of(method), invoice.ProjectId, "Invoice", invoice.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task RemovePaymentAsync(Invoice invoice, Payment payment, CancellationToken ct = default)
    {
        invoice.PaidTotal -= payment.Amount;
        db.Payment.Remove(payment);
        invoice.Status = invoice.PaidTotal >= invoice.GrossTotal ? DocStatus.Paid : invoice.PaidTotal > 0 ? DocStatus.PartiallyPaid : DocStatus.Issued;
        invoice.PaidDate = invoice.Status == DocStatus.Paid ? invoice.PaidDate : null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Corrects an issued invoice the only way allowed: a credit note with the same positions. The invoice is marked as cancelled.</summary>
    public async Task<Invoice> CancelWithCreditNoteAsync(Invoice invoice, CancellationToken ct = default)
    {
        if (invoice.Type != DocType.Invoice || invoice.Status is DocStatus.Draft or DocStatus.Cancelled)
        {
            throw new BillingException("Dieses Dokument kann nicht storniert werden.");
        }

        var credit = await NewDraftAsync(DocType.CreditNote, await db.Customer.FirstAsync(c => c.Id == invoice.CustomerId, ct), invoice.ProjectId, ct);
        credit.TemplateId = invoice.TemplateId;
        credit.SourceDocumentId = invoice.Id;
        credit.Subject = $"Stornierung der Rechnung {invoice.Number}";
        credit.SmallBusiness = invoice.SmallBusiness;
        credit.ReverseCharge = invoice.ReverseCharge;
        credit.DiscountPercent = invoice.DiscountPercent;
        credit.ServiceFrom = invoice.ServiceFrom;
        credit.ServiceTo = invoice.ServiceTo;
        foreach (var line in invoice.Lines.OrderBy(l => l.Position))
        {
            credit.Lines.Add(new InvoiceLine { Position = line.Position, ProductId = line.ProductId, Title = line.Title, Description = line.Description, Quantity = line.Quantity, Unit = line.Unit, UnitPrice = line.UnitPrice, VatRate = line.VatRate, DiscountPercent = line.DiscountPercent });
        }

        Recalculate(credit);
        db.Invoice.Add(credit);
        await db.SaveChangesAsync(ct);
        await IssueAsync(credit, ct);
        invoice.Status = DocStatus.Cancelled;

        // The time that was billed on the cancelled invoice can be billed again.
        var lineIds = invoice.Lines.Select(l => l.Id).ToList();
        foreach (var entry in await db.TimeEntry.Where(t => t.InvoiceLineId != null && lineIds.Contains(t.InvoiceLineId.Value)).ToListAsync(ct))
        {
            entry.InvoiceLineId = null;
        }

        projects.AddHistory(invoice.CustomerId, $"Rechnung {invoice.Number} storniert", $"Gutschrift {credit.Number}", invoice.ProjectId, "Invoice", invoice.Id);
        await db.SaveChangesAsync(ct);
        return credit;
    }

    /// <summary>An invoice made from an offer: the same positions, as a draft.</summary>
    public async Task<Invoice> InvoiceFromOfferAsync(Invoice offer, CancellationToken ct = default)
    {
        if (offer.Type != DocType.Offer || offer.Status == DocStatus.Draft)
        {
            throw new BillingException("Nur aus einem ausgestellten Angebot kann eine Rechnung entstehen.");
        }

        var customer = await db.Customer.FirstAsync(c => c.Id == offer.CustomerId, ct);
        var invoice = await NewDraftAsync(DocType.Invoice, customer, offer.ProjectId, ct);
        invoice.SourceDocumentId = offer.Id;
        invoice.TemplateId = offer.TemplateId ?? invoice.TemplateId;
        invoice.Subject = offer.Subject;
        invoice.DiscountPercent = offer.DiscountPercent;
        invoice.SmallBusiness = offer.SmallBusiness;
        invoice.ReverseCharge = offer.ReverseCharge;
        invoice.CustomerReference = offer.CustomerReference;
        foreach (var line in offer.Lines.OrderBy(l => l.Position))
        {
            invoice.Lines.Add(new InvoiceLine { Position = line.Position, ProductId = line.ProductId, Title = line.Title, Description = line.Description, Quantity = line.Quantity, Unit = line.Unit, UnitPrice = line.UnitPrice, VatRate = line.VatRate, DiscountPercent = line.DiscountPercent });
        }

        Recalculate(invoice);
        db.Invoice.Add(invoice);
        offer.Status = DocStatus.Accepted;
        await db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Invoice> DuplicateAsync(Invoice source, CancellationToken ct = default)
    {
        var customer = await db.Customer.FirstAsync(c => c.Id == source.CustomerId, ct);
        var copy = await NewDraftAsync(source.Type, customer, source.ProjectId, ct);
        copy.TemplateId = source.TemplateId;
        copy.Subject = source.Subject;
        copy.IntroText = source.IntroText;
        copy.OutroText = source.OutroText;
        copy.DiscountPercent = source.DiscountPercent;
        copy.SmallBusiness = source.SmallBusiness;
        copy.ReverseCharge = source.ReverseCharge;
        copy.CustomerReference = source.CustomerReference;
        foreach (var line in source.Lines.OrderBy(l => l.Position))
        {
            copy.Lines.Add(new InvoiceLine { Position = line.Position, ProductId = line.ProductId, Title = line.Title, Description = line.Description, Quantity = line.Quantity, Unit = line.Unit, UnitPrice = line.UnitPrice, VatRate = line.VatRate, DiscountPercent = line.DiscountPercent });
        }

        Recalculate(copy);
        db.Invoice.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy;
    }

    /// <summary>The unbilled working time of a project as a line (hours at the hourly rate of the customer or the company); the entries are marked as billed when the invoice is issued.</summary>
    public async Task<(InvoiceLine Line, List<long> EntryIds)?> TimeLineAsync(long projectId, Customer customer, CancellationToken ct = default)
    {
        var entries = await db.TimeEntry.AsNoTracking().Where(t => t.ProjectId == projectId && t.InvoiceLineId == null && t.IsBillable && !t.IsRunning && t.Seconds > 0).ToListAsync(ct);
        if (entries.Count == 0)
        {
            return null;
        }

        var seconds = entries.Sum(t => t.Seconds);
        var hours = Math.Round(seconds / 3600m * 4, MidpointRounding.AwayFromZero) / 4;
        var company = config.Current.Company;
        var first = DateOnly.FromDateTime(Fmt.Local(entries.Min(t => t.StartDate)));
        var last = DateOnly.FromDateTime(Fmt.Local(entries.Max(t => t.StartDate)));
        var line = new InvoiceLine
        {
            Title = "Arbeitszeit nach Aufwand",
            Description = $"Zeitraum {GermanFormat.Date(first)} bis {GermanFormat.Date(last)}",
            Quantity = Math.Max(hours, 0.25m),
            Unit = "Stunde",
            UnitPrice = customer.HourlyRate ?? company.DefaultHourlyRate,
            VatRate = company.SmallBusiness ? 0m : company.DefaultVatRate,
        };
        return (line, entries.Select(t => t.Id).ToList());
    }

    /// <summary>Marks time entries as billed on the lines of an invoice that was just saved (the ids come with the line description in a hidden field of the edit page).</summary>
    public async Task LinkTimeAsync(IEnumerable<long> entryIds, InvoiceLine line, CancellationToken ct = default)
    {
        var ids = entryIds.ToList();
        foreach (var entry in await db.TimeEntry.Where(t => ids.Contains(t.Id) && t.InvoiceLineId == null).ToListAsync(ct))
        {
            entry.InvoiceLineId = line.Id;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Gives the time back when a draft or one of its lines is removed.</summary>
    public async Task ReleaseTimeAsync(IEnumerable<long> lineIds, CancellationToken ct = default)
    {
        var ids = lineIds.ToList();
        foreach (var entry in await db.TimeEntry.Where(t => t.InvoiceLineId != null && ids.Contains(t.InvoiceLineId.Value)).ToListAsync(ct))
        {
            entry.InvoiceLineId = null;
        }
    }

    public bool IsOverdue(Invoice invoice) => invoice.Type == DocType.Invoice && invoice.Status is DocStatus.Issued or DocStatus.PartiallyPaid && invoice.DueDate < Fmt.Today(clock);

    public string StatusText(Invoice invoice) => IsOverdue(invoice) ? "Überfällig" : UiKit.Labels.Of(invoice.Status, invoice.Type);

    public string StatusTone(Invoice invoice) => IsOverdue(invoice) ? "error" : UiKit.Labels.Tone(invoice.Status);

}
