using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Auth;
using KommasKorrektus.Services.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Customers;

/// <summary>Invites a person to the portal of a customer. The invitation is a link that is valid for a limited time; it is sent by mail and shown here to copy.</summary>
public sealed class InviteModel(KkDbContext db, InvitationService invitations, ICurrentUser current, ActivityLogger log, MailService mail) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib die E-Mail-Adresse ein.")]
        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "E-Mail-Adresse")]
        public string Email { get; set; } = "";

        [Display(Name = "Name (optional)")]
        public string? DisplayName { get; set; }

        [Display(Name = "Persönliche Nachricht (optional)")]
        public string? Note { get; set; }

        public bool Send { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)]
    public long CustomerId { get; set; }

    /// <summary>The invitation that was just made: the page then shows its link.</summary>
    [BindProperty(SupportsGet = true)]
    public long? Created { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Customer Customer { get; private set; } = null!;

    public Invitation? Result { get; private set; }

    public string? Link { get; private set; }

    public bool MailConfigured => mail.IsConfigured;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (Created is { } id)
        {
            Result = await db.Invitation.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.CustomerId == CustomerId);
            Link = Result is null ? null : invitations.LinkOf(Result);
        }
        else
        {
            Input.Email = Customer.Email ?? "";
            Input.DisplayName = string.IsNullOrWhiteSpace(Customer.PersonName) ? null : Customer.PersonName;
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

        var invitation = await invitations.CreateAsync(Input.Email, Input.DisplayName, UserRole.Client, CustomerId, Input.Note);
        await log.LogAsync("Portal-Einladung erstellt", "Customer", CustomerId, invitation.Email);
        db.HistoryEntry.Add(new HistoryEntry
        {
            CustomerId = CustomerId, Kind = HistoryKind.System, Title = "Einladung zum Portal erstellt", Text = invitation.Email,
            OccurredDate = DateTime.UtcNow, RefType = "Invitation", RefId = invitation.Id,
        });
        await db.SaveChangesAsync();

        if (Input.Send)
        {
            var result = await invitations.SendAsync(invitation, current.Name ?? "Dein Lektorat");
            if (result.Status == MailStatus.Failed)
            {
                FlashError("Die E-Mail konnte nicht gesendet werden: " + result.Error + " Du kannst den Link selbst weitergeben.");
            }
            else if (result.Status == MailStatus.Sent)
            {
                Flash("Die Einladung ist per E-Mail unterwegs.");
            }
        }

        return RedirectToPage(new { customerId = CustomerId, created = invitation.Id });
    }

    private async Task<bool> PrepareAsync()
    {
        var customer = await db.Customer.AsNoTracking().FirstOrDefaultAsync(c => c.Id == CustomerId);
        if (customer is null)
        {
            return false;
        }

        Customer = customer;
        Head("Zugang einladen", null, ("Kunden", "/Customers"), (customer.DisplayName, $"/Customers/Details?id={customer.Id}&tab=portal"), ("Zugang einladen", null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Customers";
        return true;
    }
}
