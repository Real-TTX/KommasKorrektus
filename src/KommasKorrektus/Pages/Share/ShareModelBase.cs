using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;
using Microsoft.AspNetCore.Mvc;

namespace KommasKorrektus.Pages.Share;

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

    /// <summary>The link is for one account and nobody is signed in: the page asks for the sign-in (and comes back here).</summary>
    public bool NeedsSignIn { get; private set; }

    /// <summary>True for somebody who works here and looks at the link: the visit is not counted as the visit of the customer.</summary>
    /// <remarks>On a link released to an account the account itself is the visitor, even when it is a staff account.</remarks>
    public bool IsStaffPreview => current.IsAuthenticated && current.Role is UserRole.Admin or UserRole.User && (Link?.UserId is null || Link.UserId != current.UserId);

    public bool CanShow => State == ShareState.Active && !NeedsPassword && !NeedsSignIn;

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

        // A link for an account: the sign-in is the gate. Somebody else who is signed in is told "not found" (it is not said whom the link is for);
        // the people of the house may look, as they may look at any link.
        if (Link.UserId is { } owner)
        {
            if (!current.IsAuthenticated)
            {
                NeedsSignIn = true;
                return false;
            }

            if (current.UserId != owner && current.Role is not (UserRole.Admin or UserRole.User))
            {
                State = ShareState.Unknown;
                Link = null!;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return false;
            }

            ViewData["Title"] = Link.Title;
            return true;
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
