using System.Text;
using System.Text.RegularExpressions;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Core.Diff;

/// <summary>A run of text of one kind: kept, inserted (new) or deleted (old).</summary>
public sealed record DiffSegment(EditKind Kind, string Text);

public enum ParagraphChange
{
    Unchanged,
    /// <summary>The paragraph is there in both, with changes inside: <see cref="ParagraphDiff.Segments"/> say what.</summary>
    Modified,
    Inserted,
    Deleted,
}

/// <summary>
/// One row of a comparison: a paragraph as Word keeps it with tracked changes. Mostly that is one old paragraph and the new paragraph it became; a paragraph that was split is two
/// rows of the same old paragraph (the first ends with a break that was put in, <see cref="Mark"/> = Insert), two paragraphs that were joined are two rows of the same new
/// paragraph (the first ends with a break that was taken away, Delete). An inserted row has only a new paragraph, a deleted one only an old one.
/// </summary>
public sealed class ParagraphDiff
{
    public ParagraphChange Change { get; init; }

    public int? OldIndex { get; init; }

    public int? NewIndex { get; init; }

    /// <summary>The part of the old paragraph this row holds (all of it, unless the paragraph was split).</summary>
    public string? OldText { get; init; }

    /// <summary>The part of the new paragraph this row holds (all of it, unless it was made of joined paragraphs).</summary>
    public string? NewText { get; init; }

    /// <summary>Where <see cref="OldText"/> begins in the old paragraph.</summary>
    public int OldFrom { get; init; }

    /// <summary>Where <see cref="NewText"/> begins in the new paragraph.</summary>
    public int NewFrom { get; init; }

    /// <summary>What became of the paragraph break at the end of the row: it stayed (Equal), it was taken away (Delete: joined with the next row), it was put in (Insert).</summary>
    public EditKind Mark { get; init; } = EditKind.Equal;

    /// <summary>The text: equal, inserted and deleted runs in reading order (inside a change what was taken away comes first). For an unchanged paragraph one equal run.</summary>
    public IReadOnlyList<DiffSegment> Segments { get; init; } = [];
}

/// <summary>
/// How much was done between two versions. <see cref="WordsChanged"/> counts words that were replaced by others, <see cref="WordsAdded"/> words that
/// came on top, <see cref="WordsRemoved"/> words that went without a replacement. What an editor calls "words corrected" is added plus changed.
/// </summary>
public sealed record DiffStats(int WordsAdded, int WordsRemoved, int WordsChanged, int ParagraphsChanged)
{
    public int WordsEdited => WordsAdded + WordsChanged;

    public bool HasChanges => WordsAdded + WordsRemoved + WordsChanged + ParagraphsChanged > 0;

