namespace KommasKorrektus.Data.Entities;

public enum DocumentKind
{
    Docx,
    Pdf,
    Text,
}

public class Document : AuditedEntity, ISoftDeletable
{
    public long ProjectId { get; set; }

    public Project? Project { get; set; }

    public string Title { get; set; } = "";

    /// <summary>The name of the file that was uploaded.</summary>
    public string FileName { get; set; } = "";

    public DocumentKind Kind { get; set; } = DocumentKind.Text;

    /// <summary>The uploaded file as it came: the base of an export with tracked changes.</summary>
    public long? OriginalFileId { get; set; }

    public StoredFile? OriginalFile { get; set; }

    /// <summary>The number of the newest version.</summary>
    public int CurrentVersionNo { get; set; }

    public string? Notes { get; set; }

    /// <summary>What the import could not carry over (a PDF loses its layout, a table of contents is not text).</summary>
    public string? ImportNotes { get; set; }

    public UpdateState UpdateState { get; set; } = UpdateState.Created;

    public List<DocumentVersion> Versions { get; set; } = [];
}

public enum VersionOrigin
{
    /// <summary>Version 1: the text as the author delivered it.</summary>
    Import,
    /// <summary>A pass that the editor closed on purpose ("Durchgang abschließen"): the next change begins a new version.</summary>
    Manual,
    /// <summary>The version being worked on: every save of the same person, by hand or automatic, updates it until the pass is closed.</summary>
    Working,
    /// <summary>An older version that was put back.</summary>
    Restore,
    /// <summary>A file the customer sent back: the next round of the ping pong between editor and customer. Its text is the version, the file is kept with it.</summary>
    Upload,
}

/// <summary>
/// One state of the text of a document. The text holds the paragraphs separated by a line feed; a line break inside a paragraph is U+2028.
/// Nothing is ever overwritten except the version still being worked on by the same person: a closed pass is never touched, "everything is versioned".
/// </summary>
public class DocumentVersion : AuditedEntity
{
    public long DocumentId { get; set; }

    public Document? Document { get; set; }

    public int VersionNo { get; set; }

    public string? Label { get; set; }

    public VersionOrigin Origin { get; set; } = VersionOrigin.Manual;

    public string Text { get; set; } = "";

    public int ParagraphCount { get; set; }

    public int WordCount { get; set; }

    public int CharCount { get; set; }

    /// <summary>Compared with the version before: words that came in, words that went away, words that were replaced.</summary>
    public int WordsAdded { get; set; }

    public int WordsRemoved { get; set; }

    public int WordsChanged { get; set; }

    public string? Comment { get; set; }

    /// <summary>
    /// The file this version came from (a file of the customer, <see cref="VersionOrigin.Upload"/>). It is the base of the formatting from this version on: the page
    /// and the Word export of a version use the file of the latest round at or before it. Version 1 has <see cref="Document.OriginalFileId"/>.
    /// </summary>
    public long? SourceFileId { get; set; }

    public StoredFile? SourceFile { get; set; }
}

/// <summary>A remark of the editor (or of the client, through a link) on a paragraph.</summary>
public class DocumentComment : AuditedEntity
{
    public long DocumentId { get; set; }

    public Document? Document { get; set; }

    /// <summary>The version the remark was written on.</summary>
    public int VersionNo { get; set; }

    public int ParagraphIndex { get; set; }

    /// <summary>The words the remark is about, so that it can be found again when the paragraph has moved.</summary>
    public string? Quote { get; set; }

    public string Text { get; set; } = "";

    public long? AuthorUserId { get; set; }

    /// <summary>The name a visitor of a share link typed; null for a user.</summary>
    public string? AuthorName { get; set; }

    public long? ShareLinkId { get; set; }

    public bool IsResolved { get; set; }

    public DateTime? ResolvedDate { get; set; }
}

public enum FileKind
{
    Original,
    Export,
    Logo,
    InvoicePdf,
    Attachment,
}

/// <summary>A file in the file store (the volume or the NAS). The database knows the key; the bytes are in the store.</summary>
public class StoredFile : AuditedEntity
{
    public Guid Token { get; set; } = Guid.NewGuid();

    /// <summary>Where the store keeps it, relative to its root ("2026/10/3f2a...").</summary>
    public string StorageKey { get; set; } = "";

    public string FileName { get; set; } = "";

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = "";

    public FileKind Kind { get; set; } = FileKind.Attachment;

    public long? ProjectId { get; set; }

    public long? CustomerId { get; set; }

    public long? DocumentId { get; set; }

    public long? InvoiceId { get; set; }
}
