using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Products;

public sealed class EditModel(KkDbContext db, NumberService numbers, ConfigStore config) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib dem Produkt einen Namen.")]
        [Display(Name = "Name")]
        public string Name { get; set; } = "";

        [Display(Name = "Beschreibung")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Bitte gib die Einheit an.")]
        [Display(Name = "Einheit")]
        public string Unit { get; set; } = "Stunde";

        [Display(Name = "Preis (netto)")]
        [Range(0, 10000000, ErrorMessage = "Bitte gib einen Preis ab 0 ein.")]
        public decimal NetPrice { get; set; }

        [Display(Name = "Umsatzsteuer in Prozent")]
        [Range(0, 30, ErrorMessage = "Der Steuersatz liegt zwischen 0 und 30 Prozent.")]
        public decimal VatRate { get; set; } = 19;

        [Display(Name = "Kategorie")]
        public string? Category { get; set; }

        [Display(Name = "Leistung")]
        public ServiceType? ServiceType { get; set; }

        public bool IsActive { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Number { get; private set; }

    public bool SmallBusiness => config.Current.Company.SmallBusiness;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var p = await db.Product.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (p is null)
            {
                return NotFound();
            }

            Input = new InputModel { Name = p.Name, Description = p.Description, Unit = p.Unit, NetPrice = p.NetPrice, VatRate = p.VatRate, Category = p.Category, ServiceType = p.ServiceType, IsActive = p.IsActive };
            Number = p.Number;
        }
        else
        {
            Input.VatRate = config.Current.Company.DefaultVatRate;
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

        Product product;
        if (Id is { } id)
        {
            product = await db.Product.FirstOrDefaultAsync(p => p.Id == id) ?? throw new InvalidOperationException("Das Produkt existiert nicht mehr.");
        }
        else
        {
            product = new Product { Number = await numbers.NextAsync(NumberKind.Product) };
            db.Product.Add(product);
        }

        product.Name = Input.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        product.Unit = Input.Unit.Trim();
        product.NetPrice = Input.NetPrice;
        product.VatRate = Input.VatRate;
        product.Category = string.IsNullOrWhiteSpace(Input.Category) ? null : Input.Category.Trim();
        product.ServiceType = Input.ServiceType;
        product.IsActive = Input.IsActive;
        await db.SaveChangesAsync();
        Flash("Das Produkt ist gespeichert.");
        return RedirectToPage("/Products/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var product = await db.Product.FirstOrDefaultAsync(p => p.Id == Id);
        if (product is not null)
        {
            db.Product.Remove(product);
            await db.SaveChangesAsync();
            Flash("Das Produkt ist gelöscht. Bestehende Rechnungen bleiben unverändert.");
        }

        return RedirectToPage("/Products/Index");
    }

    private void Prepare()
    {
        var title = Id is null ? "Neues Produkt" : "Produkt bearbeiten";
        Head(title, null, ("Rechnungen", "/Invoices"), ("Produkte", "/Products"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Products";
    }
}
