using System.Net;
using System.Text;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace KommasKorrektus.UiKit;

/// <summary>An entry of the user's context menu. A <see cref="Post"/> item changes something (sign out): a button of a small form, not a link.</summary>
public sealed record UserMenuItem(string Title, string Href, string Icon, bool Danger = false, bool SeparatorBefore = false, bool Post = false);

/// <summary>Everything the layout needs to draw the shell around a page: the menus, the user, the version.</summary>
public sealed class ShellModel
{
    public IReadOnlyList<NavSet> Menus { get; init; } = [];

    public bool HasSwitcher => Menus.Count > 1;

    public NavSet CurrentMenu(string? active) => Menus.FirstOrDefault(m => m.Contains(active)) ?? Menus[0];

    public string UserName { get; init; } = "";

    public string UserRole { get; init; } = "";

    public string UserLine { get; init; } = "";

    public IReadOnlyList<UserMenuItem> UserMenu { get; init; } = [];

    public string Version { get; init; } = "";

    public string Tagline { get; init; } = "Lektorat & Verwaltung";

    /// <summary>"light" or "dark" when the user chose one; null follows the system.</summary>
    public string? Theme { get; init; }

    public string HomeHref { get; init; } = "/";

    /// <summary>The optional attributes of &lt;html&gt;, written only when they have a value (an empty data-theme would count as a choice).</summary>
    public string RootAttributes => Theme is "light" or "dark" ? $"data-theme=\"{WebUtility.HtmlEncode(Theme)}\"" : string.Empty;
}

/// <summary>Counts shown as badges in the menu. Cached for a minute: a badge may be a little late, a page must not wait for four queries.</summary>
public sealed class NavCounts(IMemoryCache cache, KkDbContext db, TimeProvider clock)
{
    public sealed record Counts(int OverdueTasks, int OverdueInvoices, int DraftInvoices, int OpenOffers);

    public async Task<Counts> GetAsync()
    {
        return (await cache.GetOrCreateAsync("nav-counts", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var today = Fmt.Today(clock);
            return new Counts(
                await db.ProjectTask.CountAsync(t => t.State != TaskState.Done && t.DueDate != null && t.DueDate < today),
                await db.Invoice.CountAsync(i => i.Type == DocType.Invoice && (i.Status == DocStatus.Issued || i.Status == DocStatus.PartiallyPaid) && i.DueDate != null && i.DueDate < today),
                await db.Invoice.CountAsync(i => i.Status == DocStatus.Draft),
                await db.Invoice.CountAsync(i => i.Type == DocType.Offer && i.Status == DocStatus.Issued));
        }))!;
    }
}

/// <summary>Builds the shell for the person who is signed in: the menus depend on the role (the administration only for administrators, the portal only for clients).</summary>
public sealed class ShellFactory(ICurrentUser user, NavCounts counts, IHttpContextAccessor accessor)
{
    public async Task<ShellModel> BuildAsync()
    {
        var principal = accessor.HttpContext?.User;
        var name = user.Name ?? "Gast";
        var theme = principal?.FindFirst(AppClaims.Theme)?.Value;

        if (user.Role == UserRole.Client)
        {
            return new ShellModel
            {
                Menus = [PortalMenu()],
                UserName = name,
                UserRole = "Kundenzugang",
                UserLine = principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
                UserMenu = UserMenu(),
                Version = $"KommasKorrektus {AppVersion.Current}",
                Theme = theme,
                HomeHref = "/Portal",
                Tagline = "Kundenbereich",
            };
        }

        var menus = new List<NavSet> { await WorkMenuAsync() };
        if (user.IsAdmin)
        {
            menus.Add(AdminMenu());
        }

        return new ShellModel
        {
            Menus = menus,
            UserName = name,
            UserRole = user.IsAdmin ? "Administrator" : "Lektorat",
            UserLine = principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
            UserMenu = UserMenu(),
            Version = $"KommasKorrektus {AppVersion.Current}",
            Theme = theme,
        };
    }

    /// <summary>The shell of a page without a menu (sign-in, setup, a shared link).</summary>
    public static ShellModel Bare(string? theme = null) => new()
    {
        Menus = [new NavSet("none", "KommasKorrektus", "home", [])],
        Version = $"KommasKorrektus {AppVersion.Current}",
        Theme = theme,
    };

    private async Task<NavSet> WorkMenuAsync()
    {
        var c = await counts.GetAsync();
        return new NavSet("work", "Lektorat", "book",
        [
            new(null, [new("Start", "/", "home")]),
            new("Arbeit",
            [
                new("Kunden", "/Customers", "users"),
                new("Projekte", null, "folder", Children:
                [
                    new("Alle Projekte", "/Projects", null),
                    new("Aufgaben", "/Tasks", null, c.OverdueTasks > 0 ? NavBadge.Count(c.OverdueTasks, "warn", "überfällige Aufgaben") : null),
                    new("Zeiterfassung", "/Time", null),
                    new("Aufgabenvorlagen", "/TaskTemplates", null),
                ]),
                new("Rechnungen", null, "dollar", Children:
                [
                    new("Rechnungen", "/Invoices", null, c.OverdueInvoices > 0 ? NavBadge.Count(c.OverdueInvoices, "error", "überfällige Rechnungen") : null),
                    new("Angebote", "/Offers", null),
                    new("Produkte", "/Products", null),
                    new("Vorlagen", "/Templates", null),
                    new("Textbausteine", "/TextBlocks", null),
                ]),
            ]),
        ]);
    }

    private static NavSet AdminMenu() => new("admin", "Verwaltung", "settings",
    [
        new("Benutzer",
        [
            new("Benutzer", "/Admin/Users", "user"),
            new("Einladungen", "/Admin/Invitations", "mail"),
        ]),
        new("System",
        [
            new("Firma", "/Admin/Company", "briefcase"),
            new("E-Mail", "/Admin/Mail", "mail"),
            new("Speicher (NAS)", "/Admin/Storage", "hard-drive"),
            new("Backup", null, "archive", Children:
            [
                new("Übersicht", "/Admin/Backup", null),
                new("Pläne", "/Admin/Backup/Plans", null),
                new("Ziele", "/Admin/Backup/Targets", null),
                new("Wiederherstellung", "/Admin/Backup/Restore", null),
            ]),
            new("Sicherheit", "/Admin/Security", "shield"),
        ]),
        new("Protokolle",
        [
            new("Aktivität", "/Admin/Activity", "activity"),
            new("E-Mail-Protokoll", "/Admin/MailLog", "message-square"),
            new("System", "/Admin/System", "server"),
        ]),
    ]);

    private static NavSet PortalMenu() => new("portal", "Mein Bereich", "home",
    [
        new(null,
        [
            new("Meine Projekte", "/Portal", "folder"),
            new("Rechnungen & Angebote", "/Portal/Invoices", "dollar"),
        ]),
    ]);

    private static List<UserMenuItem> UserMenu() =>
    [
        new("Mein Konto", "/Account/Profile", "user"),
        new("Sicherheit", "/Account/Security", "shield"),
        new("Abmelden", "/Account/Logout", "logout", Danger: true, SeparatorBefore: true, Post: true),
    ];
}
