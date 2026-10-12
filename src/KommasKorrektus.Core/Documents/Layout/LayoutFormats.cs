using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;

namespace KommasKorrektus.Core.Documents.Layout;

/// <summary>A line of a border (paragraph, table or cell) as CSS.</summary>
internal sealed record Border(string Style, double WidthPx, string? Color)
{
    public string Css => Style == "none" ? "none" : $"{LayoutXml.Px(WidthPx)} {Style} {Color ?? "#000"}";

    public static Border? Read(OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Val();
        if (value is null or "nil" or "none")
        {
            return new Border("none", 0, null);
        }

        var style = value switch
        {
            "double" or "triple" or "thinThickSmallGap" or "thickThinSmallGap" => "double",
            "dotted" => "dotted",
            "dashed" or "dashSmallGap" or "dotDash" or "dotDotDash" => "dashed",
            _ => "solid",
        };
        var eighths = element.Attr("sz").Number() ?? 4;
        var width = Math.Max(1, eighths / 8 * 96 / 72);
        if (style == "double")
        {
            width = Math.Max(3, width);
        }

        return new Border(style, Math.Round(width, 2), LayoutXml.HexColor(element.Attr("color")));
    }
}

/// <summary>The four sides (and, for tables, the lines inside) a border can be given to.</summary>
internal sealed class BorderSet
{
    public Border? Top { get; set; }

    public Border? Left { get; set; }

    public Border? Bottom { get; set; }

    public Border? Right { get; set; }

    public Border? InsideH { get; set; }

    public Border? InsideV { get; set; }

    public BorderSet Clone() => (BorderSet)MemberwiseClone();

    public bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null && InsideH is null && InsideV is null;

    public void Apply(BorderSet? over)
    {
        if (over is null)
        {
            return;
        }

        Top = over.Top ?? Top;
        Left = over.Left ?? Left;
        Bottom = over.Bottom ?? Bottom;
        Right = over.Right ?? Right;
        InsideH = over.InsideH ?? InsideH;
        InsideV = over.InsideV ?? InsideV;
    }

    public static BorderSet? Read(OpenXmlElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new BorderSet
        {
            Top = Border.Read(element.Child("top")),
            Left = Border.Read(element.Child("left") ?? element.Child("start")),
            Bottom = Border.Read(element.Child("bottom")),
            Right = Border.Read(element.Child("right") ?? element.Child("end")),
            InsideH = Border.Read(element.Child("insideH")),
            InsideV = Border.Read(element.Child("insideV")),
        };
    }
}

/// <summary>Font names as a CSS font stack: the name from the document first, then a generic family so that a missing font still looks about right.</summary>
internal static partial class LayoutFonts
{
    [GeneratedRegex(@"[^\p{L}\p{N} _.\-]")]
    private static partial Regex Unsafe();

    [GeneratedRegex(@"mono|courier|consolas|menlo|monaco|lucida console|fixed|typewriter", RegexOptions.IgnoreCase)]
    private static partial Regex Monospace();

    [GeneratedRegex(@"times|georgia|garamond|cambria|palatino|book antiqua|bookman|century|minion|baskerville|didot|constantia|caslon|sabon|bembo|charter|utopia|libertin|merriweather|lora|serif$|^serif", RegexOptions.IgnoreCase)]
    private static partial Regex Serif();

    public static string? Stack(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var clean = Unsafe().Replace(name, string.Empty).Trim();
        if (clean.Length == 0)
        {
            return null;
        }

        var generic = Monospace().IsMatch(clean) ? "monospace" : Serif().IsMatch(clean) && !clean.Contains("sans", StringComparison.OrdinalIgnoreCase) ? "serif" : "sans-serif";
        return $"'{clean}',{generic}";
    }
}

/// <summary>The formatting of the text of a run, as far as the page view shows it. Every property is "not said" (null) until a style or the run says it.</summary>
internal sealed class RunFmt
{
    public bool? Bold { get; set; }

    public bool? Italic { get; set; }

    public bool? Strike { get; set; }

    public bool? DoubleStrike { get; set; }

    public bool? Caps { get; set; }

    public bool? SmallCaps { get; set; }

    public bool? Hidden { get; set; }

    /// <summary>Word's underline kind (single, double, dotted, wave ...) or "none".</summary>
    public string? Underline { get; set; }

    public string? Color { get; set; }

    public string? Highlight { get; set; }

    public string? Shade { get; set; }

    public double? SizePt { get; set; }

    public string? Font { get; set; }

    public double? SpacingPt { get; set; }

    public string? VertAlign { get; set; }

