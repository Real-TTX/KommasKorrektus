using KommasKorrektus.Data.Entities;

namespace KommasKorrektus.Services;

/// <summary>What a version is called in the lists: its name, the round of the ping pong it belongs to, and which pass of the round it is.</summary>
public sealed record VersionName(string Title, int Round, int Pass)
{
    /// <summary>"Fassung 5 – Änderung 2", for a choice of versions.</summary>
    public string Option(int versionNo) => $"Fassung {versionNo} – {Title}";
}

/// <summary>
/// The ping pong between editor and customer in words: the original and every file of the customer begin a round, and the passes of the editor inside it are
/// "Änderung 1", "Änderung 2" ... (unless somebody named them). A version that was put back keeps the name it was given ("Zurück auf Fassung 3").
/// </summary>
public static class VersionNames
{
    public static Dictionary<int, VersionName> Of(IEnumerable<(int No, VersionOrigin Origin, string? Label)> versions)
    {
        var names = new Dictionary<int, VersionName>();
        var round = 0;
        var pass = 0;
        foreach (var (no, origin, label) in versions.OrderBy(v => v.No))
        {
            var named = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            switch (origin)
            {
                case VersionOrigin.Import:
                case VersionOrigin.Upload:
                    round++;
                    pass = 0;
                    names[no] = new VersionName(named ?? (origin == VersionOrigin.Import ? "Original" : "Datei vom Kunden"), round, 0);
                    break;
                case VersionOrigin.Restore:
                    names[no] = new VersionName(named ?? "Wiederhergestellt", Math.Max(1, round), pass);
                    break;
                default:
                    pass++;
                    names[no] = new VersionName(named ?? $"Änderung {pass}", Math.Max(1, round), pass);
                    break;
            }
        }

        return names;
    }
}
