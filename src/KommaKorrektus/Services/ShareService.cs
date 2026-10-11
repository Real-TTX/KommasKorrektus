using System.Security.Cryptography;
using System.Text;
using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Security;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services;

public enum ShareState
{
    Active,
    Expired,
    Revoked,
    Unknown,
}

public sealed record ShareResolution(ShareState State, ShareLink? Link);

/// <summary>
/// Anonymous links to a project or one document. The visitor is "Anonymous": no account, only what the link allows, for as long as it is valid.
/// A link can have a password (checked once, remembered in a signed cookie for a few hours); wrong passwords are counted, so a link cannot be guessed at.
/// What a visitor does (look, download, comment, approve) is written to <see cref="ShareAccess"/> with a hash of the address, not the address.
/// </summary>
public sealed class ShareService(KkDbContext db, ConfigStore config, TimeProvider clock, IDataProtectionProvider protection, PublicUrl urls)
{
    public const int MaxWrongPasswords = 10;

    private static readonly TimeSpan GateLifetime = TimeSpan.FromHours(4);

    private readonly IDataProtector _gate = protection.CreateProtector("KommaKorrektus.ShareGate.v1");

    public string LinkOf(ShareLink link) => urls.Absolute($"/s/{link.Token}");

    public async Task<ShareLink> CreateAsync(Project project, long? documentId, int? versionNo, string title, string? message, int days, string? password, bool allowDownload, bool showChanges, bool allowComments, bool allowApproval, string? recipientName, string? recipientEmail, CancellationToken ct = default)
    {
        var link = new ShareLink
        {
            ProjectId = project.Id,
            DocumentId = documentId,
            VersionNo = versionNo,
            Title = title.Trim(),
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            ExpiresDate = clock.GetUtcNow().UtcDateTime.AddDays(Math.Clamp(days, 1, 365)),
            PasswordHash = string.IsNullOrEmpty(password) ? null : PasswordHasher.Hash(password),
            AllowDownload = allowDownload,
            ShowChanges = showChanges,
            AllowComments = allowComments,
            AllowApproval = allowApproval,
            RecipientName = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName.Trim(),
            RecipientEmail = string.IsNullOrWhiteSpace(recipientEmail) ? null : recipientEmail.Trim(),
        };
        db.ShareLink.Add(link);
        await db.SaveChangesAsync(ct);
        return link;
    }

    public async Task<ShareResolution> ResolveAsync(Guid token, CancellationToken ct = default)
    {
        var link = await db.ShareLink.Include(l => l.Project).ThenInclude(p => p!.Customer).Include(l => l.Document).FirstOrDefaultAsync(l => l.Token == token, ct);
        if (link?.Project is null || (link.DocumentId is not null && link.Document is null))
        {
            return new ShareResolution(ShareState.Unknown, null);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var state = link.RevokedDate is not null ? ShareState.Revoked : link.ExpiresDate <= now ? ShareState.Expired : ShareState.Active;
        return new ShareResolution(state, link);
    }

    // ------------------------------------------------------------------------------------------------ the password

    public bool NeedsPassword(ShareLink link, HttpContext http) => link.PasswordHash is not null && !HasPassedGate(link, http);

    public bool HasPassedGate(ShareLink link, HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName(link), out var value))
        {
            return false;
        }

        try
        {
            var parts = _gate.Unprotect(value).Split('|');
            return parts.Length == 2 && parts[0] == link.Token.ToString() && long.TryParse(parts[1], out var until) && until > clock.GetUtcNow().ToUnixTimeSeconds();
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public async Task<bool> TryPasswordAsync(ShareLink link, string? password, HttpContext http, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddMinutes(-15);
        var wrong = await db.ShareAccess.CountAsync(a => a.ShareLinkId == link.Id && a.Action == ShareAction.WrongPassword && a.AccessDate > since, ct);
        if (wrong >= MaxWrongPasswords)
        {
            return false;
        }

        if (!PasswordHasher.Verify(password ?? string.Empty, link.PasswordHash))
        {
            await LogAsync(link, ShareAction.WrongPassword, http, null, ct);
            return false;
        }

        var until = clock.GetUtcNow().Add(GateLifetime).ToUnixTimeSeconds();
        http.Response.Cookies.Append(CookieName(link), _gate.Protect($"{link.Token}|{until}"), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = http.Request.IsHttps,
            Path = "/s/" + link.Token,
            MaxAge = GateLifetime,
        });
        return true;
    }

    public async Task<bool> IsLockedAsync(ShareLink link, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().UtcDateTime.AddMinutes(-15);
        return await db.ShareAccess.CountAsync(a => a.ShareLinkId == link.Id && a.Action == ShareAction.WrongPassword && a.AccessDate > since, ct) >= MaxWrongPasswords;
    }

    private static string CookieName(ShareLink link) => ".kk.share." + link.Token.ToString("N")[..12];

    // ------------------------------------------------------------------------------------------------ what visitors do

    public async Task LogAsync(ShareLink link, ShareAction action, HttpContext http, string? detail = null, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        db.ShareAccess.Add(new ShareAccess
        {
            ShareLinkId = link.Id,
            AccessDate = now,
            Action = action,
            ClientHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(link.Token + "|" + http.Connection.RemoteIpAddress))).ToLowerInvariant()[..16],
            UserAgent = http.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? (agent.Length > 200 ? agent[..200] : agent) : null,
            Detail = detail is { Length: > 300 } ? detail[..300] : detail,
        });
        if (action == ShareAction.View)
        {
            link.ViewCount++;
            link.LastViewDate = now;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The version a link shows of a document: the one fixed in the link, else the newest.</summary>
    public static int VersionFor(ShareLink link, Document document) => link.VersionNo is { } fixedNo && fixedNo <= document.CurrentVersionNo ? fixedNo : document.CurrentVersionNo;

    /// <summary>The documents a link covers: the one document, or every document of the project.</summary>
    public async Task<List<Document>> DocumentsAsync(ShareLink link, CancellationToken ct = default)
        => link.DocumentId is { } id
            ? await db.Document.AsNoTracking().Where(d => d.Id == id).ToListAsync(ct)
            : await db.Document.AsNoTracking().Where(d => d.ProjectId == link.ProjectId).OrderBy(d => d.Title).ToListAsync(ct);

    public string CompanyName => config.Current.Company.Name;
}
