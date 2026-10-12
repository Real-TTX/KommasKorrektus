using System.Diagnostics;
using System.Runtime.InteropServices;
using KommasKorrektus.Configuration;
using KommasKorrektus.Data;
using KommasKorrektus.Services;
using KommasKorrektus.Services.Backup;
using KommasKorrektus.UiKit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KommasKorrektus.Pages.Admin;

/// <summary>What the installation is made of and how it is doing: version, runtime, data volume, database, and the tables that are cleaned up by the maintenance.</summary>
public sealed class SystemModel(KkDbContext db, ConfigStore config, TimeProvider clock, ActivityLogger log) : PageModelBase
{
    public sealed record Folder(string Name, string Hint, long Bytes, int Files);

    public string Version => AppVersion.Current;

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string Os => RuntimeInformation.OSDescription;

    public string Architecture => RuntimeInformation.OSArchitecture.ToString();

    public DateTime Started => Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public long Memory => Process.GetCurrentProcess().WorkingSet64;

    public string DataDir => config.DataDir;

    public string? DatabaseVersion { get; private set; }

    public long DatabaseBytes { get; private set; }

    public List<Folder> Folders { get; private set; } = [];

    public Dictionary<string, int> Counts { get; private set; } = [];

    public string TimeZone => Fmt.Zone.Id;

    public bool MailConfigured => config.Current.Mail is { Enabled: true, Host.Length: > 0 };

    public StorageMode Storage => config.Current.Storage.Mode;

    public bool BackupBusy => BackupService.IsBusy;

    public async Task OnGetAsync()
    {
        Head("System", "Version, Speicher und Datenbank.", ("Verwaltung", null), ("System", null));
        var ct = HttpContext.RequestAborted;

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        try
        {
            await using var version = connection.CreateCommand();
            version.CommandText = "SELECT version()";
            DatabaseVersion = System.Text.RegularExpressions.Regex.Match((string?)await version.ExecuteScalarAsync(ct) ?? "", @"^PostgreSQL [\d.]+").Value;
            await using var size = connection.CreateCommand();
            size.CommandText = "SELECT pg_database_size(current_database())";
            DatabaseBytes = Convert.ToInt64(await size.ExecuteScalarAsync(ct));
        }
        finally
        {
            await connection.CloseAsync();
        }

        Counts = new Dictionary<string, int>
        {
            ["Benutzer"] = await db.User.CountAsync(ct),
            ["Kunden"] = await db.Customer.CountAsync(ct),
            ["Projekte"] = await db.Project.CountAsync(ct),
            ["Dokumente"] = await db.Document.CountAsync(ct),
            ["Fassungen"] = await db.DocumentVersion.CountAsync(ct),
            ["Rechnungen und Angebote"] = await db.Invoice.CountAsync(ct),
            ["Dateien"] = await db.StoredFile.CountAsync(ct),
            ["Freigabe-Links"] = await db.ShareLink.CountAsync(ct),
        };

        Folders =
        [
            Measure("files", "Dateien", "Dokumente und PDFs (bei Speicher auf dem NAS leer)"),
            Measure("keys", "Schlüssel", "Schutz der Cookies und der gespeicherten Passwörter"),
            Measure("config", "Einstellungen", "app.json"),
            Measure("backups", "Sicherungen im Volume", "Lokale Ziele und Sicherheitskopien"),
            Measure("tmp", "Temporär", "Zwischenablage für Sicherungen"),
        ];
    }

    /// <summary>Removes old sessions, used codes and old logs now (the maintenance does the same once a day).</summary>
    public async Task<IActionResult> OnPostMaintainAsync()
    {
        var result = await MaintenanceService.RunAsync(db, clock, HttpContext.RequestAborted);
        await log.LogAsync("Aufräumen", "System", null, $"{result.Total} Einträge entfernt");
        Flash(result.Total == 0 ? "Es gab nichts aufzuräumen." : $"{Ui.Plural(result.Total, "alter Eintrag", "alte Einträge")} entfernt.");
        return RedirectToPage();
    }

    private Folder Measure(string name, string title, string hint)
    {
        var path = Path.Combine(config.DataDir, name);
        if (!Directory.Exists(path))
        {
            return new Folder(title, hint, 0, 0);
        }

        long bytes = 0;
        var count = 0;
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            bytes += f.Length;
            count++;
        }

        return new Folder(title, hint, bytes, count);
    }
}
