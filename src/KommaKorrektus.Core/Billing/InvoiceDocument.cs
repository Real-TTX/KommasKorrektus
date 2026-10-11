using System.Text.RegularExpressions;

namespace KommaKorrektus.Core.Billing;

public enum InvoiceKind
{
    Offer,
    Invoice,
    CreditNote,
}

/// <summary>One side of a document: who writes or who gets it.</summary>
public sealed record Party(
    string Name,
    string? Contact,
    string Street,
    string PostalCode,
    string City,
    string Country,
    string? Email = null,
    string? Phone = null,
    string? VatId = null,
    string? TaxNumber = null,
    string? Website = null)
{
    public string CityLine => string.Join(' ', new[] { PostalCode, City }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public sealed record BankInfo(string Holder, string BankName, string Iban, string Bic);

public sealed record DocumentLine(int Position, string Title, string? Description, decimal Quantity, string Unit, decimal UnitPrice, decimal VatRate, decimal DiscountPercent, decimal NetAmount);

/// <summary>
/// Everything a printed offer, invoice or credit note shows, resolved: texts without placeholders, amounts worked out. The PDF, the preview in the
/// template editor and the e-invoice are all made from this one object, so they cannot disagree.
/// </summary>
public sealed class InvoiceDocument
{
    public required InvoiceKind Kind { get; init; }

    /// <summary>Null for a draft: it has no number yet.</summary>
    public string? Number { get; init; }

    public required DateOnly IssueDate { get; init; }

    public DateOnly? ServiceFrom { get; init; }

    public DateOnly? ServiceTo { get; init; }

    public DateOnly? DueDate { get; init; }

    public DateOnly? ValidUntil { get; init; }

    public string? Subject { get; init; }

    public string Intro { get; init; } = "";

    public string Outro { get; init; } = "";

    public string Currency { get; init; } = "EUR";

    public required Party Seller { get; init; }

    public required Party Buyer { get; init; }

    public string? BuyerNumber { get; init; }

    /// <summary>Leitweg-ID for an e-invoice to a public body.</summary>
    public string? BuyerReference { get; init; }

    public string? CustomerReference { get; init; }

    /// <summary>The offer or invoice this one refers to ("zu Rechnung RE-2026-0001").</summary>
    public string? RelatedNumber { get; init; }

    public string? ProjectTitle { get; init; }

    public required IReadOnlyList<DocumentLine> Lines { get; init; }

    public decimal DiscountPercent { get; init; }

    public required InvoiceTotals Totals { get; init; }

    public bool SmallBusiness { get; init; }

    public bool ReverseCharge { get; init; }

    public int PaymentTermDays { get; init; }

    public BankInfo? Bank { get; init; }

    public required InvoiceTemplateSettings Settings { get; init; }

    public byte[]? Logo { get; init; }

    public bool IsDraft => string.IsNullOrEmpty(Number);

    public string Title => Kind switch
    {
        InvoiceKind.Offer => Settings.TitleOffer,
        InvoiceKind.CreditNote => Settings.TitleCreditNote,
        _ => Settings.TitleInvoice,
    };

    /// <summary>The note about tax that the law wants on the document, or null.</summary>
    public string? TaxNote => SmallBusiness
        ? "Gemäß § 19 UStG wird keine Umsatzsteuer berechnet."
        : ReverseCharge ? "Steuerschuldnerschaft des Leistungsempfängers (Reverse Charge): Der Leistungsempfänger schuldet die Umsatzsteuer." : null;
}

/// <summary>
/// The placeholders of the texts of a document: <c>{{Anrede}}</c>, <c>{{Projekt}}</c>, <c>{{Nummer}}</c>... An unknown placeholder is left as it is, so a typo is
/// visible in the preview and does not turn into an empty hole.
/// </summary>
public static partial class TextPlaceholders
{
    [GeneratedRegex(@"\{\{\s*([A-Za-zÄÖÜäöüß]+)\s*\}\}")]
    private static partial Regex Pattern();

    public static string Resolve(string? text, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return Pattern().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    /// <summary>The names the editor of texts offers.</summary>
    public static IReadOnlyList<(string Name, string Description)> Known { get; } =
    [
        ("Anrede", "Die Anredezeile: „Sehr geehrte Frau Müller,“"),
        ("Kunde", "Name des Kunden oder der Firma"),
        ("Projekt", "Titel des Projekts"),
        ("Nummer", "Nummer des Dokuments"),
        ("Datum", "Datum des Dokuments"),
        ("Betrag", "Gesamtbetrag (brutto)"),
        ("Zahlungsziel", "Datum, bis zu dem bezahlt werden soll"),
        ("Tage", "Zahl der Tage bis zur Fälligkeit"),
        ("Gueltig", "Datum, bis zu dem ein Angebot gilt"),
        ("Bezug", "Nummer der Rechnung, auf die sich eine Gutschrift bezieht"),
    ];
}
