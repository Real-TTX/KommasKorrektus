using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Configuration;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Services.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Admin;

/// <summary>The mail server that invitations, codes, share links and documents are sent with. The password is kept protected with the key ring.</summary>
public sealed class MailModel(ConfigStore config, SecretProtector secrets, MailService mail, KkDbContext db, ICurrentUser current, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        public bool Enabled { get; set; }

        [Display(Name = "Server")]
        public string Host { get; set; } = "";

        [Range(1, 65535, ErrorMessage = "Der Port liegt zwischen 1 und 65535.")]
        [Display(Name = "Port")]
        public int Port { get; set; } = 587;

        [Display(Name = "Verschlüsselung")]
        public MailSecurity Security { get; set; } = MailSecurity.StartTls;

        [Display(Name = "Benutzername")]
        public string? UserName { get; set; }

        [Display(Name = "Passwort")]
        public string? Password { get; set; }

        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "Absenderadresse")]
        public string FromAddress { get; set; } = "";

        [Display(Name = "Absendername")]
        public string FromName { get; set; } = "KommaKorrektus";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool HasPassword => !string.IsNullOrEmpty(config.Current.Mail.PasswordProtected);

    public string? Test { get; private set; }

    public bool TestOk { get; private set; }

    public void OnGet()
    {
        Prepare();
        var m = config.Current.Mail;
        Input = new InputModel { Enabled = m.Enabled, Host = m.Host, Port = m.Port, Security = m.Security, UserName = m.UserName, FromAddress = m.FromAddress, FromName = m.FromName };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Prepare();
        Validate();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        config.Update(c =>
        {
            var m = c.Mail;
            m.Enabled = Input.Enabled;
            m.Host = Input.Host.Trim();
            m.Port = Input.Port;
            m.Security = Input.Security;
            m.UserName = Input.UserName?.Trim() ?? "";
            if (!string.IsNullOrEmpty(Input.Password))
            {
                m.PasswordProtected = secrets.Protect(Input.Password);
            }
            else if (string.IsNullOrEmpty(Input.UserName))
            {
                m.PasswordProtected = "";
            }

            m.FromAddress = Input.FromAddress.Trim();
            m.FromName = string.IsNullOrWhiteSpace(Input.FromName) ? "KommaKorrektus" : Input.FromName.Trim();
        });
        await log.LogAsync("E-Mail-Server geändert", "Mail", null, Input.Host);
        Flash("Die Einstellungen für E-Mail sind gespeichert.");
        return RedirectToPage();
    }

    /// <summary>Connects with what is in the form (not yet saved) and sends nothing.</summary>
    public async Task<IActionResult> OnPostTestAsync()
    {
        Prepare();
        if (string.IsNullOrWhiteSpace(Input.Host))
        {
            ModelState.AddModelError("Input.Host", "Bitte gib den Server ein, zu dem die Verbindung geprüft werden soll.");
            return Page();
        }

        var candidate = new MailConfig { Host = Input.Host.Trim(), Port = Input.Port, Security = Input.Security, UserName = Input.UserName?.Trim() ?? "", PasswordProtected = config.Current.Mail.PasswordProtected };
        var error = await mail.TestConnectionAsync(candidate, string.IsNullOrEmpty(Input.Password) ? null : Input.Password, HttpContext.RequestAborted);
        TestOk = error is null;
        Test = error is null ? $"Die Verbindung zu {candidate.Host}:{candidate.Port} klappt{(string.IsNullOrEmpty(candidate.UserName) ? "" : " und die Anmeldung auch")}." : "Die Verbindung klappt nicht: " + error;
        return Page();
    }

    /// <summary>Sends a short mail to the address of the signed-in administrator with the saved settings.</summary>
    public async Task<IActionResult> OnPostSendTestAsync()
    {
        var user = await db.User.AsNoTracking().FirstOrDefaultAsync(u => u.Id == current.UserId);
        if (user is null)
        {
            return RedirectToPage();
        }

        var result = await mail.SendAsync(user.Email, "Testnachricht von KommaKorrektus", "Wenn du das liest, funktioniert der Versand von E-Mails.\n\nDiese Nachricht kam von der Seite „E-Mail“ in der Verwaltung.", MailKind.Test, ct: HttpContext.RequestAborted);
        if (result.Delivered)
        {
            Flash($"Die Testnachricht ist an {user.Email} unterwegs.");
        }
        else
        {
            FlashError("Die Testnachricht konnte nicht gesendet werden: " + result.Error);
        }

        return RedirectToPage();
    }

    private void Validate()
    {
        if (!Input.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Input.Host))
        {
            ModelState.AddModelError("Input.Host", "Bitte gib den Server an, oder schalte den Versand aus.");
        }

        if (string.IsNullOrWhiteSpace(Input.FromAddress))
        {
            ModelState.AddModelError("Input.FromAddress", "Bitte gib die Adresse an, von der aus gesendet wird.");
        }
    }

    private void Prepare()
    {
        Head("E-Mail", "Der Server, über den Einladungen, Codes und Links gesendet werden.", ("Verwaltung", null), ("E-Mail", null));
        ViewData["Width"] = "narrow";
    }
}
