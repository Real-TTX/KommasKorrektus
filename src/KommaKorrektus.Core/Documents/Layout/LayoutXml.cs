using System.Globalization;
using DocumentFormat.OpenXml;

namespace KommaKorrektus.Core.Documents.Layout;

/// <summary>
/// Small helpers to read WordprocessingML by element and attribute name. The formatting parts of a document (styles, numbering, table styles) are read this
/// way instead of by the typed classes: the names are the ones of the standard, and an element the SDK does not know is read the same as one it does.
/// </summary>
internal static class LayoutXml
{
    public static OpenXmlElement? Child(this OpenXmlElement? element, string local)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var child in element.ChildElements)
        {
            if (child.LocalName == local)
            {
                return child;
            }
        }

        return null;
    }

    public static IEnumerable<OpenXmlElement> Children(this OpenXmlElement? element, string local)
        => element is null ? [] : element.ChildElements.Where(c => c.LocalName == local);

    public static string? Attr(this OpenXmlElement? element, string local)
    {
        if (element is null)
        {
            return null;
        }

        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName == local)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>The "val" attribute of a property element.</summary>
    public static string? Val(this OpenXmlElement? element) => element.Attr("val");

    /// <summary>An on/off property: present without a value or with 1 / true / on is on; 0 / false / off is off; absent is null (not said).</summary>
    public static bool? OnOff(this OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return element.Attr("val") is not ("0" or "false" or "off");
    }

    public static double? Number(this string? text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static int? Integer(this string? text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>A length in twentieths of a point (twips), as points.</summary>
    public static double? Points(this OpenXmlElement? element, string local) => element.Attr(local).Number() / 20;

    /// <summary>A colour as CSS: six hex digits, "auto" and anything else give null. Only digits are taken, so that nothing else can reach a stylesheet.</summary>
    public static string? HexColor(string? value)
    {
        if (value is not { Length: 6 })
        {
            return null;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return null;
            }
        }

        return "#" + value.ToLowerInvariant();
    }

    public static string Pt(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    public static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
