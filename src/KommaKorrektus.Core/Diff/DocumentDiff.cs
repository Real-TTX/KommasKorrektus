using System.Text;
using System.Text.RegularExpressions;
using KommaKorrektus.Core.Writing;

namespace KommaKorrektus.Core.Diff;

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

/// <summary>One paragraph of a comparison. An unchanged or modified paragraph has an old and a new text; an inserted one only a new text, a deleted one only an old one.</summary>
public sealed class ParagraphDiff
{
    public ParagraphChange Change { get; init; }

    public int? OldIndex { get; init; }

    public int? NewIndex { get; init; }

    public string? OldText { get; init; }

    public string? NewText { get; init; }

    /// <summary>The words: equal, inserted and deleted runs in reading order. For an unchanged paragraph one equal run; for an inserted or deleted one a single run of that kind.</summary>
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
/// Compares two versions of a text, first by paragraphs and then by words inside the paragraphs that were changed. Paragraphs are paired by
/// how alike they are, so a paragraph that was reworked shows as "modified" with its words marked, not as one deleted and one inserted paragraph.
/// </summary>
public static partial class DocumentDiff
{
    /// <summary>Paragraphs that are at least this alike (shared words / all words) count as the same paragraph, changed.</summary>
    public const double SimilarityThreshold = 0.35;

    /// <summary>Blocks of changed paragraphs larger than this in both directions are not paired by similarity (too slow); they are shown as deleted and inserted.</summary>
    private const int PairingLimit = 150;

    /// <summary>Words, numbers, runs of blanks and single other characters: what the word-level comparison works on.</summary>
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
        var edits = Myers.Diff(oldParagraphs, newParagraphs, StringComparer.Ordinal);
        var result = new List<ParagraphDiff>(edits.Count);

        var deleted = new List<(int Index, string Text)>();
        var inserted = new List<(int Index, string Text)>();

        void Flush()
        {
            if (deleted.Count > 0 || inserted.Count > 0)
            {
                result.AddRange(Pair(deleted, inserted));
                deleted.Clear();
                inserted.Clear();
            }
        }

        foreach (var edit in edits)
        {
            switch (edit.Kind)
            {
                case EditKind.Equal:
                    Flush();
                    result.Add(new ParagraphDiff
                    {
                        Change = ParagraphChange.Unchanged, OldIndex = edit.OldIndex, NewIndex = edit.NewIndex, OldText = edit.Item, NewText = edit.Item,
                        Segments = [new DiffSegment(EditKind.Equal, edit.Item)],
                    });
                    break;
                case EditKind.Delete:
                    deleted.Add((edit.OldIndex, edit.Item));
                    break;
                default:
                    inserted.Add((edit.NewIndex, edit.Item));
                    break;
            }
        }

        Flush();
        return result;
    }

    /// <summary>
    /// The words that differ between two paragraphs, merged into runs. Inside a stretch of changes (no kept token between them) all deleted words come first and
    /// all inserted words after them, so a replaced word reads "old new".
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

    public static DiffStats Stats(IEnumerable<ParagraphDiff> diff)
    {
        int added = 0, removed = 0, changed = 0, paragraphs = 0;
        foreach (var p in diff)
        {
            switch (p.Change)
            {
                case ParagraphChange.Inserted:
                    added += TextMetrics.CountWords(p.NewText);
                    paragraphs++;
                    break;
                case ParagraphChange.Deleted:
                    removed += TextMetrics.CountWords(p.OldText);
                    paragraphs++;
                    break;
                case ParagraphChange.Modified:
                    paragraphs++;
                    StatsOfSegments(p.Segments, ref added, ref removed, ref changed);
                    break;
            }
        }

        return new DiffStats(added, removed, changed, paragraphs);
    }

    /// <summary>
    /// Counts the changes of one paragraph: a stretch of deletions and insertions, kept together by blanks and punctuation between them, is one change;
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

