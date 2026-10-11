using KommaKorrektus.Configuration;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Data;
using KommaKorrektus.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KommaKorrektus.Services;

/// <summary>
/// What a new installation starts with, so that the first offer is a matter of minutes: task lists for the usual kinds of work, the usual products of a
/// freelance editor, ready-made texts for offers and invoices and a first invoice template. Each part is written only while its table is empty,
/// so what the user changed or deleted later never comes back.
/// </summary>
public static class DefaultData
{
    public static async Task EnsureAsync(KkDbContext db, ConfigStore config, ILogger logger, CancellationToken ct = default)
    {
        var changed = false;
        if (!await db.TaskTemplate.IgnoreQueryFilters().AnyAsync(ct))
        {
            db.TaskTemplate.AddRange(TaskTemplates());
            changed = true;
        }

        if (!await db.Product.IgnoreQueryFilters().AnyAsync(ct))
        {
            var products = Products(config.Current.Company.DefaultVatRate);
            db.Product.AddRange(products);
            if (!await db.NumberSequence.AnyAsync(s => s.Key == nameof(NumberKind.Product), ct))
            {
                db.NumberSequence.Add(new NumberSequence { Key = nameof(NumberKind.Product), NextValue = products.Count + 1 });
            }

            changed = true;
        }

        if (!await db.TextBlock.IgnoreQueryFilters().AnyAsync(ct))
        {
            db.TextBlock.AddRange(TextBlocks());
            changed = true;
        }

        if (!await db.InvoiceTemplate.IgnoreQueryFilters().AnyAsync(ct))
        {
            db.InvoiceTemplate.Add(new InvoiceTemplate { Name = "Standard", IsDefault = true, SettingsJson = InvoiceTemplateSettings.Default.ToJson() });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("The default data was written.");
        }
    }

    private static List<TaskTemplate> TaskTemplates() =>
    [
        Template("Korrektorat", ServiceType.Proofreading, "Rechtschreibung, Grammatik und Zeichensetzung.",
            ("Manuskript prüfen und Umfang bestätigen", 30, 0),
            ("Stilblatt anlegen (Namen, Schreibweisen, Regeln)", 30, 1),
            ("Korrekturdurchgang", 480, 7),
            ("Konsistenz prüfen (Namen, Schreibweisen, Zahlen)", 90, 8),
            ("Dokument mit Änderungen an die Autorin / den Autor übergeben", 20, 10),
            ("Rechnung stellen", 15, 10),
            ("Nachbesprechung", 30, 17)),
        Template("Lektorat", ServiceType.Copyediting, "Stil, Ausdruck, Logik und Sprachfluss, mit anschließendem Korrektorat.",
            ("Manuskript lesen und Umfang bestätigen", 120, 0),
            ("Stilblatt und Namensliste anlegen", 45, 2),
            ("Erster Durchgang: Stil, Ausdruck, Logik", 720, 10),
            ("Zweiter Durchgang: Feinschliff", 480, 17),
            ("Schlusskorrektorat", 300, 21),
            ("Anmerkungen und Gesamtfeedback schreiben", 90, 22),
            ("Dokument mit Änderungen übergeben", 20, 23),
            ("Rechnung stellen", 15, 23),
            ("Nachbesprechung", 45, 30)),
        Template("Probelektorat", ServiceType.Sample, "Eine kurze Probe, damit die Zusammenarbeit zur Geschichte passt.",
            ("Probetext erhalten und prüfen", 15, 0),
            ("Probelektorat durchführen", 90, 3),
            ("Rückmeldung senden und Angebot besprechen", 30, 4)),
        Template("Manuskriptberatung", ServiceType.Consulting, "Ein schriftliches Gutachten zu Handlung, Figuren und Aufbau.",
            ("Manuskript lesen", 360, 7),
            ("Gutachten schreiben (Handlung, Figuren, Struktur, Stil)", 240, 12),
            ("Gutachten versenden", 15, 12),
            ("Gespräch zum Gutachten", 60, 20),
            ("Rechnung stellen", 15, 12)),
    ];

    private static TaskTemplate Template(string name, ServiceType type, string description, params (string Title, int Minutes, int DueOffset)[] items)
        => new()
        {
            Name = name,
            ServiceType = type,
            Description = description,
            Items = [.. items.Select((item, i) => new TaskTemplateItem { Title = item.Title, EstimatedMinutes = item.Minutes, DueOffsetDays = item.DueOffset, SortOrder = i + 1 })],
        };

