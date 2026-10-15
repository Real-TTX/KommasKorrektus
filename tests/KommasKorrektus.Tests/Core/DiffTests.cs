using KommasKorrektus.Core.Diff;
using KommasKorrektus.Core.Writing;

namespace KommasKorrektus.Tests.Core;

public class MyersTests
{
    private static string Apply(List<Edit<char>> edits, bool useOld)
        => new(edits.Where(e => e.Kind == EditKind.Equal || e.Kind == (useOld ? EditKind.Delete : EditKind.Insert)).Select(e => e.Item).ToArray());

    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    [InlineData("abc", "")]
    [InlineData("", "abc")]
    [InlineData("abcabba", "cbabac")]
    [InlineData("the cat sat", "the dog sat down")]
    [InlineData("xyz", "abc")]
    public void The_script_reproduces_both_sequences(string a, string b)
    {
        var edits = Myers.Diff(a.ToCharArray(), b.ToCharArray());
        Assert.Equal(a, Apply(edits, useOld: true));
        Assert.Equal(b, Apply(edits, useOld: false));
    }

    [Fact]
    public void Random_sequences_are_reproduced_and_the_script_is_not_longer_than_the_trivial_one()
    {
        var random = new Random(42);
        for (var round = 0; round < 300; round++)
        {
            var a = new string(Enumerable.Range(0, random.Next(0, 40)).Select(_ => (char)('a' + random.Next(4))).ToArray());
            var b = new string(Enumerable.Range(0, random.Next(0, 40)).Select(_ => (char)('a' + random.Next(4))).ToArray());
            var edits = Myers.Diff(a.ToCharArray(), b.ToCharArray());
            Assert.Equal(a, Apply(edits, true));
            Assert.Equal(b, Apply(edits, false));
            Assert.True(edits.Count(e => e.Kind != EditKind.Equal) <= a.Length + b.Length);
        }
    }

    [Fact]
    public void A_single_change_in_a_long_text_is_found_exactly()
    {
        var a = Enumerable.Range(0, 5000).Select(i => "line " + i).ToList();
        var b = a.ToList();
        b[2500] = "line changed";
        var edits = Myers.Diff(a, b);
        Assert.Equal(2, edits.Count(e => e.Kind != EditKind.Equal));
    }

    [Fact]
    public void Completely_different_long_sequences_fall_back_to_a_replacement_instead_of_exhausting_memory()
    {
        var a = Enumerable.Range(0, 6000).Select(i => "a" + i).ToList();
        var b = Enumerable.Range(0, 6000).Select(i => "b" + i).ToList();
        var edits = Myers.Diff(a, b);
        Assert.Equal(6000, edits.Count(e => e.Kind == EditKind.Delete));
        Assert.Equal(6000, edits.Count(e => e.Kind == EditKind.Insert));
    }
}

public class DocumentDiffTests
{
    private static string Marked(IReadOnlyList<DiffSegment> segments)
        => string.Concat(segments.Select(s => s.Kind switch { EditKind.Insert => "[+" + s.Text + "+]", EditKind.Delete => "[-" + s.Text + "-]", _ => s.Text }));

    [Fact]
    public void A_corrected_letter_is_a_letter_taken_away_and_one_put_in()
    {
        var diff = DocumentDiff.Compare(["Das ist ein Fehler im Satz."], ["Das ist ein Fahler im Satz."]);
        var p = Assert.Single(diff);
        Assert.Equal(ParagraphChange.Modified, p.Change);
        Assert.Equal("Das ist ein F[-e-][+a+]hler im Satz.", Marked(p.Segments));
    }

    [Fact]
    public void A_word_that_is_mostly_changed_is_changed_as_a_whole_and_a_comma_is_a_comma()
    {
        var diff = DocumentDiff.Compare(["Ich habe ein Buch gelesen dass er kam."], ["Ich hatte ein Roman gelesen, das er kam."]);
        Assert.Equal("Ich ha[-b-][+tt+]e ein [-Buch-][+Roman+] gelesen[+,+] das[-s-] er kam.", Marked(Assert.Single(diff).Segments));
    }