    public static DiffStats None { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Compares two versions of a text the way Visual Studio Code compares two files (<see cref="TextDiff"/>, the same as the browser's textdiff.js): the paragraphs first, then
/// every stretch that differs character by character, so that a corrected letter is a corrected letter, a split paragraph is a break put in and the text after it goes on.
/// </summary>
public static partial class DocumentDiff
{
    /// <summary>Words, numbers, runs of blanks and single other characters: what the counting of changed words works on.</summary>
    [GeneratedRegex(@"[\p{L}\p{M}][\p{L}\p{M}\p{N}'’\-]*|\p{N}+(?:[.,]\p{N}+)*|[ \t ]+|\p{Zl}|.", RegexOptions.Singleline)]
    private static partial Regex TokenRegex();

    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (Match m in TokenRegex().Matches(text))
        {
            tokens.Add(m.Value);
        }

        return tokens;
    }

    public static IReadOnlyList<ParagraphDiff> Compare(IReadOnlyList<string> oldParagraphs, IReadOnlyList<string> newParagraphs)
    {
        var result = TextDiff.Compare(oldParagraphs, newParagraphs);
        return [.. result.Rows.Select(r => new ParagraphDiff
        {
            Change = r.Kind switch { "same" => ParagraphChange.Unchanged, "ins" => ParagraphChange.Inserted, "del" => ParagraphChange.Deleted, _ => ParagraphChange.Modified },
            OldIndex = r.O,
            NewIndex = r.N,
            OldText = r.O is { } o ? oldParagraphs[o][r.OldFrom..r.OldTo] : null,
            NewText = r.N is { } n ? newParagraphs[n][r.NewFrom..r.NewTo] : null,
            OldFrom = r.OldFrom,
            NewFrom = r.NewFrom,
            Mark = r.Mark switch { 'D' => EditKind.Delete, 'I' => EditKind.Insert, _ => EditKind.Equal },
            Segments = r.Segments,
        })];
    }

    /// <summary>
    /// The words that differ between two texts, merged into runs (word by word, for counting them). Inside a stretch of changes (no kept token between them) all deleted words come
    /// first and all inserted words after them, so a replaced word reads "old new".
    /// </summary>
    public static IReadOnlyList<DiffSegment> CompareWords(string oldText, string newText)
    {
        var edits = Myers.Diff(Tokenize(oldText), Tokenize(newText), StringComparer.Ordinal);
        var segments = new List<DiffSegment>();
        StringBuilder deleted = new(), inserted = new(), kept = new();

        void FlushChange()
        {
            if (deleted.Length > 0) { segments.Add(new DiffSegment(EditKind.Delete, deleted.ToString())); deleted.Clear(); }
            if (inserted.Length > 0) { segments.Add(new DiffSegment(EditKind.Insert, inserted.ToString())); inserted.Clear(); }
        }

        void FlushKept()
        {
            if (kept.Length > 0) { segments.Add(new DiffSegment(EditKind.Equal, kept.ToString())); kept.Clear(); }
        }

        foreach (var edit in edits)
        {
            switch (edit.Kind)
            {
                case EditKind.Equal:
                    FlushChange();
                    kept.Append(edit.Item);
                    break;
                case EditKind.Delete:
                    FlushKept();
                    deleted.Append(edit.Item);
                    break;
                default:
                    FlushKept();
                    inserted.Append(edit.Item);
                    break;
            }
        }

        FlushChange();
        FlushKept();
        return segments;
    }

    /// <summary>
    /// How many words were added, removed and changed. The marks of a comparison are characters (a corrected letter); a person counts words, so a changed row is counted by
    /// comparing its words.
    /// </summary>
    public static DiffStats Stats(IEnumerable<ParagraphDiff> diff)
    {
        int added = 0, removed = 0, changed = 0, gone = 0;
        var touched = new HashSet<int>();   // the new paragraphs with a change (a split paragraph is two, joined ones are one)
        foreach (var p in diff)
        {
            switch (p.Change)
            {
                case ParagraphChange.Inserted:
                    added += TextMetrics.CountWords(p.NewText);
                    break;
                case ParagraphChange.Deleted:
                    removed += TextMetrics.CountWords(p.OldText);
                    break;
                case ParagraphChange.Modified:
                    StatsOfSegments(CompareWords(p.OldText ?? string.Empty, p.NewText ?? string.Empty), ref added, ref removed, ref changed);
                    break;
                default:
                    continue;
            }

            if (p.NewIndex is { } index) { touched.Add(index); } else { gone++; }
        }

        return new DiffStats(added, removed, changed, touched.Count + gone);
    }

    /// <summary>
    /// Counts the changes of one row: a stretch of deletions and insertions, kept together by blanks and punctuation between them, is one change;
    /// as many words as were replaced are "changed", the rest "added" or "removed".
    /// </summary>
    private static void StatsOfSegments(IReadOnlyList<DiffSegment> segments, ref int added, ref int removed, ref int changed)
    {
        int del = 0, ins = 0;
        void End(ref int a, ref int r, ref int c)
        {
            var replaced = Math.Min(del, ins);
            c += replaced;
            a += ins - replaced;
            r += del - replaced;
            del = 0;
            ins = 0;
        }

        foreach (var s in segments)
        {
            switch (s.Kind)
            {
                case EditKind.Delete:
                    del += TextMetrics.CountWords(s.Text);
                    break;
                case EditKind.Insert:
                    ins += TextMetrics.CountWords(s.Text);
                    break;
                default:
                    if (TextMetrics.HasWordCharacter(s.Text))
                    {
                        End(ref added, ref removed, ref changed);
                    }

                    break;
            }
        }

        End(ref added, ref removed, ref changed);
    }
}
