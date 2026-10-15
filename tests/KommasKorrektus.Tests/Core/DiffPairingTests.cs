using KommasKorrektus.Core.Diff;

namespace KommasKorrektus.Tests.Core;

/// <summary>
/// Which old paragraph is which new one when something was put in, split, joined or moved: the comparison goes on with the old text after text that was put in between, a paragraph
/// that was split is the old paragraph with a break put in, joined ones are a break taken away. A row is <c>kind:old/new</c>, with the mark at its end when the break did not stay
/// (I put in, D taken away). The same cases are in tests/js/textdiff.test.mjs (the browser does the same).
/// </summary>
public class DiffPairingTests
{
    private static string Kinds(string[] old, string[] now)
        => string.Join(' ', DocumentDiff.Compare(old, now).Select(p =>
        {
            var kind = p.Change switch { ParagraphChange.Unchanged => "same", ParagraphChange.Modified => "mod", ParagraphChange.Inserted => "ins", _ => "del" };
            var mark = p.Mark switch { EditKind.Insert => "I", EditKind.Delete => "D", _ => string.Empty };
            return $"{kind}:{p.OldIndex?.ToString() ?? "-"}/{p.NewIndex?.ToString() ?? "-"}{mark}";
        }));

    private static List<string> Places(string[] old, string[] now)
        => [.. TextDiff.Compare(old, now).Changes.Select(c => $"{c.OldText.Replace(TextDiff.Sep, '¶')}>{c.NewText.Replace(TextDiff.Sep, '¶')}")];

    [Fact]
    public void A_paragraph_that_was_split_with_a_new_paragraph_in_between_goes_on_after_it()
    {
        string[] old = ["Kapitel 1", "Der Rabe kam zurück und krächzte laut. Dann flog er davon und die Hexe blieb allein im Haus.", "Ende."];
        string[] now = ["Kapitel 1", "Der Rabe kam zurück und krächzte laut.", "Ein ganz neuer Absatz, der die Stimmung beschreibt und vom Wetter erzählt.", "Dann flog er davon und die Hexe blieb allein im Haus.", "Ende."];

        // the old paragraph is two rows: its first part with a break put in, and the rest of it, unchanged, after the new paragraph
        Assert.Equal("same:0/0 mod:1/1I ins:-/2I mod:1/3 same:2/4", Kinds(old, now));
        Assert.Equal([" >¶Ein ganz neuer Absatz, der die Stimmung beschreibt und vom Wetter erzählt.¶"], Places(old, now));
    }

    [Fact]
    public void A_paragraph_split_in_two_is_a_break_put_in_and_the_words_changed_after_it()
    {
        string[] old = ["Der Rabe kam zurück und krächzte laut. Dann flog er davon und die Hexe blieb allein.", "Ende."];
        string[] now = ["Der Rabe kam zurück und krächzte laut.", "Dann flog er fort, und die Hexe blieb allein.", "Ende."];

        Assert.Equal("mod:0/0I mod:0/1 same:1/2", Kinds(old, now));
        Assert.Equal([" >¶", "davon>fort,"], Places(old, now));
    }

    [Fact]
    public void Paragraphs_that_were_joined_are_a_break_taken_away()
    {
        string[] old = ["Der Rabe kam zurück.", "Er krächzte laut.", "Ende."];
        string[] now = ["Der Rabe kam zurück. Er krächzte sehr laut.", "Ende."];

        Assert.Equal("mod:0/0D mod:1/0 same:2/1", Kinds(old, now));
        Assert.Equal(["¶> ", "> sehr"], Places(old, now));
    }

