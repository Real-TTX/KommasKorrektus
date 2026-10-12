using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Html;

namespace KommasKorrektus.UiKit;

/// <summary>The version of this build: "1.0.42-20261007", "nightly-42-20261007" or "local-20261007" (see Directory.Build.props).</summary>
public static class AppVersion
{
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? "local" : informational.Split('+')[0];
    }
}

/// <summary>Small helpers for the markup of the toolkit: icons, avatars, status pills.</summary>
public static partial class Ui
{
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NameRegex();

    /// <summary>An icon from the page's inline sprite. The name must exist in <c>matui-icons.svg</c> or in the app's own sprite.</summary>
    public static IHtmlContent Icon(string name, string? extraClass = null)
    {
        if (!NameRegex().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not an icon name.", nameof(name));
        }

        var cls = string.IsNullOrEmpty(extraClass) ? "mat-icon" : $"mat-icon {extraClass}";
        return new HtmlString($"<svg class=\"{cls}\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-{name}\"></use></svg>");
    }

    /// <summary>The logo tile: the comma with the witch hat on the accent colour of the app.</summary>
    public static IHtmlContent Logo(string size = "md")
        => new HtmlString(
            $"<span class=\"mat-logo mat-logo--{size} kk-logo\" role=\"img\" aria-label=\"KommasKorrektus\">" +
            "<svg class=\"mat-icon\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-kk-logo\"></use></svg></span>");

    /// <summary>The first letters of a name, for an avatar.</summary>
    public static string Initials(string name)
    {
        var letters = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return letters.Length == 0 ? "?" : letters;
    }

    /// <summary>A status as a pill; <paramref name="tone"/> is ok, warn, error, info, accent or empty.</summary>
    public static IHtmlContent Pill(string text, string tone = "")
    {
        var cls = string.IsNullOrEmpty(tone) ? "mat-pill" : $"mat-pill mat-pill--{tone}";
        return new HtmlString($"<span class=\"{cls}\">{WebUtility.HtmlEncode(text)}</span>");
    }

    /// <summary>Encoded text for a table cell; empty for null.</summary>
    public static IHtmlContent Text(string? text) => new HtmlString(WebUtility.HtmlEncode(text ?? string.Empty));

    /// <summary>A link in a table cell.</summary>
    public static IHtmlContent Link(string text, string href)
        => new HtmlString($"<a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(text)}</a>");

    /// <summary>A name with a second, quieter line below it.</summary>
    public static IHtmlContent NameWithSub(string text, string? href, string? sub)
    {
        var name = href is null ? WebUtility.HtmlEncode(text) : $"<a href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(text)}</a>";
        var small = string.IsNullOrWhiteSpace(sub) ? "" : $"<small class=\"mat-muted kk-sub\">{WebUtility.HtmlEncode(sub)}</small>";
        return new HtmlString($"<div class=\"mat-table__name kk-name\"><span>{name}</span>{small}</div>");
    }

    public static IHtmlContent Mono(string? text) => new HtmlString($"<span class=\"mat-mono\">{WebUtility.HtmlEncode(text ?? string.Empty)}</span>");

    private static readonly Dictionary<string, string> Singulars = new(StringComparer.Ordinal)
    {
        ["Einträge"] = "Eintrag", ["Pläne"] = "Plan", ["Ziele"] = "Ziel", ["Einladungen"] = "Einladung", ["Nachrichten"] = "Nachricht", ["Kunden"] = "Kunde",
        ["Belege"] = "Beleg", ["Produkte"] = "Produkt", ["Projekte"] = "Projekt", ["Aufgaben"] = "Aufgabe", ["Vorlagen"] = "Vorlage", ["Textbausteine"] = "Textbaustein",
        ["Rechnungen"] = "Rechnung", ["Angebote"] = "Angebot", ["Dokumente"] = "Dokument", ["Fassungen"] = "Fassung",
    };

    /// <summary>The name of the things in a list for a count: "1 Kunde", "3 Kunden". Names that are not known keep their plural.</summary>
    public static string CountNoun(long count, string plural) => $"{Services.Fmt.Integer(count)} {(count == 1 && Singulars.TryGetValue(plural, out var one) ? one : plural)}";

    /// <summary>A phrase with a count in German: "1 Projekt", "3 Projekte".</summary>
    public static string Plural(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
