using QuestPDF.Infrastructure;

namespace KommaKorrektus.Core.Billing;

/// <summary>
/// The settings of the PDF engine that every document needs. QuestPDF is free for a business with a small turnover (the Community licence: this is
/// a tool of a freelance editor; a larger company needs a licence of its own). Since QuestPDF 2026.9 the fonts of the system are no longer used
/// unless asked for: the serif font of a template is a system font (Liberation Serif in the container, Times New Roman on Windows), the sans font
/// (Lato) comes with the engine. A font that is missing is replaced by the next one of the list instead of stopping the invoice.
/// </summary>
public static class PdfEnvironment
{
    private static readonly object Gate = new();
    private static bool _done;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_done)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = true;
            QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
            _done = true;
        }
    }
}
