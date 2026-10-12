using KommasKorrektus.Configuration;
using KommasKorrektus.Core.Diff;
using KommasKorrektus.Core.Documents;
using KommasKorrektus.Core.Writing;
using KommasKorrektus.Data;
using KommasKorrektus.Data.Entities;
using KommasKorrektus.Storage;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Services;

public enum ExportFormat
{
    /// <summary>Word with the changes as tracked changes that can be accepted in Word.</summary>
    DocxTracked,
    /// <summary>Word with the new text, no changes shown.</summary>
    DocxClean,
    /// <summary>PDF proof sheet: deleted words struck through, new words underlined.</summary>
    PdfChanges,
    PdfClean,
    PdfSideBySide,
    Text,
}

public sealed record ExportResult(byte[] Bytes, string FileName, string ContentType);

public sealed record ImportResult(Document Document, IReadOnlyList<string> Notes);

public sealed class ImportException(string message) : Exception(message);

/// <summary>
/// Documents and their versions. Everything the editor does to a text goes through here: the import of a file (Word, PDF, text), saving (a version is a
/// pass over the text: saves go into the version being worked on until the editor closes it, then the next change begins a new one), putting an old
/// version back, and the exports.
/// </summary>
public sealed class DocumentService(KkDbContext db, FileStoreService files, ConfigStore config, ICurrentUser current, TimeProvider clock)
{
    public const long MaxUploadBytes = 150L * 1024 * 1024;

    public static DocumentKind DetectKind(string fileName, byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B)
        {
            return DocumentKind.Docx;
        }

        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
        {
            return DocumentKind.Pdf;
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".txt" or ".md" or ".text" ? DocumentKind.Text : throw new ImportException("Dieses Dateiformat kann ich nicht lesen. Bitte lade eine Word-Datei (.docx), ein PDF oder eine Textdatei (.txt) hoch.");
    }

    /// <summary>Reads a file: the kind, the paragraphs, and what the reading had to guess.</summary>
    private static (DocumentKind Kind, IReadOnlyList<string> Paragraphs, List<string> Notes, IReadOnlyList<ImportedComment> Comments) ReadFile(string fileName, byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ImportException("Die Datei ist leer.");
        }

