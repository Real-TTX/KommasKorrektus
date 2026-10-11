using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Customers;

/// <summary>Create and edit a customer: a page of its own, not a dialog.</summary>
public sealed class EditModel(KkDbContext db, NumberService numbers, ConfigStore config, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        public CustomerKind Kind { get; set; } = CustomerKind.Person;

        [Display(Name = "Firma / Verlag")]
        public string? Company { get; set; }

        [Display(Name = "Anrede")]
        public string? Salutation { get; set; }

        [Display(Name = "Vorname")]
        public string? FirstName { get; set; }

        [Display(Name = "Nachname")]
        public string? LastName { get; set; }

        [Display(Name = "Pseudonym")]
        public string? PenName { get; set; }

        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "E-Mail-Adresse")]
        public string? Email { get; set; }

        [Display(Name = "Telefon")]
        public string? Phone { get; set; }

        [Display(Name = "Straße und Hausnummer")]
        public string? Street { get; set; }

        [Display(Name = "Postleitzahl")]
        public string? PostalCode { get; set; }

        [Display(Name = "Ort")]
        public string? City { get; set; }

        [Display(Name = "Land")]
        public string Country { get; set; } = "DE";

        [Display(Name = "USt-IdNr.")]
        public string? VatId { get; set; }

        [Display(Name = "Leitweg-ID")]
        public string? BuyerReference { get; set; }

        [Display(Name = "Zahlungsziel in Tagen")]
        [Range(0, 365, ErrorMessage = "Das Zahlungsziel liegt zwischen 0 und 365 Tagen.")]
        public int? PaymentTermDays { get; set; }

        [Display(Name = "Stundensatz (netto)")]
        [Range(0, 10000, ErrorMessage = "Bitte gib einen Stundensatz zwischen 0 und 10000 ein.")]
        public decimal? HourlyRate { get; set; }

        [Display(Name = "Notizen")]
        public string? Notes { get; set; }

        public bool IsArchived { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Number { get; private set; }

    public string? DeleteBlocker { get; private set; }

    public int DefaultPaymentDays => config.Current.Company.DefaultPaymentDays;

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id is { } id)
        {
            var customer = await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (customer is null)
            {
                return NotFound();
            }

            Input = new InputModel
            {
                Kind = customer.Kind, Company = customer.Company, Salutation = customer.Salutation, FirstName = customer.FirstName, LastName = customer.LastName,
                PenName = customer.PenName, Email = customer.Email, Phone = customer.Phone, Street = customer.Street, PostalCode = customer.PostalCode,
                City = customer.City, Country = customer.Country, VatId = customer.VatId, BuyerReference = customer.BuyerReference,
                PaymentTermDays = customer.PaymentTermDays, HourlyRate = customer.HourlyRate, Notes = customer.Notes, IsArchived = customer.IsArchived,
            };
            Number = customer.Number;
        }

        await PrepareAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var i = Input;
        if (i.Kind == CustomerKind.Company && string.IsNullOrWhiteSpace(i.Company))
        {
            ModelState.AddModelError("Input.Company", "Bitte gib den Namen der Firma oder des Verlags ein.");
        }

        if (i.Kind == CustomerKind.Person && string.IsNullOrWhiteSpace(i.LastName))
        {
            ModelState.AddModelError("Input.LastName", "Bitte gib den Nachnamen ein.");
        }

        if (!ModelState.IsValid)
        {
            await PrepareAsync();
            return Page();
        }

        Customer customer;
        if (Id is { } id)
        {
            customer = await db.Customer.FirstOrDefaultAsync(c => c.Id == id) ?? throw new InvalidOperationException("Der Kunde existiert nicht mehr.");
        }
        else
        {
            customer = new Customer { Number = await numbers.NextAsync(NumberKind.Customer) };
            db.Customer.Add(customer);
        }

        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        customer.Kind = i.Kind;
        customer.Company = i.Kind == CustomerKind.Company ? Clean(i.Company) : null;
        customer.Salutation = Clean(i.Salutation);
        customer.FirstName = Clean(i.FirstName);
        customer.LastName = Clean(i.LastName);
        customer.PenName = Clean(i.PenName);
        customer.Email = Clean(i.Email)?.ToLowerInvariant();
        customer.Phone = Clean(i.Phone);
        customer.Street = Clean(i.Street);
        customer.PostalCode = Clean(i.PostalCode);
        customer.City = Clean(i.City);
        customer.Country = string.IsNullOrWhiteSpace(i.Country) ? "DE" : i.Country.Trim().ToUpperInvariant();
        customer.VatId = i.Kind == CustomerKind.Company ? Clean(i.VatId) : null;
        customer.BuyerReference = i.Kind == CustomerKind.Company ? Clean(i.BuyerReference) : null;
        customer.PaymentTermDays = i.PaymentTermDays;
        customer.HourlyRate = i.HourlyRate;
        customer.Notes = Clean(i.Notes);
        customer.IsArchived = i.IsArchived;

        await db.SaveChangesAsync();
        await log.LogAsync(Id is null ? "Kunde angelegt" : "Kunde geändert", "Customer", customer.Id, customer.DisplayName);
        Flash(Id is null ? "Der Kunde ist angelegt." : "Der Kunde ist gespeichert.");
        return RedirectToPage("/Customers/Details", new { id = customer.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var customer = await db.Customer.FirstOrDefaultAsync(c => c.Id == Id);
        if (customer is null)
        {
            return NotFound();
        }

        if (await BlockerAsync(customer.Id) is { } blocker)
        {
            FlashError(blocker);
            return RedirectToPage(new { id = customer.Id });
        }

        db.Customer.Remove(customer);
        await db.SaveChangesAsync();
        await log.LogAsync("Kunde gelöscht", "Customer", customer.Id, customer.DisplayName);
        Flash("Der Kunde ist gelöscht.");
        return RedirectToPage("/Customers/Index");
    }

    private async Task PrepareAsync()
    {
        var title = Id is null ? "Neuer Kunde" : "Kunde bearbeiten";
        Head(title, null, ("Kunden", "/Customers"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Customers";
        if (Id is { } id)
        {
            DeleteBlocker = await BlockerAsync(id);
        }
    }

    /// <summary>A customer with projects or invoices is archived, never deleted: the invoices have to stay readable.</summary>
    private async Task<string?> BlockerAsync(long customerId)
    {
        if (await db.Invoice.AnyAsync(i => i.CustomerId == customerId))
        {
            return "Zu diesem Kunden gibt es Rechnungen oder Angebote. Lösche sie nicht, sondern archiviere den Kunden.";
        }

        if (await db.Project.AnyAsync(p => p.CustomerId == customerId))
        {
            return "Zu diesem Kunden gibt es Projekte. Lösche zuerst die Projekte oder archiviere den Kunden.";
        }

        return null;
    }
}
