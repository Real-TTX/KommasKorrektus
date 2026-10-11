using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Customers;

/// <summary>A contact person of a customer (a publisher has several).</summary>
public sealed class ContactModel(KkDbContext db) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib einen Namen ein.")]
        [Display(Name = "Name")]
        public string Name { get; set; } = "";

        [Display(Name = "Rolle")]
        public string? Role { get; set; }

        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "E-Mail-Adresse")]
        public string? Email { get; set; }

        [Display(Name = "Telefon")]
        public string? Phone { get; set; }

        public bool IsPrimary { get; set; }

        [Display(Name = "Notizen")]
        public string? Notes { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long CustomerId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Customer Customer { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (Id is { } id)
        {
            var c = await db.CustomerContact.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id && k.CustomerId == CustomerId);
            if (c is null)
            {
                return NotFound();
            }

            Input = new InputModel { Name = c.Name, Role = c.Role, Email = c.Email, Phone = c.Phone, IsPrimary = c.IsPrimary, Notes = c.Notes };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        CustomerContact contact;
        if (Id is { } id)
        {
            contact = await db.CustomerContact.FirstOrDefaultAsync(k => k.Id == id && k.CustomerId == CustomerId) ?? throw new InvalidOperationException("Der Kontakt existiert nicht mehr.");
        }
        else
        {
            contact = new CustomerContact { CustomerId = CustomerId };
            db.CustomerContact.Add(contact);
        }

        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        contact.Name = Input.Name.Trim();
        contact.Role = Clean(Input.Role);
        contact.Email = Clean(Input.Email)?.ToLowerInvariant();
        contact.Phone = Clean(Input.Phone);
        contact.Notes = Clean(Input.Notes);
        contact.IsPrimary = Input.IsPrimary;
        if (contact.IsPrimary)
        {
            foreach (var other in await db.CustomerContact.Where(k => k.CustomerId == CustomerId && k.Id != contact.Id && k.IsPrimary).ToListAsync())
            {
                other.IsPrimary = false;
            }
        }

        await db.SaveChangesAsync();
        Flash("Der Kontakt ist gespeichert.");
        return RedirectToPage("/Customers/Details", new { id = CustomerId, tab = "contacts" });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var contact = await db.CustomerContact.FirstOrDefaultAsync(k => k.Id == Id && k.CustomerId == CustomerId);
        if (contact is not null)
        {
            db.CustomerContact.Remove(contact);
            await db.SaveChangesAsync();
            Flash("Der Kontakt ist gelöscht.");
        }

        return RedirectToPage("/Customers/Details", new { id = CustomerId, tab = "contacts" });
    }

    private async Task<bool> PrepareAsync()
    {
        var customer = await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
        {
            return false;
        }

        Customer = customer;
        var title = Id is null ? "Neuer Kontakt" : "Kontakt bearbeiten";
        Head(title, null, ("Kunden", "/Customers"), (customer.DisplayName, $"/Customers/Details?id={customer.Id}&tab=contacts"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Customers";
        return true;
    }
}
