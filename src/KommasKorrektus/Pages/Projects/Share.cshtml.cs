using System.ComponentModel.DataAnnotations;
using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Mail;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Projects;

/// <summary>A link to a project or one document, anonymous or released to one account: made here, shown here with its link to copy, its use, and the way to take it back.</summary>
public sealed class ShareModel(KkDbContext db, ShareService shares, MailService mail, ConfigStore config, ICurrentUser current, TimeProvider clock, ActivityLogger log, ProjectService projects) : PageModelBase
{
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Bitte gib dem Link einen Titel.")]
        [Display(Name = "Titel")]
        public string Title { get; set; } = "";

        [Display(Name = "Dokument")]
        public long? DocumentId { get; set; }

        [Display(Name = "Fassung")]
        public int? VersionNo { get; set; }

        [Display(Name = "Nachricht an die Empfängerin oder den Empfänger")]
        public string? Message { get; set; }

        [Range(1, 365, ErrorMessage = "Der Link gilt zwischen 1 und 365 Tagen.")]
        [Display(Name = "Gültig für Tage")]
        public int Days { get; set; } = 14;

        [Display(Name = "Konto")]
        public long? UserId { get; set; }

        [Display(Name = "Passwort")]
        public string? Password { get; set; }

        public bool AllowDownload { get; set; } = true;

        public bool ShowChanges { get; set; } = true;

        public bool AllowComments { get; set; }

        public bool AllowApproval { get; set; }

        [Display(Name = "Name")]
        public string? RecipientName { get; set; }

