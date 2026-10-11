using KommaKorrektus.Data.Entities;
using KommaKorrektus.Services;
using Microsoft.AspNetCore.Mvc;

namespace KommaKorrektus.Pages.Share;

/// <summary>
/// The common start of every page behind a share link: is the link there, valid, past the password? What it cannot show it explains in words
/// (a link that has run out is no secret), and it never says more than that: no title, no customer.
/// </summary>
public abstract class ShareModelBase(ShareService shares, ICurrentUser current) : PageModelBase
{
    [BindProperty(SupportsGet = true)]
    public Guid Token { get; set; }

    public ShareLink Link { get; private set; } = null!;

    public ShareState State { get; private set; } = ShareState.Unknown;

    public bool NeedsPassword { get; private set; }

    public bool Locked { get; private set; }

    /// <summary>True for somebody who works here and looks at the link: the visit is not counted as the visit of the customer.</summary>
    public bool IsStaffPreview => current.IsAuthenticated && current.Role is UserRole.Admin or UserRole.User;

    public bool CanShow => State == ShareState.Active && !NeedsPassword;

    public string CompanyName => shares.CompanyName;

    /// <summary>Resolves the link and the gate. Returns false when the page must not show content (the view then shows why).</summary>
    protected async Task<bool> InitAsync()
    {
        ViewData["Wide"] = true;
        ViewData["Title"] = "Freigabe";
        var resolution = await shares.ResolveAsync(Token, HttpContext.RequestAborted);
        State = resolution.State;
        if (resolution.Link is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return false;
        }

        Link = resolution.Link;
        if (State != ShareState.Active)
        {
            Response.StatusCode = StatusCodes.Status410Gone;
            return false;
        }

        NeedsPassword = shares.NeedsPassword(Link, HttpContext);
        Locked = NeedsPassword && await shares.IsLockedAsync(Link, HttpContext.RequestAborted);
        ViewData["Title"] = Link.Title;
        return !NeedsPassword;
    }

    protected Task CountViewAsync() => IsStaffPreview ? Task.CompletedTask : shares.LogAsync(Link, ShareAction.View, HttpContext, null, HttpContext.RequestAborted);

    public static string Remaining(ShareLink link, DateTime now)
    {
        var span = link.ExpiresDate - now;
        return span.TotalDays >= 2 ? $"noch {(int)span.TotalDays} Tage" : span.TotalHours >= 1 ? $"noch {(int)span.TotalHours} Stunden" : "läuft bald ab";
    }
}
