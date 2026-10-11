using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;

namespace KommaKorrektus.Pages.Admin;

/// <summary>The sender of offers and invoices: who the editor is, how she is paid and how documents are numbered. Kept in the JSON configuration.</summary>
public sealed class CompanyModel(ConfigStore config, ActivityLogger log) : PageModelBase
{
    [BindProperty]
    public CompanyConfig Company { get; set; } = new();

    public IReadOnlyDictionary<string, string> Examples { get; private set; } = new Dictionary<string, string>();

    public async Task OnGetAsync()
    {
        Company = Clone(config.Current.Company);
        await PrepareAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Validate();
        if (!ModelState.IsValid)
        {
            await PrepareAsync();
            return Page();
        }

        config.Update(c =>
        {
            Company.Iban = Iban.Normalize(Company.Iban);
            Company.Bic = Company.Bic.Replace(" ", string.Empty).ToUpperInvariant();
            c.Company = Company;
        });
        await log.LogAsync("Firmendaten geändert", "Company");
        Flash("Die Firmendaten sind gespeichert.");
        return RedirectToPage();
    }

    private void Validate()
    {
        var c = Company;
        if (string.IsNullOrWhiteSpace(c.Name))
        {
            ModelState.AddModelError("Company.Name", "Bitte gib den Namen deiner Firma oder deinen Namen ein.");
        }

        if (!string.IsNullOrWhiteSpace(c.Iban) && !Iban.IsValid(c.Iban))
        {
            ModelState.AddModelError("Company.Iban", "Diese IBAN ist nicht gültig. Prüfe die Ziffern.");
        }

        if (!string.IsNullOrWhiteSpace(c.Email) && !c.Email.Contains('@'))
        {
            ModelState.AddModelError("Company.Email", "Das ist keine gültige E-Mail-Adresse.");
        }

        if (c.DefaultVatRate is < 0 or > 30)
        {
            ModelState.AddModelError("Company.DefaultVatRate", "Der Steuersatz liegt zwischen 0 und 30 Prozent.");
        }

        if (c.DefaultPaymentDays is < 0 or > 365)
        {
            ModelState.AddModelError("Company.DefaultPaymentDays", "Das Zahlungsziel liegt zwischen 0 und 365 Tagen.");
        }

        if (c.NormPageCharacters is < 500 or > 5000)
        {
            ModelState.AddModelError("Company.NormPageCharacters", "Eine Normseite hat üblicherweise 1800 Zeichen.");
        }

        foreach (var (field, format) in new[]
        {
            (nameof(CompanyConfig.CustomerNumberFormat), c.CustomerNumberFormat), (nameof(CompanyConfig.ProjectNumberFormat), c.ProjectNumberFormat),
            (nameof(CompanyConfig.OfferNumberFormat), c.OfferNumberFormat), (nameof(CompanyConfig.InvoiceNumberFormat), c.InvoiceNumberFormat),
            (nameof(CompanyConfig.CreditNoteNumberFormat), c.CreditNoteNumberFormat), (nameof(CompanyConfig.ProductNumberFormat), c.ProductNumberFormat),
        })
        {
            if (string.IsNullOrWhiteSpace(format) || !System.Text.RegularExpressions.Regex.IsMatch(format, "\\{N+\\}"))
            {
                ModelState.AddModelError($"Company.{field}", "Das Format braucht einen Zähler, zum Beispiel {NNNN}.");
            }
        }
    }

    private async Task PrepareAsync()
    {
        Head("Firma", "Der Absender von Angeboten und Rechnungen.", ("Verwaltung", null), ("Firma", null));
        ViewData["Width"] = "narrow";
        var today = Fmt.Today(TimeProvider.System);
        Examples = new Dictionary<string, string>
        {
            ["Customer"] = NumberService.Format(Company.CustomerNumberFormat, today, 1),
            ["Project"] = NumberService.Format(Company.ProjectNumberFormat, today, 1),
            ["Offer"] = NumberService.Format(Company.OfferNumberFormat, today, 1),
            ["Invoice"] = NumberService.Format(Company.InvoiceNumberFormat, today, 1),
            ["CreditNote"] = NumberService.Format(Company.CreditNoteNumberFormat, today, 1),
            ["Product"] = NumberService.Format(Company.ProductNumberFormat, today, 1),
        };
        await Task.CompletedTask;
    }

    private static CompanyConfig Clone(CompanyConfig source)
        => System.Text.Json.JsonSerializer.Deserialize<CompanyConfig>(System.Text.Json.JsonSerializer.Serialize(source))!;
}
