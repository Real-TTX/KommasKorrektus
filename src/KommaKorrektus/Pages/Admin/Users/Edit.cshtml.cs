using System.ComponentModel.DataAnnotations;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using KommaKorrektus.Services.Auth;
using KommaKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Pages.Admin.Users;

/// <summary>One account: name, address, role, whether it may sign in, a new password, the second factor, the sessions, and (for a client) the customers it can see.</summary>
public sealed class EditModel(KkDbContext db, UserService users, SignInService signIn, TwoFactorService twoFactor, SessionCache cache, ICurrentUser current, ActivityLogger log, TimeProvider clock) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib den Namen ein.")]
        [Display(Name = "Name")]
        public string DisplayName { get; set; } = "";

        [Required(ErrorMessage = "Bitte gib die E-Mail-Adresse ein.")]
        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "E-Mail-Adresse")]
        public string Email { get; set; } = "";

        [Display(Name = "Telefon")]
        public string? Phone { get; set; }

        [Display(Name = "Rolle")]
        public UserRole Role { get; set; } = UserRole.User;

        public bool IsActive { get; set; } = true;

        [Display(Name = "Passwort")]
        public string? Password { get; set; }
    }

    public sealed record CustomerAccess(long Id, string Name, string Number);

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public User? Account { get; private set; }

    public bool IsMe => Account is not null && Account.Id == current.UserId;

    public bool HasTotp => Account?.Totp is { IsConfirmed: true };

    public bool IsLocked => Account is { } a && a.LockoutEndDate > clock.GetUtcNow().UtcDateTime;

    public int Sessions { get; private set; }

    public List<CustomerAccess> Customers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (Account is { } a)
        {
            Input = new InputModel { DisplayName = a.DisplayName, Email = a.Email, Phone = a.Phone, Role = a.Role, IsActive = a.IsActive };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var email = SignInService.NormalizeEmail(Input.Email);
        if (ModelState.IsValid && await users.EmailTakenAsync(email, Id))
        {
            ModelState.AddModelError("Input.Email", "Zu dieser E-Mail-Adresse gibt es schon ein Konto.");
        }

        if (!string.IsNullOrEmpty(Input.Password) && UserService.CheckPassword(Input.Password, email) is { } weak)
        {
            ModelState.AddModelError("Input.Password", weak);
        }

        if (Id is null && string.IsNullOrEmpty(Input.Password))
        {
            ModelState.AddModelError("Input.Password", "Bitte vergib ein Passwort. Oder lade die Person ein, dann wählt sie es selbst.");
        }

        if (Account is { Role: UserRole.Admin } && (Input.Role != UserRole.Admin || !Input.IsActive) && !await OtherAdminExistsAsync())
        {
            ModelState.AddModelError("Input.Role", "Das ist der einzige aktive Administrator. Es muss immer einen geben.");
        }

        if (IsMe && (!Input.IsActive || Input.Role != UserRole.Admin))
        {
            ModelState.AddModelError("Input.Role", "Du kannst dir nicht selbst die Rechte nehmen oder dich sperren.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Account is null)
        {
            var created = await users.CreateAsync(email, Input.DisplayName.Trim(), Input.Role, Input.Password);
            created.Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim();
            created.IsActive = Input.IsActive;
            await db.SaveChangesAsync();
            await log.LogAsync("Benutzer angelegt", "User", created.Id, $"{created.Email} ({Labels.Of(created.Role)})");
            Flash("Der Benutzer ist angelegt.");
            return RedirectToPage("/Admin/Users/Edit", new { id = created.Id });
        }

        var user = await db.User.FirstAsync(u => u.Id == Account.Id);
        var roleChanged = user.Role != Input.Role;
        user.DisplayName = Input.DisplayName.Trim();
        user.Email = email;
        user.Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim();
        user.Role = Input.Role;
        await db.SaveChangesAsync();
        if (user.IsActive != Input.IsActive)
        {
            await users.SetActiveAsync(user, Input.IsActive);
        }

        if (!string.IsNullOrEmpty(Input.Password))
        {
            await users.ChangePasswordAsync(user, Input.Password);
        }

        cache.InvalidateUser(user.Id);
        await log.LogAsync("Benutzer geändert", "User", user.Id, roleChanged ? $"{user.Email}, neue Rolle: {Labels.Of(user.Role)}" : user.Email);
        Flash("Der Benutzer ist gespeichert.");
        return RedirectToPage("/Admin/Users/Edit", new { id = user.Id });
    }

    public async Task<IActionResult> OnPostResetTotpAsync()
    {
        var user = await db.User.Include(u => u.Totp).FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        await twoFactor.DisableAsync(user);
        await log.LogAsync("Zwei-Faktor-Anmeldung zurückgesetzt", "User", user.Id, user.Email);
        Flash("Die Zwei-Faktor-Anmeldung ist abgeschaltet. Die Person kann sie neu einrichten.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostEndSessionsAsync()
    {
        if (Id is not { } id)
        {
            return NotFound();
        }

        // An administrator who ends their own sessions would lock themselves out in the middle of the click; keep the one in use.
        var keep = id == current.UserId ? Guid.TryParse(User.FindFirst(AppClaims.Session)?.Value, out var token) ? token : (Guid?)null : null;
        await signIn.EndAllSessionsAsync(id, keep);
        await log.LogAsync("Sitzungen beendet", "User", id);
        Flash("Alle Sitzungen sind beendet. Die Person muss sich neu anmelden.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUnlockAsync()
    {
        var user = await db.User.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return NotFound();
        }

        user.FailedLogins = 0;
        user.LockoutEndDate = null;
        await db.SaveChangesAsync();
        cache.InvalidateUser(user.Id);
        Flash("Die Sperre nach falschen Anmeldungen ist aufgehoben.");
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var user = await db.User.FirstOrDefaultAsync(u => u.Id == Id);
        if (user is null)
        {
            return RedirectToPage("/Admin/Users/Index");
        }

        if (user.Id == current.UserId)
        {
            FlashError("Du kannst dich nicht selbst löschen.");
            return RedirectToPage(new { id = Id });
        }

        if (user.Role == UserRole.Admin && user.IsActive && !await db.User.AnyAsync(u => u.Id != user.Id && u.Role == UserRole.Admin && u.IsActive))
        {
            FlashError("Das ist der einzige aktive Administrator.");
            return RedirectToPage(new { id = Id });
        }

        await signIn.EndAllSessionsAsync(user.Id, null);
        db.User.Remove(user);
        await db.SaveChangesAsync();
        cache.InvalidateUser(user.Id);
        await log.LogAsync("Benutzer gelöscht", "User", user.Id, user.Email);
        Flash("Der Benutzer ist gelöscht. Seine Einträge bleiben erhalten.");
        return RedirectToPage("/Admin/Users/Index");
    }

    private Task<bool> OtherAdminExistsAsync() => db.User.AnyAsync(u => u.Id != Id && u.Role == UserRole.Admin && u.IsActive);

    private async Task<bool> LoadAsync()
    {
        if (Id is { } id)
        {
            Account = await db.User.AsNoTracking().Include(u => u.Totp).FirstOrDefaultAsync(u => u.Id == id);
            if (Account is null)
            {
                return false;
            }

            var now = clock.GetUtcNow().UtcDateTime;
            Sessions = await db.UserSession.CountAsync(s => s.UserId == id && s.RevokedDate == null && s.ExpiresDate > now);
            Customers = await db.UserCustomer.AsNoTracking().Where(c => c.UserId == id)
                .Select(c => new CustomerAccess(c.CustomerId, c.Customer!.Company ?? ((c.Customer.FirstName ?? "") + " " + c.Customer.LastName).Trim(), c.Customer.Number)).ToListAsync();
        }

        var title = Id is null ? "Neuer Benutzer" : Account!.DisplayName;
        Head(title, null, ("Verwaltung", null), ("Benutzer", "/Admin/Users"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Admin/Users";
        return true;
    }
}