    /// <summary>The character style the run names; resolved by the renderer, not part of the CSS.</summary>
    public string? CharStyle { get; set; }

    public RunFmt Clone() => (RunFmt)MemberwiseClone();

    public void Apply(RunFmt? over)
    {
        if (over is null)
        {
            return;
        }

        Bold = over.Bold ?? Bold;
        Italic = over.Italic ?? Italic;
        Strike = over.Strike ?? Strike;
        DoubleStrike = over.DoubleStrike ?? DoubleStrike;
        Caps = over.Caps ?? Caps;
        SmallCaps = over.SmallCaps ?? SmallCaps;
        Hidden = over.Hidden ?? Hidden;
        Underline = over.Underline ?? Underline;
        Color = over.Color ?? Color;
        Highlight = over.Highlight ?? Highlight;
        Shade = over.Shade ?? Shade;
        SizePt = over.SizePt ?? SizePt;
        Font = over.Font ?? Font;
        SpacingPt = over.SpacingPt ?? SpacingPt;
        VertAlign = over.VertAlign ?? VertAlign;
        CharStyle = over.CharStyle ?? CharStyle;
    }

    private static string? HighlightColor(string? name) => name switch
    {
        "yellow" => "#ffff00",
        "green" => "#00ff00",
        "cyan" => "#00ffff",
        "magenta" => "#ff00ff",
        "blue" => "#0000ff",
        "red" => "#ff0000",
        "darkBlue" => "#000080",
        "darkCyan" => "#008080",
        "darkGreen" => "#008000",
        "darkMagenta" => "#800080",
        "darkRed" => "#800000",
        "darkYellow" => "#808000",
        "darkGray" => "#808080",
        "lightGray" => "#c0c0c0",
        "black" => "#000000",
        "white" => "#ffffff",
        _ => null,
    };

    /// <summary>Reads the children of a run properties element (of a run, a style or the document defaults).</summary>
    public static RunFmt Read(OpenXmlElement? properties, ThemeBook theme)
    {
        var fmt = new RunFmt();
        if (properties is null)
        {
            return fmt;
        }

        foreach (var child in properties.ChildElements)
        {
            switch (child.LocalName)
            {
                case "b": fmt.Bold = child.OnOff(); break;
                case "i": fmt.Italic = child.OnOff(); break;
                case "strike": fmt.Strike = child.OnOff(); break;
                case "dstrike": fmt.DoubleStrike = child.OnOff(); break;
                case "caps": fmt.Caps = child.OnOff(); break;
                case "smallCaps": fmt.SmallCaps = child.OnOff(); break;
                case "vanish": fmt.Hidden = child.OnOff(); break;
                case "u": fmt.Underline = child.Val() ?? "single"; break;
                case "rStyle": fmt.CharStyle = child.Val(); break;
                case "vertAlign": fmt.VertAlign = child.Val(); break;
                case "highlight": fmt.Highlight = HighlightColor(child.Val()); break;
                case "sz": fmt.SizePt = child.Val().Number() / 2; break;
                case "spacing": fmt.SpacingPt = child.Val().Number() / 20; break;
                case "color":
                    // The value already has a tint or shade worked in; the theme colour is the fallback when there is none.
                    fmt.Color = LayoutXml.HexColor(child.Val()) ?? (child.Attr("themeColor") is { } themed ? theme.Color(themed) : null);
                    break;
                case "shd":
                    fmt.Shade = LayoutXml.HexColor(child.Attr("fill")) ?? (child.Attr("themeFill") is { } fill ? theme.Color(fill) : null);
                    break;
                case "rFonts":
                    var themeFont = child.Attr("asciiTheme") ?? child.Attr("hAnsiTheme");
                    fmt.Font = (themeFont is null ? null : theme.Font(themeFont)) ?? child.Attr("ascii") ?? child.Attr("hAnsi") ?? child.Attr("eastAsia");
                    break;
            }
        }

        return fmt;
    }

