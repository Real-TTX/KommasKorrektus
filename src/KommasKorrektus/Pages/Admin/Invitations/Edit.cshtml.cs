using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Auth;
using KommasKorrektus.Services.Mail;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Admin.Invitations;

/// <summary>
/// A new invitation (for a person who works here, or a client of the portal), or the invitation that exists: its link to copy, to send again, to take back.
/// </summary>
public sealed class EditModel(KkDbContext db, InvitationService invitations, UserService users, ICurrentUser current, ActivityLogger log, MailService mail, TimeProvider clock) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib die E-Mail-Adresse ein.")]
        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "E-Mail-Adresse")]
        public string Email { get; set; } = "";

        [Display(Name = "Name (optional)")]
        public string? DisplayName { get; set; }

        [Display(Name = "Rolle")]
        public UserRole Role { get; set; } = UserRole.User;

        [Display(Name = "Kunde")]
        public long? CustomerId { get; set; }

        [Display(Name = "Persönliche Nachricht (optional)")]
        public string? Note { get; set; }

        public bool Send { get; set; } = true;
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Invitation? Invitation { get; private set; }

    public string? Link { get; private set; }

    public List<SelectListItem> Customers { get; private set; } = [];

    public bool MailConfigured => mail.IsConfigured;

    public bool IsOpen => Invitation is { } i && i.AcceptedDate is null && i.RevokedDate is null && i.ExpiresDate > clock.GetUtcNow().UtcDateTime;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Id is not null || !await PrepareAsync())
        {
            return NotFound();
        }

        var email = SignInService.NormalizeEmail(Input.Email);
        if (Input.Role == UserRole.Client && Input.CustomerId is null)
        {
            ModelState.AddModelError("Input.CustomerId", "Bitte wähle den Kunden, dessen Projekte die Person sehen soll.");
        }

        if (ModelState.IsValid && Input.Role != UserRole.Client && await users.EmailTakenAsync(email))
        {
            ModelState.AddModelError("Input.Email", "Zu dieser E-Mail-Adresse gibt es schon ein Konto.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var invitation = await invitations.CreateAsync(email, Input.DisplayName, Input.Role, Input.CustomerId, Input.Note);
        await log.LogAsync("Einladung erstellt", "Invitation", invitation.Id, $"{invitation.Email} ({Labels.Of(invitation.Role)})");
        if (Input.Send)
        {
            ReportMail(await invitations.SendAsync(invitation, current.Name ?? "Dein Lektorat"));
        }

        return RedirectToPage(new { id = invitation.Id });
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        var invitation = await db.Invitation.FirstOrDefaultAsync(i => i.Id == Id);
        if (invitation is null)
        {
            return NotFound();
        }

        ReportMail(await invitations.SendAsync(invitation, current.Name ?? "Dein Lektorat"));
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostRevokeAsync()
    {
        var invitation = await db.Invitation.FirstOrDefaultAsync(i => i.Id == Id);
        if (invitation is null)
        {
            return NotFound();
        }

        await invitations.RevokeAsync(invitation);
        await log.LogAsync("Einladung zurückgezogen", "Invitation", invitation.Id, invitation.Email);
        Flash("Die Einladung ist zurückgezogen. Der Link funktioniert nicht mehr.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var invitation = await db.Invitation.FirstOrDefaultAsync(i => i.Id == Id);
        if (invitation is not null)
        {
            db.Invitation.Remove(invitation);
            await db.SaveChangesAsync();
            Flash("Die Einladung ist gelöscht.");
        }

        return RedirectToPage("/Admin/Invitations/Index");
    }

    private void ReportMail(MailResult result)
    {
        if (result.Status == MailStatus.Failed)
        {
            FlashError("Die E-Mail konnte nicht gesendet werden: " + result.Error + " Du kannst den Link selbst weitergeben.");
        }
        else if (result.Status == MailStatus.Sent)
        {
            Flash("Die Einladung ist per E-Mail unterwegs.");
        }
    }

    private async Task<bool> PrepareAsync()
    {
        if (Id is { } id)
        {
            Invitation = await db.Invitation.AsNoTracking().Include(i => i.Customer).FirstOrDefaultAsync(i => i.Id == id);
            if (Invitation is null)
            {
                return false;
            }

            Link = invitations.LinkOf(Invitation);
        }
        else
        {
            Customers = await db.Customer.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Company ?? c.LastName).ThenBy(c => c.FirstName)
                .Select(c => new SelectListItem((c.Company ?? ((c.FirstName ?? "") + " " + c.LastName).Trim()) + " (" + c.Number + ")", c.Id.ToString())).ToListAsync();
        }

        var title = Invitation is null ? "Neue Einladung" : "Einladung für " + Invitation.Email;
        Head(title, null, ("Verwaltung", null), ("Einladungen", "/Admin/Invitations"), (Invitation is null ? "Neue Einladung" : Invitation.Email, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Invitations";
        return true;
    }
}
