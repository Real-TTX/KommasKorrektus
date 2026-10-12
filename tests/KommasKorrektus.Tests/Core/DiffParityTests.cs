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
/// (<see cref="DocumentDiff"/>: Word export, statistics, the other views) have to give the same answer for any two texts, or the same text looks different in two places.
/// </summary>
public sealed class DiffParityTests
{
    private static readonly string[] Pool =
    [
        "der", "die", "das", "und", "Haus", "Garten", "über", "Brücke", "wurde", "schnell", "Ost-West", "gibt's", "3,5", "1.200", "2026", "Nebel", "Wiese", "Ofen", "Katze",
        ",", ".", ";", ":", "—", "„Hallo“", "(Klammer)", "…", "Kommata", "Fehler", "korrekt",
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

    private static string Paragraph(Random random)
    {
        var count = random.Next(0, 14);
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            sb.Append(i == 0 ? string.Empty : random.Next(12) == 0 ? "  " : random.Next(25) == 0 ? " " : " ").Append(Pool[random.Next(Pool.Length)]);
        }

        return sb.ToString();
    }

    private static (string[] Old, string[] New) Case(Random random)
    {
        var old = new List<string>();
        var count = random.Next(0, 16);
        for (var i = 0; i < count; i++)
        {
            old.Add(old.Count > 0 && random.Next(8) == 0 ? old[random.Next(old.Count)] : Paragraph(random));
        }

        var now = old.ToList();
        for (var step = random.Next(0, 7); step > 0; step--)
        {
            var at = now.Count == 0 ? 0 : random.Next(now.Count);
            switch (random.Next(7))
            {
                case 0 when now.Count > 0:
                    now.RemoveAt(at);
                    break;
                case 1:
                    now.Insert(random.Next(now.Count + 1), random.Next(4) == 0 && now.Count > 0 ? now[random.Next(now.Count)] : Paragraph(random));
                    break;
                case 2 when now.Count > 0:
                    var words = now[at].Split(' ').ToList();
                    for (var k = random.Next(1, 4); k > 0; k--)
                    {
                        var p = random.Next(words.Count + 1);
                        if (random.Next(2) == 0 && words.Count > 0)
                        {
                            words.RemoveAt(Math.Min(p, words.Count - 1));
                        }
                        else
                        {
                            words.Insert(p, Pool[random.Next(Pool.Length)]);
                        }
                    }

                    now[at] = string.Join(' ', words);
                    break;
                case 3 when now.Count > 0:
                    var parts = now[at].Split(' ');
                    var cut = random.Next(parts.Length + 1);
                    now[at] = string.Join(' ', parts.Take(cut));
                    now.Insert(at + 1, string.Join(' ', parts.Skip(cut)));
                    break;
                case 4 when at + 1 < now.Count:
                    now[at] = now[at] + " " + now[at + 1];
                    now.RemoveAt(at + 1);
                    break;
                case 5 when now.Count > 1:
                    var other = random.Next(now.Count);
                    (now[at], now[other]) = (now[other], now[at]);
                    break;
                case 6 when now.Count > 0:
                    now[at] = Paragraph(random);
                    break;
            }
        }

        return ([.. old], [.. now]);
    }

    /// <summary>The way the browser writes an answer: kind, the paragraph of the old and the new text, and for a changed one the ranges of the words that differ.</summary>
    private static string Line(ParagraphDiff p)
    {
        var kind = p.Change switch { ParagraphChange.Unchanged => "same", ParagraphChange.Modified => "mod", ParagraphChange.Inserted => "ins", _ => "del" };
        var line = $"{kind}:{p.OldIndex?.ToString() ?? "-"}/{p.NewIndex?.ToString() ?? "-"}";
        if (p.Change != ParagraphChange.Modified)
        {
            return line;
        }

        List<string> del = [], ins = [];
        int oldAt = 0, newAt = 0;
        foreach (var s in p.Segments)
        {
            switch (s.Kind)
            {
                case EditKind.Equal:
                    oldAt += s.Text.Length;
                    newAt += s.Text.Length;
                    break;
                case EditKind.Delete:
                    del.Add($"[{oldAt},{oldAt + s.Text.Length}]");
                    oldAt += s.Text.Length;
                    break;
                default:
                    ins.Add($"[{newAt},{newAt + s.Text.Length}]");
                    newAt += s.Text.Length;
                    break;
            }
        }

        return $"{line} del=[{string.Join(',', del)}] ins=[{string.Join(',', ins)}]";
    }

    [NodeFact]
    public void The_browser_and_the_server_compare_alike()
    {
        var random = new Random(2026);
        var cases = Enumerable.Range(0, 400).Select(_ => Case(random)).ToList();
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
            Assert.True(node.WaitForExit(60_000), "node did not finish");
            Assert.True(node.ExitCode == 0, errors.GetAwaiter().GetResult());

            var browser = JsonSerializer.Deserialize<List<List<string>>>(output)!;
            Assert.Equal(cases.Count, browser.Count);
            for (var i = 0; i < cases.Count; i++)
            {
                var server = DocumentDiff.Compare(cases[i].Old, cases[i].New).Select(Line).ToList();
                Assert.True(server.SequenceEqual(browser[i]),
                    $"Case {i} differs.\nOld: {JsonSerializer.Serialize(cases[i].Old)}\nNew: {JsonSerializer.Serialize(cases[i].New)}\nServer:  {string.Join(" | ", server)}\nBrowser: {string.Join(" | ", browser[i])}");
            }
        }
        finally
        {
            File.Delete(file);
        }

    }
}
