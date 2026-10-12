namespace KommasKorrektus.Data.Entities;

public enum DocType
{
    Offer,
    Invoice,
    /// <summary>A credit note (Gutschrift) or the cancellation (Storno) of an invoice: the amounts are negative.</summary>
    CreditNote,
}

public enum DocStatus
{
    /// <summary>Editable, has no number yet.</summary>
    Draft,
    /// <summary>Issued: it has a number and is locked. Corrections are made with a credit note.</summary>
    Issued,
    /// <summary>Offer only.</summary>
    Accepted,
    /// <summary>Offer only.</summary>
    Declined,
    PartiallyPaid,
    Paid,
    Cancelled,
}

/// <summary>
/// An offer, an invoice or a credit note: they share everything but the title and the rules (an invoice is locked once issued).
/// The totals are kept in the row so that lists and reports need no calculation.
/// </summary>
public class Invoice : AuditedEntity
{
    public DocType Type { get; set; } = DocType.Invoice;

    /// <summary>Null while a draft: the number is taken when the document is issued, so the numbers have no gaps.</summary>
    public string? Number { get; set; }

    public long CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public long? ProjectId { get; set; }

    public Project? Project { get; set; }

    public long? TemplateId { get; set; }

    public InvoiceTemplate? Template { get; set; }

    public DocStatus Status { get; set; } = DocStatus.Draft;

    public DateOnly IssueDate { get; set; }

    /// <summary>The period of the service (Leistungszeitraum), required on an invoice.</summary>
    public DateOnly? ServiceFrom { get; set; }

    public DateOnly? ServiceTo { get; set; }

    public DateOnly? DueDate { get; set; }

    /// <summary>Offers: how long the price stands.</summary>
    public DateOnly? ValidUntil { get; set; }

    public int PaymentTermDays { get; set; } = 14;

    public string? Subject { get; set; }

    public string? IntroText { get; set; }

    public string? OutroText { get; set; }

    public string Currency { get; set; } = "EUR";

    /// <summary>A discount on the whole document, in percent.</summary>
    public decimal DiscountPercent { get; set; }

    public decimal NetTotal { get; set; }

    public decimal VatTotal { get; set; }

    public decimal GrossTotal { get; set; }

    /// <summary>Paragraph 19 UStG: no VAT is shown. Copied from the company settings when the draft is made.</summary>
    public bool SmallBusiness { get; set; }

    /// <summary>The client owes the VAT (reverse charge).</summary>
    public bool ReverseCharge { get; set; }

    /// <summary>Leitweg-ID for an e-invoice to a public body.</summary>
    public string? BuyerReference { get; set; }

    /// <summary>The order number or reference of the customer.</summary>
    public string? CustomerReference { get; set; }

    /// <summary>The offer an invoice was made from, or the invoice a credit note cancels.</summary>
    public long? SourceDocumentId { get; set; }

    public DateTime? SentDate { get; set; }

    public DateOnly? PaidDate { get; set; }

    public decimal PaidTotal { get; set; }

    public string? InternalNote { get; set; }

    /// <summary>The PDF as it was issued (with the e-invoice inside), kept for good.</summary>
    public long? PdfFileId { get; set; }

    /// <summary>The sender, the recipient and the look at the moment of issue (JSON). An issued invoice is never redrawn from today's data.</summary>
    public string? SnapshotJson { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];

    public bool IsLocked => Status != DocStatus.Draft;
}

public class InvoiceLine : AuditedEntity
{
    public long InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public int Position { get; set; }

    public long? ProductId { get; set; }

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public decimal Quantity { get; set; } = 1;

    public string Unit { get; set; } = "Stk.";

    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; } = 19m;

    public decimal DiscountPercent { get; set; }

    public decimal NetAmount { get; set; }
}

public enum PaymentMethod
{
    Transfer,
    Cash,
    PayPal,
    Other,
}

public class Payment : AuditedEntity
{
    public long InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public DateOnly PaidDate { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Transfer;

    public string? Reference { get; set; }
}

/// <summary>A service or item that can be put on an offer or invoice.</summary>
public class Product : AuditedEntity, ISoftDeletable
{
    public string Number { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>"Normseite", "Wort", "Stunde", "pauschal"...</summary>
    public string Unit { get; set; } = "Stk.";

    public decimal NetPrice { get; set; }

    public decimal VatRate { get; set; } = 19m;

    public string? Category { get; set; }

    public ServiceType? ServiceType { get; set; }

    public bool IsActive { get; set; } = true;

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>The look of offers and invoices: logo, title, colour, texts, columns. The settings are JSON (see <c>InvoiceTemplateSettings</c>).</summary>
public class InvoiceTemplate : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public bool IsDefault { get; set; }

    public long? LogoFileId { get; set; }

    public string SettingsJson { get; set; } = "{}";

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

public enum TextBlockKind
{
    Intro,
    Outro,
    Mail,
    Reminder,
}

/// <summary>A ready-made text (Textbaustein) for the head and the foot of a document or for a mail.</summary>
public class TextBlock : AuditedEntity, ISoftDeletable
{
    public string Name { get; set; } = "";

    public TextBlockKind Kind { get; set; } = TextBlockKind.Intro;

    /// <summary>Null: for every kind of document.</summary>
    public DocType? DocType { get; set; }

    public string Text { get; set; } = "";

    public bool IsDefault { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;
}

/// <summary>A counter for numbers that must not repeat ("Invoice:2026"). It is incremented in one statement, so two requests never get the same number.</summary>
public class NumberSequence : AuditedEntity
{
    public string Key { get; set; } = "";

    public long NextValue { get; set; } = 1;
}