    private static List<Product> Products(decimal vat) =>
    [
        new() { Number = "A-001", Name = "Korrektorat", Description = "Rechtschreibung, Grammatik, Zeichensetzung", Unit = "Normseite", NetPrice = 1.80m, VatRate = vat, Category = "Lektorat", ServiceType = ServiceType.Proofreading },
        new() { Number = "A-002", Name = "Lektorat", Description = "Stil, Ausdruck, Logik und Sprachfluss, inklusive Korrektorat", Unit = "Normseite", NetPrice = 4.50m, VatRate = vat, Category = "Lektorat", ServiceType = ServiceType.Copyediting },
        new() { Number = "A-003", Name = "Probelektorat", Description = "Bis zu 10 Normseiten", Unit = "pauschal", NetPrice = 0m, VatRate = vat, Category = "Lektorat", ServiceType = ServiceType.Sample },
        new() { Number = "A-004", Name = "Manuskriptberatung", Description = "Schriftliches Gutachten zu Handlung, Figuren und Aufbau", Unit = "pauschal", NetPrice = 280m, VatRate = vat, Category = "Beratung", ServiceType = ServiceType.Consulting },
        new() { Number = "A-005", Name = "Stundensatz", Description = "Nach Aufwand", Unit = "Stunde", NetPrice = 55m, VatRate = vat, Category = "Sonstiges", ServiceType = ServiceType.Other },
        new() { Number = "A-006", Name = "Expresszuschlag", Description = "Bearbeitung mit verkürzter Frist", Unit = "Prozent", NetPrice = 25m, VatRate = vat, Category = "Sonstiges" },
        new() { Number = "A-007", Name = "Formatierung / Satz", Description = "Einrichten des Dokuments nach Vorgabe", Unit = "Stunde", NetPrice = 45m, VatRate = vat, Category = "Sonstiges", ServiceType = ServiceType.Formatting },
    ];

    private static List<TextBlock> TextBlocks() =>
    [
        new() { Name = "Angebot: Einleitung", Kind = TextBlockKind.Intro, DocType = DocType.Offer, IsDefault = true,
            Text = "{{Anrede}}\n\nvielen Dank für Ihre Anfrage und das Vertrauen in meine Arbeit. Gern unterbreite ich Ihnen folgendes Angebot für das Projekt „{{Projekt}}“:" },
        new() { Name = "Angebot: Schluss", Kind = TextBlockKind.Outro, DocType = DocType.Offer, IsDefault = true,
            Text = "Dieses Angebot ist gültig bis {{Gueltig}}. Bei Fragen melde ich mich jederzeit gern. Ich freue mich auf Ihre Geschichte!\n\nHerzliche Grüße" },
        new() { Name = "Rechnung: Einleitung", Kind = TextBlockKind.Intro, DocType = DocType.Invoice, IsDefault = true,
            Text = "{{Anrede}}\n\nvielen Dank für die gute Zusammenarbeit. Für meine Leistungen im Projekt „{{Projekt}}“ stelle ich Ihnen folgende Rechnung:" },
        new() { Name = "Rechnung: Schluss", Kind = TextBlockKind.Outro, DocType = DocType.Invoice, IsDefault = true,
            Text = "Bitte überweisen Sie den Rechnungsbetrag innerhalb von {{Tage}} Tagen bis zum {{Zahlungsziel}} unter Angabe der Rechnungsnummer {{Nummer}} auf das unten genannte Konto.\n\nHerzlichen Dank!" },
        new() { Name = "Gutschrift: Einleitung", Kind = TextBlockKind.Intro, DocType = DocType.CreditNote, IsDefault = true,
            Text = "{{Anrede}}\n\nhiermit erteile ich Ihnen folgende Gutschrift zur Rechnung {{Bezug}}:" },
        new() { Name = "E-Mail: Rechnung", Kind = TextBlockKind.Mail, DocType = DocType.Invoice, IsDefault = true,
            Text = "{{Anrede}}\n\nanbei erhalten Sie die Rechnung {{Nummer}} über {{Betrag}} für das Projekt „{{Projekt}}“.\n\nBitte überweisen Sie den Betrag bis zum {{Zahlungsziel}}.\n\nHerzliche Grüße" },
        new() { Name = "E-Mail: Angebot", Kind = TextBlockKind.Mail, DocType = DocType.Offer, IsDefault = true,
            Text = "{{Anrede}}\n\nanbei erhalten Sie mein Angebot {{Nummer}} für das Projekt „{{Projekt}}“. Es ist gültig bis {{Gueltig}}.\n\nHerzliche Grüße" },
        new() { Name = "Zahlungserinnerung", Kind = TextBlockKind.Reminder, DocType = DocType.Invoice, IsDefault = true,
            Text = "{{Anrede}}\n\nleider konnte ich zur Rechnung {{Nummer}} über {{Betrag}} (fällig am {{Zahlungsziel}}) noch keinen Zahlungseingang feststellen. Vielleicht ist sie in Ihrem Alltag untergegangen. Bitte überweisen Sie den Betrag in den nächsten Tagen.\n\nFalls Sie bereits bezahlt haben, betrachten Sie diese Nachricht als gegenstandslos.\n\nHerzliche Grüße" },
    ];
}