    /// <summary>The declarations for what differs from <paramref name="parent"/> (null: everything that is said).</summary>
    public string Css(RunFmt? parent)
    {
        var sb = new StringBuilder();
        static bool Differs<T>(T? mine, T? theirs) where T : struct => mine is not null && !EqualityComparer<T?>.Default.Equals(mine, theirs);
        static bool DiffersText(string? mine, string? theirs) => mine is not null && mine != theirs;

        if (Differs(Bold, parent?.Bold))
        {
            sb.Append(Bold == true ? "font-weight:bold;" : "font-weight:normal;");
        }

        if (Differs(Italic, parent?.Italic))
        {
            sb.Append(Italic == true ? "font-style:italic;" : "font-style:normal;");
        }

        if (DiffersText(Underline, parent?.Underline) || Differs(Strike, parent?.Strike) || Differs(DoubleStrike, parent?.DoubleStrike))
        {
            var lines = new List<string>();
            if (Underline is not null and not "none")
            {
                lines.Add("underline");
            }

            if (Strike == true || DoubleStrike == true)
            {
                lines.Add("line-through");
            }

            if (lines.Count == 0)
            {
                sb.Append("text-decoration:none;");
            }
            else
            {
                var style = DoubleStrike == true || Underline is "double" ? "double" : Underline is "dotted" or "dottedHeavy" ? "dotted" : Underline is "dash" or "dashedHeavy" or "dashLong" or "dashLongHeavy" ? "dashed" : Underline is "wave" or "wavyHeavy" or "wavyDouble" ? "wavy" : "solid";
                sb.Append("text-decoration-line:").Append(string.Join(' ', lines)).Append(";text-decoration-style:").Append(style).Append(';');
            }
        }

        if (Differs(Caps, parent?.Caps))
        {
            sb.Append(Caps == true ? "text-transform:uppercase;" : "text-transform:none;");
        }

        if (Differs(SmallCaps, parent?.SmallCaps))
        {
            sb.Append(SmallCaps == true ? "font-variant-caps:small-caps;" : "font-variant-caps:normal;");
        }

        if (Differs(Hidden, parent?.Hidden) && Hidden == true)
        {
            // Hidden text counts as text, so it must stay where it can be seen; Word's "show hidden text" draws it dotted.
            sb.Append("opacity:.55;border-bottom:1px dotted currentColor;");
        }

        if (DiffersText(Color, parent?.Color))
        {
            sb.Append("color:").Append(Color).Append(';');
        }

        var background = Highlight ?? Shade;
        if (DiffersText(background, parent?.Highlight ?? parent?.Shade))
        {
            sb.Append("background-color:").Append(background).Append(';');
        }

        if (DiffersText(Font, parent?.Font) && LayoutFonts.Stack(Font) is { } stack)
        {
            sb.Append("font-family:").Append(stack).Append(';');
        }

        var raised = VertAlign is "superscript" or "subscript";
        var parentRaised = parent?.VertAlign is "superscript" or "subscript";
        if (raised)
        {
            if (!parentRaised)
            {
                var size = SizePt ?? parent?.SizePt;
                sb.Append(VertAlign == "superscript" ? "vertical-align:super;" : "vertical-align:sub;");
                sb.Append(size is { } s ? $"font-size:{LayoutXml.Pt(s * 0.65)};" : "font-size:.65em;");
            }
        }
        else if (Differs(SizePt, parent?.SizePt))
        {
            sb.Append("font-size:").Append(LayoutXml.Pt(SizePt!.Value)).Append(';');
        }

        if (Differs(SpacingPt, parent?.SpacingPt))
        {
            sb.Append("letter-spacing:").Append(LayoutXml.Pt(SpacingPt!.Value)).Append(';');
        }

        return sb.ToString();
    }
}

/// <summary>The formatting of a paragraph as the page view shows it.</summary>
internal sealed class ParaFmt
{
    public string? Align { get; set; }

    public double? LeftPt { get; set; }

    public double? RightPt { get; set; }

    /// <summary>The first line against the left indent: positive for an indented first line, negative for a hanging indent.</summary>
    public double? FirstLinePt { get; set; }

    public double? BeforePt { get; set; }

    public double? AfterPt { get; set; }

    /// <summary>The line height as a factor of the font size, when Word says "multiple".</summary>
    public double? LineFactor { get; set; }

    /// <summary>The line height in points, when Word says "exactly" or "at least".</summary>
    public double? LinePt { get; set; }

    public bool? Contextual { get; set; }

    public bool? PageBreakBefore { get; set; }

    public bool? Bidi { get; set; }

    public string? Shade { get; set; }

    public BorderSet? Borders { get; set; }

    public int? NumId { get; set; }

    public int? NumLevel { get; set; }

    public ParaFmt Clone()
    {
        var copy = (ParaFmt)MemberwiseClone();
        copy.Borders = Borders?.Clone();
        return copy;
    }