        var kind = DetectKind(fileName, bytes);
        IReadOnlyList<string> paragraphs;
        IReadOnlyList<ImportedComment> comments = [];
        var notes = new List<string>();
        try
        {
            switch (kind)
            {
                case DocumentKind.Docx:
                    paragraphs = DocxReader.ReadParagraphs(bytes);
                    comments = ReadWordComments(bytes, notes);
                    if (!Path.GetExtension(fileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add("Die Datei ist ein Word-Dokument, hat aber eine andere Endung.");
                    }

                    break;
                case DocumentKind.Pdf:
                    var pdf = PdfTextReader.Read(bytes);
                    paragraphs = pdf.Paragraphs;
                    comments = pdf.Comments;
                    notes.AddRange(pdf.Notes);
                    break;
                default:
                    paragraphs = PlainTextReader.Read(DecodeText(bytes)).Paragraphs;
                    break;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or IOException or InvalidOperationException or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            throw new ImportException("Die Datei lässt sich nicht lesen. Ist sie beschädigt oder mit einem Kennwort geschützt?");
        }

        if (paragraphs.Count == 0 || paragraphs.All(string.IsNullOrWhiteSpace))
        {
            throw new ImportException(notes.FirstOrDefault(n => n.Contains("keinen Text")) ?? "In der Datei steht kein Text.");
        }

        if (comments.Count > 0)
        {
            notes.Add(comments.Count == 1 ? "Ein Kommentar aus der Datei wurde übernommen." : $"{comments.Count} Kommentare aus der Datei wurden übernommen.");
        }

        return (kind, paragraphs, notes, comments);
    }

    /// <summary>The comments of a Word file. They are a bonus: a file whose comments cannot be read is imported without them.</summary>
    private static IReadOnlyList<ImportedComment> ReadWordComments(byte[] bytes, List<string> notes)
    {
        try
        {
            return DocxComments.Read(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            notes.Add("Die Kommentare der Word-Datei ließen sich nicht lesen.");
            return [];
        }
    }

    /// <summary>The remarks that were in a file become comments of the document, on the paragraph and the passage they were written on.</summary>
    private void AddImportedComments(Document document, int versionNo, DocumentKind kind, int paragraphCount, IReadOnlyList<ImportedComment> comments)
    {
        foreach (var c in comments)
        {
            var comment = new DocumentComment
            {
                VersionNo = versionNo,
                ParagraphIndex = Math.Clamp(c.ParagraphIndex, 0, Math.Max(0, paragraphCount - 1)),
                Quote = c.Quote,
                Text = c.Text,
                AuthorName = c.Author ?? "Aus der Datei",
                Source = kind == DocumentKind.Docx ? "docx" : "pdf",
                WrittenDate = c.Written,
                IsResolved = c.Resolved,
                ResolvedDate = c.Resolved ? c.Written ?? clock.GetUtcNow().UtcDateTime : null,
            };
            if (document.Id == 0)
            {
                comment.Document = document;
            }
            else
            {
                comment.DocumentId = document.Id;
            }

            db.DocumentComment.Add(comment);
        }
    }

    private static string ContentTypeOf(DocumentKind kind) => kind switch
    {
        DocumentKind.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        DocumentKind.Pdf => "application/pdf",
        _ => "text/plain; charset=utf-8",
    };

    /// <summary>Reads a file and makes a document with its first version (the text as the author delivered it). Saved by the caller's SaveChanges.</summary>
    public async Task<ImportResult> ImportAsync(Project project, string title, string fileName, byte[] bytes, CancellationToken ct = default)
    {
        var (kind, paragraphs, notes, comments) = ReadFile(fileName, bytes);
        var original = await files.SaveAsync(bytes, fileName, ContentTypeOf(kind), FileKind.Original, projectId: project.Id, customerId: project.CustomerId, ct: ct);
        var document = new Document
        {
            Project = project,
            ProjectId = project.Id,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(),
            FileName = FileStoreService.SafeFileName(fileName),
            Kind = kind,
            OriginalFile = original,
            CurrentVersionNo = 1,
            ImportNotes = notes.Count == 0 ? null : string.Join(' ', notes),
        };
        document.Versions.Add(NewVersion(1, paragraphs, VersionOrigin.Import, "Original", null, DiffStats.None));
        db.Document.Add(document);
        AddImportedComments(document, 1, kind, paragraphs.Count, comments);
        return new ImportResult(document, notes);
    }

    /// <summary>
    /// A file the customer sent back is the next version of the document: its text becomes the version (compared with the one before, so that "what did the customer
    /// change" is the comparison of the last two), the file is kept with it and is the base of the formatting from here on (a new round of the ping pong). The pass of the
    /// editor that was open ends with it. Saved here.
    /// </summary>
    public async Task<SaveResult> AddFileVersionAsync(Document document, string fileName, byte[] bytes, string? label, string? comment, CancellationToken ct = default)
    {
        var (kind, paragraphs, notes, comments) = ReadFile(fileName, bytes);
        var latest = await db.DocumentVersion.FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == document.CurrentVersionNo, ct);
        var delta = DocumentDiff.Stats(DocumentDiff.Compare(TextMetrics.Paragraphs(latest.Text), paragraphs));
        if (latest.Origin == VersionOrigin.Working)
        {
            Close(latest, null, null);
        }

        var customerId = await db.Project.AsNoTracking().Where(p => p.Id == document.ProjectId).Select(p => (long?)p.CustomerId).FirstOrDefaultAsync(ct);
        var stored = await files.SaveAsync(bytes, fileName, ContentTypeOf(kind), FileKind.Original, projectId: document.ProjectId, customerId: customerId, ct: ct);
        var remark = string.Join(' ', new[] { string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), notes.Count == 0 ? null : string.Join(' ', notes) }.Where(t => t is not null));
        var version = NewVersion(latest.VersionNo + 1, paragraphs, VersionOrigin.Upload, string.IsNullOrWhiteSpace(label) ? "Datei vom Kunden" : label.Trim(), remark.Length == 0 ? null : remark, delta);
        version.DocumentId = document.Id;
        version.SourceFile = stored;
        db.DocumentVersion.Add(version);
        AddImportedComments(document, version.VersionNo, kind, paragraphs.Count, comments);
        document.CurrentVersionNo = version.VersionNo;
        await db.SaveChangesAsync(ct);
        return new SaveResult(version, true, delta);
    }