    private static List<ParagraphDiff> Pair(List<(int Index, string Text)> deleted, List<(int Index, string Text)> inserted)
    {
        var result = new List<ParagraphDiff>();
        if (deleted.Count == 0)
        {
            result.AddRange(inserted.Select(i => Inserted(i.Index, i.Text)));
            return result;
        }

        if (inserted.Count == 0)
        {
            result.AddRange(deleted.Select(d => Deleted(d.Index, d.Text)));
            return result;
        }

        if (deleted.Count > PairingLimit && inserted.Count > PairingLimit)
        {
            result.AddRange(deleted.Select(d => Deleted(d.Index, d.Text)));
            result.AddRange(inserted.Select(i => Inserted(i.Index, i.Text)));
            return result;
        }

        // Needleman-Wunsch over the two blocks: the score of a pair is its similarity when it is above the threshold; unpaired paragraphs cost nothing.
        var n = deleted.Count;
        var m = inserted.Count;
        var sets = new HashSet<string>[n];
        for (var i = 0; i < n; i++) { sets[i] = WordSet(deleted[i].Text); }
        var setsNew = new HashSet<string>[m];
        for (var j = 0; j < m; j++) { setsNew[j] = WordSet(inserted[j].Text); }

        var score = new double[n + 1, m + 1];
        var sim = new double[n, m];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < m; j++)
            {
                sim[i, j] = Similarity(sets[i], setsNew[j]);
            }
        }

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                var best = Math.Max(score[i + 1, j], score[i, j + 1]);
                if (sim[i, j] >= SimilarityThreshold)
                {
                    best = Math.Max(best, score[i + 1, j + 1] + sim[i, j]);
                }

                score[i, j] = best;
            }
        }

        // Nothing is alike, but as many paragraphs went as came: a paragraph that was written over completely is still the same paragraph, in the same place.
        var anyAlike = false;
        for (var x = 0; x < n && !anyAlike; x++)
        {
            for (var y = 0; y < m; y++)
            {
                if (sim[x, y] >= SimilarityThreshold)
                {
                    anyAlike = true;
                    break;
                }
            }
        }

        if (!anyAlike && n == m)
        {
            for (var k = 0; k < n; k++)
            {
                result.Add(Modified(deleted[k].Index, deleted[k].Text, inserted[k].Index, inserted[k].Text));
            }

            return result;
        }

        int a = 0, b = 0;
        while (a < n || b < m)
        {
            if (a < n && b < m && sim[a, b] >= SimilarityThreshold && Math.Abs(score[a, b] - (score[a + 1, b + 1] + sim[a, b])) < 1e-9)
            {
                result.Add(Modified(deleted[a].Index, deleted[a].Text, inserted[b].Index, inserted[b].Text));
                a++;
                b++;
            }
            else if (a < n && (b >= m || Math.Abs(score[a, b] - score[a + 1, b]) < 1e-9))
            {
                result.Add(Deleted(deleted[a].Index, deleted[a].Text));
                a++;
            }
            else
            {
                result.Add(Inserted(inserted[b].Index, inserted[b].Text));
                b++;
            }
        }

        return result;
    }

    private static HashSet<string> WordSet(string text)
        => new(Tokenize(text).Where(TextMetrics.HasWordCharacter), StringComparer.Ordinal);

    private static double Similarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 1;
        }

        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var shared = a.Count(b.Contains);
        return 2.0 * shared / (a.Count + b.Count);
    }

    private static ParagraphDiff Inserted(int index, string text) => new()
    {
        Change = ParagraphChange.Inserted, NewIndex = index, NewText = text, Segments = [new DiffSegment(EditKind.Insert, text)],
    };

    private static ParagraphDiff Deleted(int index, string text) => new()
    {
        Change = ParagraphChange.Deleted, OldIndex = index, OldText = text, Segments = [new DiffSegment(EditKind.Delete, text)],
    };

    private static ParagraphDiff Modified(int oldIndex, string oldText, int newIndex, string newText)
    {
        var segments = CompareWords(oldText, newText);
        return new ParagraphDiff
        {
            Change = segments.All(s => s.Kind == EditKind.Equal) ? ParagraphChange.Unchanged : ParagraphChange.Modified,
            OldIndex = oldIndex, NewIndex = newIndex, OldText = oldText, NewText = newText, Segments = segments,
        };
    }
}