    [Fact]
    public void Text_put_in_at_the_end_of_a_paragraph_and_a_new_paragraph_after_it_are_two_places()
    {
        const string Longer = " Er hatte eine lange Reise hinter sich, über die Berge und durch die Täler, vorbei an den Dörfern und den Flüssen, und er erzählte der Hexe alles, was er gesehen hatte, bis es dunkel wurde.";
        string[] old = ["Eins.", "Der Rabe kam zurück.", "Ende."];
        string[] now = ["Eins.", "Der Rabe kam zurück." + Longer, "Und noch ein ganz neuer Absatz über den Wind.", "Ende."];

        Assert.Equal("same:0/0 mod:1/1 ins:-/2I same:2/3", Kinds(old, now));
        Assert.Equal([">" + Longer, ">Und noch ein ganz neuer Absatz über den Wind.¶"], Places(old, now));
    }

    [Fact]
    public void The_full_stop_stays_with_its_paragraph_when_text_went_in_before_it_and_a_paragraph_after_it()
    {
        string[] old = ["Eins.", "Er kam zurück und schwieg lange.", "Ende."];
        string[] now = ["Eins.", "Er kam zurück und schwieg lange vor dem Feuer.", "Ein anderer Absatz.", "Ende."];

        Assert.Equal("same:0/0 mod:1/1 ins:-/2I same:2/3", Kinds(old, now));
        Assert.Equal(["> vor dem Feuer", ">Ein anderer Absatz.¶"], Places(old, now));
    }

    [Fact]
    public void A_sentence_put_in_the_middle_of_a_long_paragraph_is_one_place_in_it()
    {
        string[] old = ["Kapitel 1", "Der Rabe kam zurück und krächzte laut. Dann flog er davon und die Hexe blieb allein im Haus, lange nachdem die Sonne untergegangen war.", "Ende."];
        string[] now = ["Kapitel 1", "Der Rabe kam zurück und krächzte laut. Er war müde. Dann flog er davon und die Hexe blieb allein im Haus, lange nachdem die Sonne untergegangen war.", "Ende."];

        Assert.Equal("same:0/0 mod:1/1 same:2/2", Kinds(old, now));
        Assert.Equal(["> Er war müde."], Places(old, now));
    }

    [Fact]
    public void A_paragraph_that_was_moved_is_taken_away_at_one_place_and_put_in_at_the_other()
    {
        string[] old = ["Erster Absatz über den Wald.", "Zweiter Absatz über die Hexe.", "Dritter Absatz über den Raben."];
        string[] now = ["Zweiter Absatz über die Hexe.", "Dritter Absatz über den Raben.", "Erster Absatz über den Wald."];

        Assert.Equal("del:0/-D same:1/0 same:2/1 ins:-/2I", Kinds(old, now));
    }

    [Fact]
    public void Empty_paragraphs_are_no_anchors_for_the_paragraphs_with_text()
    {
        string[] old = ["A", string.Empty, "B", string.Empty, "C"];
        string[] now = ["A", string.Empty, "B", string.Empty, "Neu eingefügt.", string.Empty, "C"];

        Assert.Equal("same:0/0 same:1/1 same:2/2 same:3/3 ins:-/4I ins:-/5I same:4/6", Kinds(old, now));
    }

    [Fact]
    public void A_paragraph_split_with_many_empty_paragraphs_goes_on_with_its_text_after_them()
    {
        // what happened in the editor: Enter pressed many times in the middle of a word, a few letters typed in between
        string[] old = ["Am nächsten Morgen klopfte es.", "„Ein Komma kann Leben retten“ sagte sie, und sie öffnete den Brief noch einmal, langsam, als könnte er sich verändert haben.", "Darin stand: Komm schnell."];
        string[] now = ["Am nächsten Morgen klopfte es.", "„Ein Komma kann Leben retten“ sagte sie, und sie öffnete den Brief noch einmal, lan,", string.Empty, string.Empty, "ööä", string.Empty, "gsam, als könnte er sich verändert haben.", "Darin stand: Komm schnell."];

        Assert.Equal([">,¶¶¶ööä¶¶"], Places(old, now));
        var rest = DocumentDiff.Compare(old, now).Single(p => p.NewIndex == 6);
        Assert.Equal(1, rest.OldIndex);
        Assert.All(rest.Segments, s => Assert.Equal(EditKind.Equal, s.Kind));
    }
}
