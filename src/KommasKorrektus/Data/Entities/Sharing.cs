namespace KommasKorrektus.Data.Entities;

/// <summary>
/// An anonymous link to a project, or to one document (and one version of it): valid for a limited time, optionally with a password.
/// The visitor is the "Anonymous" role: no account, only what the link allows.
/// </summary>
public class ShareLink : AuditedEntity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>Null: the link shows the project and everything that was released in it.</summary>
    public long? DocumentId { get; set; }

    public Document? Document { get; set; }

    /// <summary>The version that is shown; null follows the newest.</summary>
    public int? VersionNo { get; set; }

    public string Title { get; set; } = "";

    /// <summary>A message to the visitor, shown on the page.</summary>
    public string? Message { get; set; }

    public DateTime ExpiresDate { get; set; }

    public string? PasswordHash { get; set; }

    public bool AllowDownload { get; set; } = true;

    /// <summary>Show the changes (old / new) and not only the clean text.</summary>
    public bool ShowChanges { get; set; } = true;

    public bool AllowComments { get; set; }

    /// <summary>The visitor can say "approved": the changes are accepted.</summary>
    public bool AllowApproval { get; set; }

    public string? RecipientName { get; set; }

    public string? RecipientEmail { get; set; }

    public DateTime? SentDate { get; set; }

    public DateTime? RevokedDate { get; set; }

    public int ViewCount { get; set; }

    public DateTime? LastViewDate { get; set; }

    public DateTime? ApprovedDate { get; set; }

    public string? ApprovedByName { get; set; }

    public List<ShareAccess> Accesses { get; set; } = [];

    public bool IsActive(DateTime nowUtc) => RevokedDate is null && ExpiresDate > nowUtc;
}

public enum ShareAction
{
    View,
    Download,
    Comment,
    Approve,
    WrongPassword,
}

public class ShareAccess : AuditedEntity
{
    public long ShareLinkId { get; set; }

    public ShareLink? ShareLink { get; set; }

    public DateTime AccessDate { get; set; }

    public ShareAction Action { get; set; }

    /// <summary>A hash of the address, not the address: enough to tell visits apart, nothing to identify a person.</summary>
    public string? ClientHash { get; set; }

    public string? UserAgent { get; set; }

    public string? Detail { get; set; }
}