    private static string DecodeText(byte[] bytes)
    {
        // UTF-8 with or without a mark; a file that is not valid UTF-8 is taken as Windows-1252 (an old text file from a German word processor).
        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private DocumentVersion NewVersion(int no, IReadOnlyList<string> paragraphs, VersionOrigin origin, string? label, string? comment, DiffStats stats) => new()
    {
        VersionNo = no,
        Origin = origin,
        Label = label,
        Comment = comment,
        Text = TextMetrics.JoinParagraphs(paragraphs),
        ParagraphCount = paragraphs.Count,
        WordCount = TextMetrics.CountWords(paragraphs),
        CharCount = TextMetrics.CountCharacters(paragraphs),
        WordsAdded = stats.WordsAdded,
        WordsRemoved = stats.WordsRemoved,
        WordsChanged = stats.WordsChanged,
    };

    public Task<DocumentVersion?> GetVersionAsync(long documentId, int versionNo, CancellationToken ct = default)
        => db.DocumentVersion.AsNoTracking().FirstOrDefaultAsync(v => v.DocumentId == documentId && v.VersionNo == versionNo, ct);

    public static string[] ParagraphsOf(DocumentVersion version) => TextMetrics.Paragraphs(version.Text);

    /// <summary>What a save did.</summary>
    public sealed record SaveResult(DocumentVersion Version, bool Created, DiffStats Delta);

    /// <summary>
    /// Saves a text. No change, no new version. A save goes into the version that is being worked on (<see cref="VersionOrigin.Working"/>) as long as it is
    /// the same person's: saving ten times leaves one version, so a version is a pass over the text, not a click. Saving with <see cref="VersionOrigin.Manual"/>
    /// closes the version (the editor says "this pass is done", and may name it); the next change then begins a new one.
    /// </summary>
    public async Task<SaveResult> SaveAsync(Document document, string text, VersionOrigin origin, string? label, string? comment, CancellationToken ct = default)
    {
        var latest = await db.DocumentVersion.FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == document.CurrentVersionNo, ct);
        var paragraphs = TextMetrics.Paragraphs(text);
        var oldParagraphs = TextMetrics.Paragraphs(latest.Text);
        var closing = origin == VersionOrigin.Manual;

        var delta = DocumentDiff.Stats(DocumentDiff.Compare(oldParagraphs, paragraphs));
        if (!delta.HasChanges)
        {
            // Nothing changed since, but the editor closes the pass (or names a closed one). The original is never touched.
            var named = !string.IsNullOrWhiteSpace(label) || !string.IsNullOrWhiteSpace(comment);
            if (closing && latest.Origin != VersionOrigin.Import && (latest.Origin == VersionOrigin.Working || named))
            {
                Close(latest, label, comment);
                await db.SaveChangesAsync(ct);
            }

            return new SaveResult(latest, false, DiffStats.None);
        }

        var working = latest.Origin == VersionOrigin.Working && latest.CreateUserId == current.UserId && latest.VersionNo > 1;
        if (working)
        {
            // The statistics of the version are against the version before it, not against itself.
            var before = await db.DocumentVersion.AsNoTracking().FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == latest.VersionNo - 1, ct);
            var against = DocumentDiff.Stats(DocumentDiff.Compare(TextMetrics.Paragraphs(before.Text), paragraphs));
            latest.Text = TextMetrics.JoinParagraphs(paragraphs);
            latest.ParagraphCount = paragraphs.Length;
            latest.WordCount = TextMetrics.CountWords(paragraphs);
            latest.CharCount = TextMetrics.CountCharacters(paragraphs);
            latest.WordsAdded = against.WordsAdded;
            latest.WordsRemoved = against.WordsRemoved;
            latest.WordsChanged = against.WordsChanged;
            if (closing)
            {
                Close(latest, label, comment);
            }

            await db.SaveChangesAsync(ct);
            return new SaveResult(latest, false, delta);
        }