        [EmailAddress(ErrorMessage = "Das ist keine gültige E-Mail-Adresse.")]
        [Display(Name = "E-Mail-Adresse")]
        public string? RecipientEmail { get; set; }
    }

    [BindProperty(SupportsGet = true)]
    public long ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public Project Project { get; private set; } = null!;

    public ShareLink? Link { get; private set; }

    public string? LinkUrl { get; private set; }

    public List<ShareAccess> Accesses { get; private set; } = [];

    public List<SelectListItem> Documents { get; private set; } = [];

    /// <summary>The accounts a release can be made to: the staff, and the clients of this project's customer.</summary>
    public List<SelectListItem> Accounts { get; private set; } = [];

    /// <summary>The names of the accounts that used the link.</summary>
    public Dictionary<long, string> VisitorNames { get; private set; } = [];

    public bool MailConfigured => mail.IsConfigured;

    public bool IsActive => Link is { } l && l.IsActive(clock.GetUtcNow().UtcDateTime);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        if (Link is { } link)
        {
            Input = new InputModel
            {
                Title = link.Title, DocumentId = link.DocumentId, VersionNo = link.VersionNo, Message = link.Message, Days = Math.Max(1, (int)Math.Ceiling((link.ExpiresDate - clock.GetUtcNow().UtcDateTime).TotalDays)),
                AllowDownload = link.AllowDownload, ShowChanges = link.ShowChanges, AllowComments = link.AllowComments, AllowApproval = link.AllowApproval, UserId = link.UserId,
                RecipientName = link.RecipientName, RecipientEmail = link.RecipientEmail,
            };
        }
        else
        {
            Input.Title = $"Lektorat „{Project.Title}“";
            Input.Days = config.Current.Security.ShareLinkDays;
            Input.RecipientName = string.IsNullOrWhiteSpace(Project.Customer?.PersonName) ? null : Project.Customer!.PersonName;
            Input.RecipientEmail = Project.Customer?.Email;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await PrepareAsync())
        {
            return NotFound();
        }

        var account = Input.UserId is { } wanted ? await db.User.AsNoTracking().FirstOrDefaultAsync(u => u.Id == wanted) : null;
        if (Input.UserId is not null && (account is null || Accounts.All(a => a.Value != account.Id.ToString())))
        {
            ModelState.AddModelError("Input.UserId", "Dieses Konto kann keine Freigabe bekommen.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // A release to an account is addressed to that account: the name and the address for the mail follow it unless somebody wrote others.
        if (account is not null)
        {
            Input.RecipientName = string.IsNullOrWhiteSpace(Input.RecipientName) ? account.DisplayName : Input.RecipientName;
            Input.RecipientEmail = string.IsNullOrWhiteSpace(Input.RecipientEmail) ? account.Email : Input.RecipientEmail;
        }

        if (Link is { } existing)
        {
            var link = await db.ShareLink.FirstAsync(l => l.Id == existing.Id);
            link.Title = Input.Title.Trim();
            link.DocumentId = Input.DocumentId;
            link.VersionNo = Input.DocumentId is null ? null : Input.VersionNo;
            link.Message = string.IsNullOrWhiteSpace(Input.Message) ? null : Input.Message.Trim();
            link.ExpiresDate = clock.GetUtcNow().UtcDateTime.AddDays(Input.Days);
            link.AllowDownload = Input.AllowDownload;
            link.ShowChanges = Input.ShowChanges;
            link.AllowComments = Input.AllowComments;
            link.AllowApproval = Input.AllowApproval;
            link.RecipientName = string.IsNullOrWhiteSpace(Input.RecipientName) ? null : Input.RecipientName.Trim();
            link.RecipientEmail = string.IsNullOrWhiteSpace(Input.RecipientEmail) ? null : Input.RecipientEmail.Trim();
            link.UserId = Input.UserId;
            if (Input.UserId is not null)
            {
                link.PasswordHash = null;
            }
            else if (!string.IsNullOrEmpty(Input.Password))
            {
                link.PasswordHash = Core.Security.PasswordHasher.Hash(Input.Password);
            }

            link.RevokedDate = null;
            await db.SaveChangesAsync();
            Flash("Der Link ist gespeichert und gilt wieder ab jetzt.");
            return RedirectToPage(new { projectId = ProjectId, id = link.Id });
        }

        var created = await shares.CreateAsync(Project, Input.DocumentId, Input.DocumentId is null ? null : Input.VersionNo, Input.Title, Input.Message, Input.Days, Input.Password,
            Input.AllowDownload, Input.ShowChanges, Input.AllowComments, Input.AllowApproval, Input.RecipientName, Input.RecipientEmail, Input.UserId);
        projects.AddHistory(Project.CustomerId, $"Link „{created.Title}“ erstellt", $"gültig bis {Fmt.Date(created.ExpiresDate)}", Project.Id, "ShareLink", created.Id);
        await db.SaveChangesAsync();
        await log.LogAsync(created.UserId is null ? "Freigabe-Link erstellt" : "Freigabe an ein Konto erstellt", "ShareLink", created.Id, created.Title);
        Flash("Der Link ist erstellt.");
        return RedirectToPage(new { projectId = ProjectId, id = created.Id });
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        var link = await db.ShareLink.FirstOrDefaultAsync(l => l.Id == Id && l.ProjectId == ProjectId);
        if (link is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(link.RecipientEmail))
        {
            FlashError("Trage zuerst die E-Mail-Adresse der Empfängerin oder des Empfängers ein.");
            return RedirectToPage(new { projectId = ProjectId, id = link.Id });
        }

        var company = config.Current.Company;
        var text = $"Hallo{(link.RecipientName is null ? "" : " " + link.RecipientName)},\n\n" +
                   (string.IsNullOrWhiteSpace(link.Message) ? $"hier ist der Link zu „{link.Title}“." : link.Message) +
                   $"\n\n{shares.LinkOf(link)}\n\nDer Link ist bis {Fmt.Date(link.ExpiresDate)} gültig." +
                   (link.UserId is not null ? " Melde dich mit deinem Konto an, dann findest du es auch unter „Mit mir geteilt“." : link.PasswordHash is null ? "" : " Das Passwort bekommst du von mir getrennt.") +
                   $"\n\nHerzliche Grüße\n{current.Name}{(string.IsNullOrWhiteSpace(company.Name) ? "" : "\n" + company.Name)}";
        var result = await mail.SendAsync(link.RecipientEmail, link.Title, text, MailKind.ShareLink);
        if (result.Delivered)
        {
            link.SentDate = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
            Flash($"Der Link ist per E-Mail an {link.RecipientEmail} unterwegs.");
        }
        else
        {
            FlashError(result.Status == MailStatus.NotConfigured ? "Es ist noch kein E-Mail-Server eingerichtet. Kopiere den Link und schicke ihn selbst." : "Die E-Mail konnte nicht gesendet werden: " + result.Error);
        }

        return RedirectToPage(new { projectId = ProjectId, id = link.Id });
    }

    public async Task<IActionResult> OnPostRevokeAsync()
    {
        var link = await db.ShareLink.FirstOrDefaultAsync(l => l.Id == Id && l.ProjectId == ProjectId);
        if (link is null)
        {
            return NotFound();
        }

        link.RevokedDate = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();
        await log.LogAsync("Freigabe-Link zurückgezogen", "ShareLink", link.Id, link.Title);
        Flash("Der Link ist zurückgezogen: Wer ihn hat, sieht nichts mehr.");
        return RedirectToPage(new { projectId = ProjectId, id = link.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var link = await db.ShareLink.FirstOrDefaultAsync(l => l.Id == Id && l.ProjectId == ProjectId);
        if (link is not null)
        {
            db.ShareLink.Remove(link);
            await db.SaveChangesAsync();
            Flash("Der Link ist gelöscht.");
        }

        return RedirectToPage("/Projects/Details", new { id = ProjectId, tab = "shares" });
    }

    private async Task<bool> PrepareAsync()
    {
        var project = await db.Project.AsNoTracking().Include(p => p.Customer).FirstOrDefaultAsync(p => p.Id == ProjectId);
        if (project is null)
        {
            return false;
        }

        Project = project;
        if (Id is { } id)
        {
            Link = await db.ShareLink.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && l.ProjectId == ProjectId);
            if (Link is null)
            {
                return false;
            }

            LinkUrl = shares.LinkOf(Link);
            Accesses = await db.ShareAccess.AsNoTracking().Where(a => a.ShareLinkId == id).OrderByDescending(a => a.AccessDate).Take(40).ToListAsync();
            var visitors = Accesses.Where(a => a.UserId is not null).Select(a => a.UserId!.Value).Distinct().ToList();
            VisitorNames = await db.User.AsNoTracking().Where(u => visitors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        }

        var title = Link is null ? "Neuer Link" : "Freigabe-Link";
        Head(title, null, ("Projekte", "/Projects"), (project.Title, $"/Projects/Details?id={project.Id}&tab=shares"), (title, null));
        ViewData["Width"] = "narrow";
        ViewData["ActiveHref"] = "/Projects";
        Documents = (await db.Document.AsNoTracking().Where(d => d.ProjectId == ProjectId).OrderBy(d => d.Title).ToListAsync())
            .Select(d => new SelectListItem($"{d.Title} (Fassung {d.CurrentVersionNo})", d.Id.ToString())).ToList();
        var ofCustomer = db.UserCustomer.Where(uc => uc.CustomerId == project.CustomerId).Select(uc => uc.UserId);
        Accounts = (await db.User.AsNoTracking().Where(u => u.IsActive && (u.Role != UserRole.Client || ofCustomer.Contains(u.Id))).OrderBy(u => u.DisplayName).ToListAsync())
            .Select(u => new SelectListItem($"{u.DisplayName} · {Labels.Of(u.Role)}", u.Id.ToString())).ToList();
        return true;
    }
}