    public void Apply(ParaFmt? over)
    {
        if (over is null)
        {
            return;
        }

        Align = over.Align ?? Align;
        LeftPt = over.LeftPt ?? LeftPt;
        RightPt = over.RightPt ?? RightPt;
        FirstLinePt = over.FirstLinePt ?? FirstLinePt;
        BeforePt = over.BeforePt ?? BeforePt;
        AfterPt = over.AfterPt ?? AfterPt;
        if (over.LineFactor is not null || over.LinePt is not null)
        {
            LineFactor = over.LineFactor;
            LinePt = over.LinePt;
        }

        Contextual = over.Contextual ?? Contextual;
        PageBreakBefore = over.PageBreakBefore ?? PageBreakBefore;
        Bidi = over.Bidi ?? Bidi;
        Shade = over.Shade ?? Shade;
        NumId = over.NumId ?? NumId;
        NumLevel = over.NumLevel ?? NumLevel;
        if (over.Borders is not null)
        {
            Borders ??= new BorderSet();
            Borders.Apply(over.Borders);
        }
    }

    public static ParaFmt Read(OpenXmlElement? properties, ThemeBook theme)
    {
        var fmt = new ParaFmt();
        if (properties is null)
        {
            return fmt;
        }

        foreach (var child in properties.ChildElements)
        {
            switch (child.LocalName)
            {
                case "jc":
                    fmt.Align = child.Val() switch
                    {
                        "center" => "center",
                        "right" or "end" => "right",
                        "both" or "distribute" or "thaiDistribute" or "highKashida" => "justify",
                        "left" or "start" => "left",
                        _ => null,
                    };
                    break;
                case "ind":
                    fmt.LeftPt = child.Points("left") ?? child.Points("start");
                    fmt.RightPt = child.Points("right") ?? child.Points("end");
                    fmt.FirstLinePt = child.Points("hanging") is { } hanging ? -hanging : child.Points("firstLine");
                    break;
                case "spacing":
                    fmt.BeforePt = child.Points("before");
                    fmt.AfterPt = child.Points("after");
                    if (child.Attr("line").Number() is { } line)
                    {
                        if (child.Attr("lineRule") is "exact" or "atLeast")
                        {
                            fmt.LinePt = line / 20;
                        }
                        else
                        {
                            fmt.LineFactor = line / 240;
                        }
                    }

                    break;
                case "contextualSpacing": fmt.Contextual = child.OnOff(); break;
                case "pageBreakBefore": fmt.PageBreakBefore = child.OnOff(); break;
                case "bidi": fmt.Bidi = child.OnOff(); break;
                case "shd":
                    fmt.Shade = LayoutXml.HexColor(child.Attr("fill")) ?? (child.Attr("themeFill") is { } fill ? theme.Color(fill) : null);
                    break;
                case "pBdr": fmt.Borders = BorderSet.Read(child); break;
                case "numPr":
                    fmt.NumId = child.Child("numId").Val().Integer();
                    fmt.NumLevel = child.Child("ilvl").Val().Integer();
                    break;
            }
        }

        return fmt;
    }

    public string Css()
    {
        var sb = new StringBuilder();
        if (Align is not null)
        {
            sb.Append("text-align:").Append(Align).Append(';');
        }

        if (LeftPt is { } left)
        {
            sb.Append("margin-left:").Append(LayoutXml.Pt(left)).Append(';');
        }

        if (RightPt is { } right)
        {
            sb.Append("margin-right:").Append(LayoutXml.Pt(right)).Append(';');
        }

        if (FirstLinePt is { } first)
        {
            sb.Append("text-indent:").Append(LayoutXml.Pt(first)).Append(';');
        }

        // Word adds the space after one paragraph to the space before the next, where CSS margins would collapse: padding adds up like Word does.
        if (BeforePt is { } before)
        {
            sb.Append("padding-top:").Append(LayoutXml.Pt(before)).Append(";--kk-pt:").Append(LayoutXml.Pt(before)).Append(';');
        }

        if (AfterPt is { } after)
        {
            sb.Append("padding-bottom:").Append(LayoutXml.Pt(after)).Append(';');
        }

        if (LinePt is { } linePt)
        {
            sb.Append("line-height:").Append(LayoutXml.Pt(linePt)).Append(';');
        }
        else if (LineFactor is { } factor)
        {
            sb.Append("line-height:").Append(LayoutXml.Num(factor)).Append(';');
        }

        if (Shade is not null)
        {
            sb.Append("background-color:").Append(Shade).Append(';');
        }

        if (Bidi == true)
        {
            sb.Append("direction:rtl;");
        }

        if (Borders is { } b)
        {
            AppendSide(sb, "top", b.Top);
            AppendSide(sb, "left", b.Left);
            AppendSide(sb, "bottom", b.Bottom);
            AppendSide(sb, "right", b.Right);
        }

        return sb.ToString();
    }

    private static void AppendSide(StringBuilder sb, string side, Border? border)
    {
        if (border is not null)
        {
            sb.Append("border-").Append(side).Append(':').Append(border.Css).Append(';');
        }
    }
}
