using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Account;

/// <summary>
/// Accepting an invitation. A new person registers (a password, or none and e-mail codes from then on); somebody who has an account already signs in
/// first and then accepts, which only adds the access to the customer.
/// </summary>
[AllowAnonymous]
public sealed class RegisterModel(InvitationService invitations, UserService users, SignInService signIn, KkDbContext db, ICurrentUser current, ActivityLogger log, LoginCodeService codes) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib deinen Namen ein.")]
        [Display(Name = "Dein Name")]
        public string DisplayName { get; set; } = "";

        [Display(Name = "Passwort")]
        public string? Password { get; set; }

        [Display(Name = "Passwort wiederholen")]
        public string? PasswordRepeat { get; set; }

        /// <summary>"password" or "code".</summary>
        public string Method { get; set; } = "password";
    }

    [BindProperty(SupportsGet = true)]
    public Guid Token { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public InvitationCheck Check { get; private set; } = new(InvitationState.Unknown, null);

    /// <summary>Signing in with a code from an e-mail needs a mail server; without one a person without a password could never get in.</summary>
    public bool CodeAvailable => codes.IsAvailable;

    public bool ExistingAccount { get; private set; }

    public bool SignedInAsInvitee { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        Input.DisplayName = Check.Invitation?.DisplayName ?? "";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Check.State != InvitationState.Valid)
        {
            return Page();
        }

        var invitation = Check.Invitation!;
        if (ExistingAccount)
        {
            // Somebody with an account has to prove it by being signed in as that account.
            if (!SignedInAsInvitee)
            {
                return RedirectToPage("/Account/Login", new { returnUrl = $"/Account/Register?token={Token}" });
            }

            var existing = await db.User.FirstAsync(u => u.Id == current.UserId);
            await invitations.GrantAsync(invitation, existing);
            Flash("Der Zugriff ist freigegeben.");
            return RedirectToPage(existing.Role == UserRole.Client ? "/Portal/Index" : "/Index");
        }

        var usePassword = Input.Method != "code" || !CodeAvailable;
        if (usePassword)
        {
            if (UserService.CheckPassword(Input.Password, invitation.Email) is { } problem)
            {
                ModelState.AddModelError("Input.Password", problem);
            }
            else if (Input.Password != Input.PasswordRepeat)
            {
                ModelState.AddModelError("Input.PasswordRepeat", "Die Passwörter stimmen nicht überein.");
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await invitations.AcceptAsync(invitation, Input.DisplayName, usePassword ? Input.Password : null);
        await signIn.StartSessionAsync(HttpContext, user);
        await log.LogAsync("Einladung angenommen", "User", user.Id, user.Email);
        Flash("Willkommen bei KommasKorrektus!");
        return RedirectToPage(user.Role == UserRole.Client ? "/Portal/Index" : "/Index");
    }

    private async Task LoadAsync()
    {
        ViewData["Title"] = "Einladung";
        Check = await invitations.CheckAsync(Token);
        if (Check.Invitation is { } invitation)
        {
            ExistingAccount = await users.EmailTakenAsync(invitation.Email);
            SignedInAsInvitee = current.IsAuthenticated && ExistingAccount
                && await db.User.AnyAsync(u => u.Id == current.UserId && u.Email == invitation.Email);
        }
    }
}
