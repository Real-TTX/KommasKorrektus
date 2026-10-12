using KommasKorrektus.Data.Entities;
using Microsoft.AspNetCore.Html;

namespace KommasKorrektus.UiKit;

/// <summary>The German words for the enums of the data model, and the tone (colour) of a status pill.</summary>
public static class Labels
{
    public static string Of(ProjectStatus s) => s switch
    {
        ProjectStatus.Inquiry => "Anfrage",
        ProjectStatus.Sample => "Probelektorat",
        ProjectStatus.Offered => "Angebot",
        ProjectStatus.Commissioned => "Beauftragt",
        ProjectStatus.InProgress => "In Bearbeitung",
        ProjectStatus.Review => "Rückgabe",
        ProjectStatus.Done => "Abgeschlossen",
        ProjectStatus.Archived => "Archiviert",
        _ => "Abgesagt",
    };

    public static string Tone(ProjectStatus s) => s switch
    {
        ProjectStatus.Inquiry or ProjectStatus.Sample or ProjectStatus.Offered => "info",
        ProjectStatus.Commissioned => "accent",
        ProjectStatus.InProgress => "busy",
        ProjectStatus.Review => "warn",
        ProjectStatus.Done => "ok",
        _ => "plain",
    };

    public static string Of(ServiceType s) => s switch
    {
        ServiceType.Proofreading => "Korrektorat",
        ServiceType.Copyediting => "Lektorat",
        ServiceType.Consulting => "Manuskriptberatung",
        ServiceType.Sample => "Probelektorat",
        ServiceType.Formatting => "Formatierung / Satz",
        _ => "Sonstiges",
    };

    public static string Of(PricingModel p) => p switch
    {
        PricingModel.PerNormPage => "Pro Normseite",
        PricingModel.PerWord => "Pro Wort",
        PricingModel.Hourly => "Nach Stunden",
        _ => "Pauschal",
    };

    public static string Of(Priority p) => p switch
    {
        Priority.Low => "Niedrig",
        Priority.High => "Hoch",
        _ => "Normal",
    };

    public static string Of(TaskState s) => s switch
    {
        TaskState.Open => "Offen",
        TaskState.InProgress => "In Arbeit",
        _ => "Erledigt",
    };

    public static string Tone(TaskState s) => s switch
    {
        TaskState.InProgress => "busy",
        TaskState.Done => "ok",
        _ => "",
    };

    public static string Of(CustomerKind k) => k == CustomerKind.Company ? "Firma / Verlag" : "Privatperson";

    public static string Of(UserRole r) => r switch
    {
        UserRole.Admin => "Administrator",
        UserRole.User => "Lektorat (Benutzer)",
        _ => "Kunde (Portal)",
    };

    public static string Of(HistoryKind k) => k switch
    {
        HistoryKind.Call => "Telefonat",
        HistoryKind.Mail => "E-Mail",
        HistoryKind.Meeting => "Gespräch",
        HistoryKind.System => "System",
        _ => "Notiz",
    };

    public static string Icon(HistoryKind k) => k switch
    {
        HistoryKind.Call => "smartphone",
        HistoryKind.Mail => "mail",
        HistoryKind.Meeting => "users",
        HistoryKind.System => "activity",
        _ => "edit",
    };

    public static string Of(StyleCategory c) => c switch
    {
        StyleCategory.Character => "Figur",
        StyleCategory.Place => "Ort",
        StyleCategory.Spelling => "Schreibweise",
        StyleCategory.Term => "Begriff",
        StyleCategory.Rule => "Regel",
        _ => "Sonstiges",
    };

    public static string Of(DocType t) => t switch
    {
        DocType.Offer => "Angebot",
        DocType.Invoice => "Rechnung",
        _ => "Gutschrift",
    };

    public static string Plural(DocType t) => t switch
    {
        DocType.Offer => "Angebote",
        DocType.Invoice => "Rechnungen",
        _ => "Gutschriften",
    };

    public static string Of(DocStatus s, DocType type = DocType.Invoice) => s switch
    {
        DocStatus.Draft => "Entwurf",
        DocStatus.Issued => type == DocType.Offer ? "Versendet" : "Gestellt",
        DocStatus.Accepted => "Angenommen",
        DocStatus.Declined => "Abgelehnt",
        DocStatus.PartiallyPaid => "Teilweise bezahlt",
        DocStatus.Paid => "Bezahlt",
        _ => "Storniert",
    };

    public static string Tone(DocStatus s) => s switch
    {
        DocStatus.Draft => "",
        DocStatus.Issued => "info",
        DocStatus.Accepted or DocStatus.Paid => "ok",
        DocStatus.PartiallyPaid => "warn",
        DocStatus.Declined => "error",
        _ => "plain",
    };

    public static string Of(PaymentMethod m) => m switch
    {
        PaymentMethod.Transfer => "Überweisung",
        PaymentMethod.Cash => "Bar",
        PaymentMethod.PayPal => "PayPal",
        _ => "Sonstige",
    };

    public static string Of(TextBlockKind k) => k switch
    {
        TextBlockKind.Intro => "Einleitung",
        TextBlockKind.Outro => "Schluss",
        TextBlockKind.Mail => "E-Mail",
        _ => "Zahlungserinnerung",
    };

    public static string Of(TimeEntryKind k) => k switch
    {
        TimeEntryKind.Auto => "Automatisch",
        TimeEntryKind.Timer => "Stoppuhr",
        _ => "Manuell",
    };

    public static string Of(VersionOrigin o) => o switch
    {
        VersionOrigin.Import => "Original",
        VersionOrigin.Working => "In Arbeit",
        VersionOrigin.Restore => "Wiederhergestellt",
        _ => "Abgeschlossen",
    };

    public static string Of(DocumentKind k) => k switch
    {
        DocumentKind.Docx => "Word",
        DocumentKind.Pdf => "PDF",
        _ => "Text",
    };

    public static string Of(BackupScope s) => s == BackupScope.SystemOnly ? "System (Datenbank und Einstellungen)" : "System und Projektdateien";

    public static string Of(ScheduleKind k) => k switch
    {
        ScheduleKind.Hourly => "Stündlich",
        ScheduleKind.Daily => "Täglich",
        ScheduleKind.Weekly => "Wöchentlich",
        _ => "Monatlich",
    };

    public static string Of(MailStatus s) => s switch
    {
        MailStatus.Sent => "Gesendet",
        MailStatus.Failed => "Fehlgeschlagen",
        _ => "Nicht gesendet (kein Server)",
    };

    public static string Of(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        _ => "Sonntag",
    };

    /// <summary>A status as a pill.</summary>
    public static IHtmlContent Pill(ProjectStatus s) => Ui.Pill(Of(s), Tone(s));

    public static IHtmlContent Pill(TaskState s) => Ui.Pill(Of(s), Tone(s));

    public static IHtmlContent Pill(DocStatus s, DocType type = DocType.Invoice) => Ui.Pill(Of(s, type), Tone(s));
}