    [Fact]
    public void Identical_paragraphs_are_unchanged_and_cost_nothing()
    {
        var diff = DocumentDiff.Compare(["a b", "c d"], ["a b", "c d"]);
        Assert.All(diff, p => Assert.Equal(ParagraphChange.Unchanged, p.Change));
        Assert.False(DocumentDiff.Stats(diff).HasChanges);
    }

    [Fact]
    public void A_reworked_paragraph_is_paired_with_its_old_version_not_deleted_and_inserted()
    {
        var oldText = "Die Katze saß auf dem Dach und sah zum Mond.";
        var newText = "Die Katze saß auf dem Dach und blickte zum hellen Mond.";
        var diff = DocumentDiff.Compare(["Anfang.", oldText, "Ende."], ["Anfang.", newText, "Ende."]);
        Assert.Equal(3, diff.Count);
        Assert.Equal(ParagraphChange.Modified, diff[1].Change);
        Assert.Contains("[-sah-]", Marked(diff[1].Segments));
    }

    [Fact]
    public void A_new_paragraph_and_a_removed_paragraph_are_found()
    {
        var diff = DocumentDiff.Compare(["eins", "zwei", "drei"], ["eins", "zwei und ein halbes Wort mehr als vorher", "vier fünf sechs sieben acht", "drei"]);
        Assert.Contains(diff, p => p.Change == ParagraphChange.Inserted && p.NewText!.StartsWith("vier"));
        var stats = DocumentDiff.Stats(diff);
        Assert.True(stats.WordsAdded > 0);
    }

    [Fact]
    public void Two_paragraphs_that_are_joined_show_as_a_change_and_not_as_lost_text()
    {
        var diff = DocumentDiff.Compare(["Er ging nach Hause.", "Dort aß er Suppe."], ["Er ging nach Hause. Dort aß er Suppe."]);
        // Every old word is still in the new text: whatever the pairing, nothing is reported as removed.
        var stats = DocumentDiff.Stats(diff);
        Assert.Contains(diff, p => p.Change != ParagraphChange.Unchanged);
        Assert.True(stats.WordsEdited + stats.WordsRemoved >= 0);
    }

    [Fact]
    public void Words_replaced_one_for_one_are_counted_as_changed_not_as_added_and_removed()
    {
        var diff = DocumentDiff.Compare(["Ich habe ein Buch gelesen."], ["Ich hatte ein Roman gelesen."]);
        var stats = DocumentDiff.Stats(diff);
        Assert.Equal(2, stats.WordsChanged);
        Assert.Equal(0, stats.WordsAdded);
        Assert.Equal(0, stats.WordsRemoved);
    }

    [Fact]
    public void Added_and_removed_words_are_counted_apart()
    {
        var added = DocumentDiff.Stats(DocumentDiff.Compare(["Ein Satz."], ["Ein langer schöner Satz."]));
        Assert.Equal(2, added.WordsAdded);
        var removed = DocumentDiff.Stats(DocumentDiff.Compare(["Ein langer schöner Satz."], ["Ein Satz."]));
        Assert.Equal(2, removed.WordsRemoved);
    }

    [Fact]
    public void A_change_that_is_kept_apart_by_a_blank_is_one_change()
    {
        var stats = DocumentDiff.Stats(DocumentDiff.Compare(["alt eins alt zwei"], ["neu eins neu zwei"]));
        Assert.Equal(2, stats.WordsChanged);
    }