        var version = NewVersion(latest.VersionNo + 1, paragraphs, closing ? VersionOrigin.Manual : VersionOrigin.Working, string.IsNullOrWhiteSpace(label) ? null : label.Trim(), string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), delta);
        version.DocumentId = document.Id;
        db.DocumentVersion.Add(version);
        document.CurrentVersionNo = version.VersionNo;
        await db.SaveChangesAsync(ct);
        return new SaveResult(version, true, delta);
    }

    /// <summary>The pass is done: a version being worked on becomes a closed one; a name or remark given with it is kept (a closed version only gets the name).</summary>
    private static void Close(DocumentVersion version, string? label, string? comment)
    {
        if (version.Origin == VersionOrigin.Working)
        {
            version.Origin = VersionOrigin.Manual;
        }

        version.Label = string.IsNullOrWhiteSpace(label) ? version.Label : label.Trim();
        version.Comment = string.IsNullOrWhiteSpace(comment) ? version.Comment : comment.Trim();
    }

    /// <summary>Puts an old version back as a new one: nothing is lost, the versions in between stay.</summary>
    public async Task<SaveResult> RestoreAsync(Document document, int versionNo, CancellationToken ct = default)
    {
        var old = await GetVersionAsync(document.Id, versionNo, ct) ?? throw new InvalidOperationException("Diese Fassung gibt es nicht.");
        var latest = await db.DocumentVersion.AsNoTracking().FirstAsync(v => v.DocumentId == document.Id && v.VersionNo == document.CurrentVersionNo, ct);
        var delta = DocumentDiff.Stats(DocumentDiff.Compare(TextMetrics.Paragraphs(latest.Text), TextMetrics.Paragraphs(old.Text)));
        var version = NewVersion(latest.VersionNo + 1, TextMetrics.Paragraphs(old.Text), VersionOrigin.Restore, $"Zurück auf Fassung {versionNo}", null, delta);
        version.DocumentId = document.Id;
        db.DocumentVersion.Add(version);
        document.CurrentVersionNo = version.VersionNo;
        await db.SaveChangesAsync(ct);
        return new SaveResult(version, true, delta);
    }

    public async Task<(IReadOnlyList<ParagraphDiff> Diff, DiffStats Stats)> CompareAsync(Document document, int fromNo, int toNo, CancellationToken ct = default)
    {
        var from = await GetVersionAsync(document.Id, fromNo, ct) ?? throw new InvalidOperationException("Die Ausgangsfassung gibt es nicht.");
        var to = fromNo == toNo ? from : await GetVersionAsync(document.Id, toNo, ct) ?? throw new InvalidOperationException("Die Zielfassung gibt es nicht.");
        var diff = DocumentDiff.Compare(ParagraphsOf(from), ParagraphsOf(to));
        return (diff, DocumentDiff.Stats(diff));
    }

    public async Task<ExportResult> ExportAsync(Document document, int fromNo, int toNo, ExportFormat format, string author, CancellationToken ct = default)
    {
        var from = await GetVersionAsync(document.Id, fromNo, ct) ?? throw new InvalidOperationException("Die Ausgangsfassung gibt es nicht.");
        var to = fromNo == toNo ? from : await GetVersionAsync(document.Id, toNo, ct) ?? throw new InvalidOperationException("Die Zielfassung gibt es nicht.");
        var oldParagraphs = ParagraphsOf(from);
        var newParagraphs = ParagraphsOf(to);
        var stem = Path.GetFileNameWithoutExtension(document.FileName);
        var date = clock.GetUtcNow().UtcDateTime;
        var customer = await db.Project.AsNoTracking().Where(p => p.Id == document.ProjectId).Select(p => p.Customer!.Company ?? p.Customer.LastName).FirstOrDefaultAsync(ct);
        var header = new PdfHeader(document.Title, $"{customer} · Fassung {fromNo} → {toNo} · {Fmt.Date(date)}".TrimStart(' ', '·'), config.Current.Company.Name);

        switch (format)
        {
            case ExportFormat.DocxTracked:
            case ExportFormat.DocxClean:
                var baseDocx = await BaseDocxAsync(document, fromNo, toNo, oldParagraphs, ct);
                var mode = format == ExportFormat.DocxTracked ? RevisionMode.Tracked : RevisionMode.Accepted;
                var docx = DocxWriter.Write(baseDocx, oldParagraphs, newParagraphs, mode, author, date);
                var suffix = format == ExportFormat.DocxTracked ? "mit Änderungen" : "bereinigt";
                return new ExportResult(docx, $"{stem} ({suffix}).docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

            case ExportFormat.PdfChanges:
                return new ExportResult(DocumentPdf.Render(header, DocumentDiff.Compare(oldParagraphs, newParagraphs), PdfLayout.Changes), $"{stem} (Korrekturfahne).pdf", "application/pdf");

            case ExportFormat.PdfSideBySide:
                return new ExportResult(DocumentPdf.Render(header, DocumentDiff.Compare(oldParagraphs, newParagraphs), PdfLayout.SideBySide), $"{stem} (alt und neu).pdf", "application/pdf");

            case ExportFormat.PdfClean:
                return new ExportResult(DocumentPdf.RenderClean(header with { Subtitle = $"{customer} · Fassung {toNo} · {Fmt.Date(date)}".TrimStart(' ', '·') }, newParagraphs), $"{stem} (bereinigt).pdf", "application/pdf");

            default:
                return new ExportResult(System.Text.Encoding.UTF8.GetBytes(string.Join("\n\n", newParagraphs.Where(p => p.Length > 0)).Replace(TextMetrics.LineBreak, '\n')), $"{stem}.txt", "text/plain; charset=utf-8");
        }
    }

    /// <summary>
    /// A round of the ping pong between editor and customer: it begins with version 1 or with a file of the customer. <see cref="Docx"/> is that file when it is a Word file
    /// (null: the page and the export of the round are plain), <see cref="Paragraphs"/> its text, which is the text of version <see cref="StartNo"/>.
    /// </summary>
    public sealed record Round(int StartNo, string[] Paragraphs, byte[]? Docx);

    /// <summary>The round a version belongs to: the latest version at or before it that came from a file (version 1 is the file of the import).</summary>
    public async Task<Round?> RoundOfAsync(Document document, int versionNo, CancellationToken ct = default)
    {
        var start = await db.DocumentVersion.AsNoTracking()
            .Where(v => v.DocumentId == document.Id && v.VersionNo <= versionNo && (v.VersionNo == 1 || v.SourceFileId != null))
            .OrderByDescending(v => v.VersionNo).FirstOrDefaultAsync(ct);
        if (start is null)
        {
            return null;
        }

        var docx = start.VersionNo == 1 ? await OriginalDocxAsync(document, ct) : await DocxOfFileAsync(start.SourceFileId, ct);
        return new Round(start.VersionNo, ParagraphsOf(start), docx);
    }

    /// <summary>The number of the version the round of <paramref name="versionNo"/> began with: what the editor changed in this round is everything since.</summary>
    public async Task<int> RoundStartAsync(Document document, int versionNo, CancellationToken ct = default)
        => await db.DocumentVersion.AsNoTracking()
            .Where(v => v.DocumentId == document.Id && v.VersionNo <= versionNo && (v.VersionNo == 1 || v.SourceFileId != null))
            .OrderByDescending(v => v.VersionNo).Select(v => v.VersionNo).FirstOrDefaultAsync(ct) is var no && no > 0 ? no : 1;

    private async Task<byte[]?> DocxOfFileAsync(long? fileId, CancellationToken ct)
    {
        var stored = fileId is null ? null : await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (stored is null)
        {
            return null;
        }

        try
        {
            var bytes = await files.ReadAllAsync(stored, ct);
            return bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B ? bytes : null;
        }
        catch (StorageException)
        {
            return null;
        }
    }

    /// <summary>The Word file as it was uploaded; null when the document did not come as Word or the file cannot be read (the page and the export then make a plain one).</summary>
    public async Task<byte[]?> OriginalDocxAsync(Document document, CancellationToken ct = default)
    {
        if (document.Kind != DocumentKind.Docx || document.OriginalFileId is null)
        {
            return null;
        }

        var stored = await db.StoredFile.AsNoTracking().FirstOrDefaultAsync(f => f.Id == document.OriginalFileId, ct);
        if (stored is null)
        {
            return null;
        }

        try
        {
            return await files.ReadAllAsync(stored, ct);
        }
        catch (StorageException)
        {
            return null;
        }
    }

    /// <summary>
    /// The Word file the export starts from: the file of the round the target version belongs to, brought to the starting version (its text with the changes
    /// accepted, formatting kept). A round that did not come as Word has no file to keep the formatting of, so the export makes a plain one (null).
    /// </summary>
    private async Task<byte[]?> BaseDocxAsync(Document document, int fromNo, int toNo, IReadOnlyList<string> fromParagraphs, CancellationToken ct)
    {
        var round = await RoundOfAsync(document, toNo, ct);
        if (round?.Docx is null)
        {
            return null;
        }

        return fromNo == round.StartNo ? round.Docx : DocxWriter.Write(round.Docx, round.Paragraphs, fromParagraphs, RevisionMode.Accepted, "KommasKorrektus", clock.GetUtcNow().UtcDateTime);
    }
}
