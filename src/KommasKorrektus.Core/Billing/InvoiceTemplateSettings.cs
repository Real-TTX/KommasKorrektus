using System.Text.Json;

namespace KommasKorrektus.Core.Billing;

/// <summary>
/// The look of offers, invoices and credit notes. It is stored as JSON in the template and drives both the PDF and the live preview in the
/// template editor, so what is shown there is what is printed.
/// </summary>
public sealed class InvoiceTemplateSettings
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = false };

    /// <summary>The accent colour: the title, the rules and the head of the table.</summary>
    public string AccentColor { get; set; } = "#a43bd0";

    /// <summary>"Sans" or "Serif".</summary>
    public string FontFamily { get; set; } = "Sans";

    public double FontSize { get; set; } = 10;

    /// <summary>"Left" or "Right".</summary>
    public string LogoPosition { get; set; } = "Right";

    public int LogoHeightMm { get; set; } = 18;

    public string TitleOffer { get; set; } = "Angebot";

    public string TitleInvoice { get; set; } = "Rechnung";

    public string TitleCreditNote { get; set; } = "Gutschrift";

    /// <summary>A small line above the address window: who the sender is.</summary>
    public bool ShowSenderLine { get; set; } = true;

    public bool ShowQuantityColumn { get; set; } = true;

    public bool ShowUnitColumn { get; set; } = true;

    public bool ShowVatColumn { get; set; } = true;

    public bool ShowDiscountColumn { get; set; }

    public bool ShowBankDetails { get; set; } = true;

    /// <summary>A QR code that a banking app reads to fill in the transfer (GiroCode, EPC).</summary>
    public bool ShowPaymentQr { get; set; } = true;

    /// <summary>The page number in the footer.</summary>
    public bool ShowPageNumbers { get; set; } = true;

    /// <summary>A text for the foot of every page, below the three columns of the company data; empty: none.</summary>
    public string FooterNote { get; set; } = "";

    /// <summary>A text that follows the outro on invoices (payment information, retention note).</summary>
    public string InvoiceNote { get; set; } = "";

    /// <summary>"A4" or "Letter".</summary>
    public string PaperSize { get; set; } = "A4";

    public int MarginMm { get; set; } = 20;

    public static InvoiceTemplateSettings Default => new();

    public static InvoiceTemplateSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new InvoiceTemplateSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<InvoiceTemplateSettings>(json, Json) ?? new InvoiceTemplateSettings();
        }
        catch (JsonException)
        {
            return new InvoiceTemplateSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A colour as it may be written into a document: #rrggbb only. Anything else is replaced by the default.</summary>
    public static string SafeColor(string? value, string fallback = "#a43bd0")
        => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit) ? value.ToLowerInvariant() : fallback;
}