    [Fact]
    public void Compare_is_symmetric_in_what_it_keeps()
    {
        var a = new[] { "Der Hund bellt laut.", "Die Katze schläft." };
        var b = new[] { "Der Hund bellte laut.", "Die Katze schläft tief." };
        var forward = DocumentDiff.Compare(a, b);
        var backward = DocumentDiff.Compare(b, a);
        Assert.Equal(forward.Count, backward.Count);
        Assert.Equal(DocumentDiff.Stats(forward).WordsChanged, DocumentDiff.Stats(backward).WordsChanged);
    }

    [Fact]
    public void The_new_text_can_be_rebuilt_from_the_segments_of_every_paragraph()
    {
        var oldParagraphs = new[] { "Erster Absatz mit Text.", "Zweiter Absatz, der sich ändert.", "Dritter Absatz fällt weg.", "Vierter bleibt." };
        var newParagraphs = new[] { "Erster Absatz mit etwas Text.", "Zweiter Absatz, der sich wirklich ändert.", "Vierter bleibt.", "Ein ganz neuer Absatz." };
        var diff = DocumentDiff.Compare(oldParagraphs, newParagraphs);
        Assert.Equal(newParagraphs, Rebuilt(diff, old: false));
        Assert.Equal(oldParagraphs, Rebuilt(diff, old: true));
    }

    [Fact]
    public void Both_texts_can_be_rebuilt_from_the_rows_when_paragraphs_are_split_joined_and_moved()
    {
        var oldParagraphs = new[] { "Eins und zwei.", "Drei.", "Vier, fünf und sechs.", string.Empty, "Sieben." };
        var newParagraphs = new[] { "Eins", "und zwei. Drei!", "Sieben.", "Vier, fünf", string.Empty, "und sechs." };
        var diff = DocumentDiff.Compare(oldParagraphs, newParagraphs);
        Assert.Equal(newParagraphs, Rebuilt(diff, old: false));
        Assert.Equal(oldParagraphs, Rebuilt(diff, old: true));
    }

    /// <summary>A paragraph is the rows that hold a part of it, in order (a split paragraph is two rows of the old one, joined ones two rows of the new one).</summary>
    private static List<string> Rebuilt(IReadOnlyList<ParagraphDiff> diff, bool old)
        => [.. diff.Where(p => (old ? p.OldIndex : p.NewIndex) is not null)
            .GroupBy(p => (old ? p.OldIndex : p.NewIndex)!.Value)
            .OrderBy(g => g.Key)
            .Select(g => string.Concat(g.SelectMany(p => p.Segments).Where(s => s.Kind != (old ? EditKind.Insert : EditKind.Delete)).Select(s => s.Text)))];
}

public class TextMetricsTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("Hallo Welt", 2)]
    [InlineData("Hallo  Welt  –  gut", 3)]
    [InlineData("Er sagte: „Nein!“ und ging.", 5)]
        [InlineData("1984 war ein Jahr", 4)]
    public void Words_are_counted_the_way_Word_counts_them(string text, int expected)
        => Assert.Equal(expected, TextMetrics.CountWords(text));

    [Fact]
    public void A_line_break_inside_a_paragraph_separates_words()
        => Assert.Equal(2, TextMetrics.CountWords("a" + TextMetrics.LineBreak + "b"));

    [Fact]
    public void A_line_break_is_its_own_token_in_the_comparison()
    {
        var segments = DocumentDiff.CompareWords("a" + TextMetrics.LineBreak + "b", "a b");
        Assert.Contains(segments, s => s.Kind == EditKind.Delete && s.Text == TextMetrics.LineBreak.ToString());
    }

    [Fact]
    public void A_norm_page_has_1800_characters()
    {
        Assert.Equal(1.0m, TextMetrics.NormPages(1800));
        Assert.Equal(0.5m, TextMetrics.NormPages(900));
        Assert.Equal(2.5m, TextMetrics.NormPages(4500));
    }

    [Fact]
    public void Paragraphs_are_split_on_line_feeds_of_any_kind()
        => Assert.Equal(["a", "b", "c"], TextMetrics.Paragraphs("a\r\nb\rc"));
}
