using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KommasKorrektus.Core.Diff;

namespace KommasKorrektus.Tests.Core;

/// <summary>Runs a test only where Node.js is there (the comparison of the browser is JavaScript).</summary>
public sealed class NodeFactAttribute : FactAttribute
{
    public NodeFactAttribute()
    {
        if (!DiffParityTests.NodeAvailable)
        {
            Skip = "node is not installed: the comparison of the browser cannot be run.";
        }
    }
}

/// <summary>
/// The comparison of the browser (<c>wwwroot/js/textdiff.js</c>: the editor beside the earlier version, the comparison of two pages) and the one of the server
/// (<see cref="TextDiff"/>: Word export, statistics, the other views) have to give the same answer for any two texts, or the same text looks different in two places.
/// </summary>
public sealed class DiffParityTests
{
    private static readonly string SoftBreak = ((char)0x2028).ToString();

    private static readonly string NoBreak = ((char)0xA0).ToString();

    private static readonly string[] Pool =
    [
        "der", "die", "das", "dass", "und", "Haus", "Hause", "Garten", "über", "Brücke", "wurde", "schnell", "Ost-West", "gibt's", "3,5", "1.200", "2026", "Nebel", "Wiese",
        "Ofen", "Katze", "hate", "hatte", ",", ".", ";", ":", "—", "„Hallo“", "(Klammer)", "…", "Kommata", "Fehler", "Fahler", "korrekt", "Käuzchen", "ÄRGER", "Straße", "😀",
    ];

    private static readonly Lazy<bool> Node = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            p!.WaitForExit(10_000);
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    });

    public static bool NodeAvailable => Node.Value;

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KommasKorrektus.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found.");
    }

    private static string Paragraph(Random random, int words)
    {
        var count = random.Next(0, words);
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Append(i == 0 ? string.Empty : random.Next(12) == 0 ? "  " : random.Next(25) == 0 ? NoBreak : random.Next(40) == 0 ? SoftBreak : " ").Append(Pool[random.Next(Pool.Length)]);
        }

        return sb.ToString();
    }

    private static (string[] Old, string[] New) Case(Random random, int paragraphs, int words, int steps)
    {
        var old = new List<string>();
        var count = random.Next(0, paragraphs);
        for (var i = 0; i < count; i++)
        {
            old.Add(old.Count > 0 && random.Next(8) == 0 ? old[random.Next(old.Count)] : random.Next(6) == 0 ? string.Empty : Paragraph(random, words));
        }

        var now = old.ToList();
        for (var step = random.Next(0, steps); step > 0; step--)
        {
            var at = now.Count == 0 ? 0 : random.Next(now.Count);
            switch (random.Next(9))
            {
                case 0 when now.Count > 0:
                    now.RemoveAt(at);
                    break;
                case 1:
                    now.Insert(random.Next(now.Count + 1), random.Next(4) == 0 && now.Count > 0 ? now[random.Next(now.Count)] : random.Next(3) == 0 ? string.Empty : Paragraph(random, words));
                    break;
                case 2 when now.Count > 0:
                    var list = now[at].Split(' ').ToList();
                    for (var k = random.Next(1, 4); k > 0; k--)
                    {
                        var p = random.Next(list.Count + 1);
                        if (random.Next(2) == 0 && list.Count > 0)
                        {
                            list.RemoveAt(Math.Min(p, list.Count - 1));
                        }
                        else
                        {
                            list.Insert(p, Pool[random.Next(Pool.Length)]);
                        }
                    }

                    now[at] = string.Join(' ', list);
                    break;
                case 3 when now.Count > 0:
                    var text = now[at];
                    var cut = Whole(text, random.Next(text.Length + 1));
                    now[at] = text[..cut];
                    now.Insert(at + 1, text[cut..]);
                    break;
                case 4 when at + 1 < now.Count:
                    now[at] = now[at] + (random.Next(2) == 0 ? " " : string.Empty) + now[at + 1];
                    now.RemoveAt(at + 1);
                    break;
                case 5 when now.Count > 1:
                    var other = random.Next(now.Count);
                    (now[at], now[other]) = (now[other], now[at]);
                    break;
                case 6 when now.Count > 0:
                    now[at] = Paragraph(random, words);
                    break;
                case 7 when now.Count > 0:
                    // a letter or two typed or taken away inside a word, a comma put in
                    var t = now[at];
                    var place = Whole(t, random.Next(t.Length + 1));
                    now[at] = t[..place] + (random.Next(3) == 0 ? "," : random.Next(2) == 0 ? "e" : string.Empty) + t[Whole(t, Math.Min(t.Length, place + random.Next(0, 3)))..];
                    break;
                case 8 when now.Count > 0:
                    var moved = now[at];
                    now.RemoveAt(at);
                    now.Insert(random.Next(now.Count + 1), moved);
                    break;
            }
        }

        return ([.. old], [.. now]);
    }

    /// <summary>A place in a text that does not cut a character made of two (an emoji): JSON would not carry the half of one.</summary>
    private static int Whole(string text, int at) => at > 0 && at < text.Length && char.IsLowSurrogate(text[at]) ? at - 1 : at;

    private static string Ranges(IEnumerable<(int From, int To)> list) => "[" + string.Join(',', list.Select(r => $"[{r.From},{r.To}]")) + "]";

    private static string At(TextDiff.Position p) => $"{p.P},{p.C}";

    /// <summary>The answer the way the browser writes it (tests/js/parity.mjs).</summary>
    private static List<string> Lines(string[] old, string[] now)
    {
        var r = TextDiff.Compare(old, now);
        var lines = r.Rows.Select(row => $"{row.Kind}:{row.O?.ToString() ?? "-"}/{row.N?.ToString() ?? "-"}:{row.Mark}:{row.OldFrom}-{row.OldTo}/{row.NewFrom}-{row.NewTo} del={Ranges(row.Del)} ins={Ranges(row.Ins)}").ToList();
        lines.AddRange(r.Changes.Select(c => $"c:{At(c.OldFrom)}-{At(c.OldTo)}>{At(c.NewFrom)}-{At(c.NewTo)}"));
        lines.Add("p:" + string.Join(' ', r.Pairs.Select(p => $"{p.Old}/{p.New}")));
        return lines;
    }

    private static List<(string[] Old, string[] New)> Cases()
    {
        var random = new Random(2026);
        var cases = Enumerable.Range(0, 400).Select(_ => Case(random, 16, 14, 7)).ToList();
        // long paragraphs (more than 500 characters: Myers instead of the subsequence)
        cases.AddRange(Enumerable.Range(0, 40).Select(_ => Case(random, 6, 160, 9)));
        // many paragraphs (more than 1700: the paragraphs that occur once are the anchors)
        cases.AddRange(Enumerable.Range(0, 3).Select(_ => Case(random, 2400, 12, 60)));
        // two long texts with little in common
        cases.Add(([Paragraph(random, 900), Paragraph(random, 900)], [Paragraph(random, 900)]));
        // a long text in which many neighbouring words changed places: too much for Myers on the characters, so the words first, then the characters inside every change
        const string Letters = "abcdefghijklmnopqrstuvwxyzäöüß";
        var vocabulary = Enumerable.Range(0, 1500).Select(_ => new string([.. Enumerable.Range(0, random.Next(3, 10)).Select(_ => Letters[random.Next(Letters.Length)])])).ToArray();
        var words = Enumerable.Range(0, 3000).Select(_ => vocabulary[random.Next(vocabulary.Length)]).ToArray();
        var swapped = words.ToArray();
        for (var i = 0; i + 1 < swapped.Length; i++)
        {
            if (random.Next(10) < 3)
            {
                (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
                i++;
            }
        }

        cases.Add(([string.Join(' ', words)], [string.Join(' ', swapped)]));
        return cases;
    }

    [NodeFact]
    public void The_browser_and_the_server_compare_alike()
    {
        var cases = Cases();
        var root = RepositoryRoot();
        var file = Path.Combine(Path.GetTempPath(), $"kk-parity-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(cases.Select(c => new { old = c.Old, @new = c.New })));
        try
        {
            using var node = Process.Start(new ProcessStartInfo("node", $"\"{Path.Combine(root, "tests", "js", "parity.mjs")}\" \"{file}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8,
            })!;
            var errors = node.StandardError.ReadToEndAsync();
            var output = node.StandardOutput.ReadToEnd();
            Assert.True(node.WaitForExit(120_000), "node did not finish");
            Assert.True(node.ExitCode == 0, errors.GetAwaiter().GetResult());

            var browser = JsonSerializer.Deserialize<List<List<string>>>(output)!;
            Assert.Equal(cases.Count, browser.Count);
            for (var i = 0; i < cases.Count; i++)
            {
                var server = Lines(cases[i].Old, cases[i].New);
                if (server.SequenceEqual(browser[i]))
                {
                    continue;
                }

                var first = Enumerable.Range(0, Math.Min(server.Count, browser[i].Count)).FirstOrDefault(k => server[k] != browser[i][k], Math.Min(server.Count, browser[i].Count));
                Assert.Fail($"Case {i} differs at line {first}.\nOld: {Shorten(JsonSerializer.Serialize(cases[i].Old))}\nNew: {Shorten(JsonSerializer.Serialize(cases[i].New))}\n" +
                    $"Server:  {string.Join(" | ", server.Skip(Math.Max(0, first - 2)).Take(5))}\nBrowser: {string.Join(" | ", browser[i].Skip(Math.Max(0, first - 2)).Take(5))}");
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string Shorten(string text) => text.Length > 3000 ? text[..3000] + " …" : text;
}
