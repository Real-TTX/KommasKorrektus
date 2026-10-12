using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;

namespace KommasKorrektus.Core.Documents.Layout;

/// <summary>The fonts and colours of the document theme, for the runs that name a theme font or colour instead of a value.</summary>
internal sealed class ThemeBook
{
    private readonly Dictionary<string, string> _colors = [];

    private string? _major;

    private string? _minor;

    public static ThemeBook Read(MainDocumentPart main)
    {
        var book = new ThemeBook();
        try
        {
            var theme = main.ThemePart?.Theme;
            if (theme is null)
            {
                return book;
            }

            foreach (var element in theme.Descendants())
            {
                switch (element.LocalName)
                {
                    case "clrScheme":
                        foreach (var slot in element.ChildElements)
                        {
                            var color = slot.ChildElements.FirstOrDefault();
                            var value = color?.Attr("val") ?? color?.Attr("lastClr");
                            if (LayoutXml.HexColor(value) is { } hex)
                            {
                                book._colors[slot.LocalName] = hex;
                            }
                        }

                        break;
                    case "majorFont":
                        book._major = element.Child("latin").Attr("typeface");
                        break;
                    case "minorFont":
                        book._minor = element.Child("latin").Attr("typeface");
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or OpenXmlPackageException or InvalidDataException)
        {
            // A damaged theme is only a missing nicety.
        }

        return book;
    }

    public string? Font(string themeName) => themeName.StartsWith("major", StringComparison.Ordinal) ? _major : _minor;

    public string? Color(string themeName)
    {
        var slot = themeName switch
        {
            "text1" or "dark1" => "dk1",
            "background1" or "light1" => "lt1",
            "text2" or "dark2" => "dk2",
            "background2" or "light2" => "lt2",
            "hyperlink" => "hlink",
            "followedHyperlink" => "folHlink",
            _ => themeName,
        };
        return _colors.GetValueOrDefault(slot);
    }
}

/// <summary>What a table style (and its conditional parts: first row, banded rows ...) adds to a cell.</summary>
internal sealed class CellFmt
{
    public ParaFmt Para { get; private set; } = new();

    public RunFmt Run { get; private set; } = new();

    public string? Shade { get; set; }

    public BorderSet Borders { get; private set; } = new();

    public void Apply(CellFmt? over)
    {
        if (over is null)
        {
            return;
        }

        Para.Apply(over.Para);
        Run.Apply(over.Run);
        Shade = over.Shade ?? Shade;
        Borders.Apply(over.Borders);
    }

    public CellFmt Clone() => new() { Para = Para.Clone(), Run = Run.Clone(), Shade = Shade, Borders = Borders.Clone() };
}

/// <summary>A table style with its base styles worked in.</summary>
internal sealed class TableStyle
{
    public CellFmt Whole { get; } = new();

    public BorderSet Borders { get; } = new();

    /// <summary>Cell padding in points: top, left, bottom, right.</summary>
    public (double? Top, double? Left, double? Bottom, double? Right) Margins { get; set; }

    public Dictionary<string, CellFmt> Conditional { get; } = [];

    /// <summary>The look of a cell: the whole-table part, then bands, first / last column, first / last row (later ones win).</summary>
    public CellFmt For(int row, int column, int rows, int columns, TableLook look)
    {
        var cell = Whole.Clone();
        void Add(string type)
        {
            if (Conditional.TryGetValue(type, out var part))
            {
                cell.Apply(part);
            }
        }

        var firstRow = look.FirstRow && row == 0;
        var lastRow = look.LastRow && row == rows - 1 && rows > 1;
        var firstColumn = look.FirstColumn && column == 0;
        var lastColumn = look.LastColumn && column == columns - 1 && columns > 1;

        if (!look.NoHBand && !firstRow && !lastRow)
        {
            var index = row - (look.FirstRow ? 1 : 0);
            Add(index % 2 == 0 ? "band1Horz" : "band2Horz");
        }

        if (!look.NoVBand && !firstColumn && !lastColumn)
        {
            var index = column - (look.FirstColumn ? 1 : 0);
            Add(index % 2 == 0 ? "band1Vert" : "band2Vert");
        }

        if (firstColumn)
        {
            Add("firstCol");
        }

        if (lastColumn)
        {
            Add("lastCol");
        }

        if (firstRow)
        {
            Add("firstRow");
        }

        if (lastRow)
        {
            Add("lastRow");
        }

        return cell;
    }
}

/// <summary>Which special parts of a table style a table asks for (<c>w:tblLook</c>).</summary>
internal readonly record struct TableLook(bool FirstRow, bool LastRow, bool FirstColumn, bool LastColumn, bool NoHBand, bool NoVBand)
{
    public static TableLook Read(OpenXmlElement? look)
    {
        if (look is null)
        {
            return new TableLook(true, false, true, false, false, true);
        }

        // Word writes the flags as attributes, older files as one hexadecimal mask.
        var mask = look.Attr("val") is { } text && int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        bool Flag(string name, int bit) => look.Attr(name) is { } value ? value is "1" or "true" or "on" : (mask & bit) != 0;
        return new TableLook(Flag("firstRow", 0x20), Flag("lastRow", 0x40), Flag("firstColumn", 0x80), Flag("lastColumn", 0x100), Flag("noHBand", 0x200), Flag("noVBand", 0x400));
    }
}

/// <summary>The styles of the document: paragraph, character and table styles with their base styles, and the document defaults they all start from.</summary>
internal sealed class StyleBook
{
    private sealed class Raw(string id, string type, string? basedOn, OpenXmlElement element)
    {
        public string Id { get; } = id;

        public string Type { get; } = type;

        public string? BasedOn { get; } = basedOn;

        public OpenXmlElement Element { get; } = element;

        public ParaFmt? Para { get; set; }

        public RunFmt? Run { get; set; }
    }

    private readonly Dictionary<string, Raw> _styles = [];

    private readonly ThemeBook _theme;

    private readonly Dictionary<string, TableStyle> _tables = [];

    public ParaFmt DefaultPara { get; }

    public RunFmt DefaultRun { get; }

    public string? DefaultParagraphStyle { get; }

    public string? DefaultTableStyle { get; }

    public StyleBook(MainDocumentPart main, ThemeBook theme)
    {
        _theme = theme;
        var styles = main.StyleDefinitionsPart?.Styles;
        var defaults = styles.Child("docDefaults");
        DefaultRun = RunFmt.Read(defaults.Child("rPrDefault").Child("rPr"), theme);
        DefaultPara = ParaFmt.Read(defaults.Child("pPrDefault").Child("pPr"), theme);

        // What Word assumes for a document that says nothing.
        DefaultRun.SizePt ??= 10;
        DefaultRun.Font ??= "Times New Roman";

        foreach (var style in styles.Children("style"))
        {
            if (style.Attr("styleId") is not { } id)
            {
                continue;
            }

            var type = style.Attr("type") ?? "paragraph";
            _styles[id] = new Raw(id, type, style.Child("basedOn").Val(), style);
            if (style.Attr("default") is "1" or "true" or "on")
            {
                if (type == "paragraph")
                {
                    DefaultParagraphStyle = id;
                }
                else if (type == "table")
                {
                    DefaultTableStyle = id;
                }
            }
        }
    }

    private List<Raw> Chain(string? id)
    {
        var chain = new List<Raw>();
        var seen = new HashSet<string>();
        while (id is not null && _styles.TryGetValue(id, out var raw) && seen.Add(id))
        {
            chain.Insert(0, raw);
            id = raw.BasedOn;
        }

        return chain;
    }

    private ParaFmt ParaOf(Raw raw) => raw.Para ??= ParaFmt.Read(raw.Element.Child("pPr"), _theme);

    private RunFmt RunOf(Raw raw) => raw.Run ??= RunFmt.Read(raw.Element.Child("rPr"), _theme);

    /// <summary>What a paragraph of this style looks like before its own properties: defaults, the table it sits in, then the style and its base styles.</summary>
    public (ParaFmt Para, RunFmt Run) Paragraph(string? styleId, CellFmt? cell)
    {
        var para = DefaultPara.Clone();
        var run = DefaultRun.Clone();
        if (cell is not null)
        {
            para.Apply(cell.Para);
            run.Apply(cell.Run);
        }

        var id = styleId is not null && _styles.ContainsKey(styleId) ? styleId : DefaultParagraphStyle;
        foreach (var raw in Chain(id))
        {
            para.Apply(ParaOf(raw));
            run.Apply(RunOf(raw));
        }

        return (para, run);
    }

    public string? ResolveParagraphStyleId(string? styleId) => styleId is not null && _styles.ContainsKey(styleId) ? styleId : DefaultParagraphStyle;

    public RunFmt Character(string id)
    {
        var run = new RunFmt();
        foreach (var raw in Chain(id))
        {
            run.Apply(RunOf(raw));
        }

        return run;
    }

    public TableStyle Table(string? styleId)
    {
        var id = styleId is not null && _styles.ContainsKey(styleId) ? styleId : DefaultTableStyle;
        if (id is null)
        {
            return new TableStyle();
        }

        if (_tables.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var table = new TableStyle();
        foreach (var raw in Chain(id))
        {
            var element = raw.Element;
            var props = element.Child("tblPr");
            if (BorderSet.Read(props.Child("tblBorders")) is { } borders)
            {
                table.Borders.Apply(borders);
            }

            var margins = props.Child("tblCellMar");
            if (margins is not null)
            {
                table.Margins = (
                    margins.Child("top").Points("w") ?? table.Margins.Top,
                    (margins.Child("left") ?? margins.Child("start")).Points("w") ?? table.Margins.Left,
                    margins.Child("bottom").Points("w") ?? table.Margins.Bottom,
                    (margins.Child("right") ?? margins.Child("end")).Points("w") ?? table.Margins.Right);
            }

            table.Whole.Para.Apply(ParaFmt.Read(element.Child("pPr"), _theme));
            table.Whole.Run.Apply(RunFmt.Read(element.Child("rPr"), _theme));

            foreach (var part in element.Children("tblStylePr"))
            {
                if (part.Attr("type") is not { } type)
                {
                    continue;
                }

                if (!table.Conditional.TryGetValue(type, out var cell))
                {
                    cell = new CellFmt();
                    table.Conditional[type] = cell;
                }

                cell.Para.Apply(ParaFmt.Read(part.Child("pPr"), _theme));
                cell.Run.Apply(RunFmt.Read(part.Child("rPr"), _theme));
                var cellProps = part.Child("tcPr");
                cell.Shade = LayoutXml.HexColor(cellProps.Child("shd").Attr("fill")) ?? cell.Shade;
                if (BorderSet.Read(cellProps.Child("tcBorders")) is { } cellBorders)
                {
                    cell.Borders.Apply(cellBorders);
                }
            }
        }

        _tables[id] = table;
        return table;
    }
}

/// <summary>The number or bullet in front of a list paragraph, and the indentation its level gives.</summary>
internal sealed record ListMarker(string Text, string Suffix, ParaFmt Para, RunFmt Run);

/// <summary>
/// List numbering: counts the paragraphs of every list as the document is walked, so that "1.", "1.1", "a)" and bullets come out as Word draws them.
/// </summary>
internal sealed class NumberingBook
{
    private sealed class Level
    {
        public int Start { get; set; } = 1;

        public string Format { get; set; } = "decimal";

        public string Text { get; set; } = "";

        public string Suffix { get; set; } = "tab";

        public string? ParagraphStyle { get; set; }

        public bool Legal { get; set; }

        public ParaFmt Para { get; set; } = new();

        public RunFmt Run { get; set; } = new();
    }

    private sealed class Abstract
    {
        public Level?[] Levels { get; } = new Level?[9];
    }

    private sealed class Instance(int abstractId)
    {
        public int AbstractId { get; } = abstractId;

        public Dictionary<int, int> StartOverrides { get; } = [];

        public int[] Counters { get; } = new int[9];
    }

    private readonly Dictionary<int, Abstract> _abstracts = [];

    private readonly Dictionary<int, Instance> _instances = [];

    public NumberingBook(MainDocumentPart main, ThemeBook theme)
    {
        var numbering = main.NumberingDefinitionsPart?.Numbering;
        foreach (var abstractNum in numbering.Children("abstractNum"))
        {
            if (abstractNum.Attr("abstractNumId").Integer() is not { } id)
            {
                continue;
            }

            var abs = new Abstract();
            foreach (var lvl in abstractNum.Children("lvl"))
            {
                var index = lvl.Attr("ilvl").Integer();
                if (index is null or < 0 or > 8)
                {
                    continue;
                }

                abs.Levels[index.Value] = new Level
                {
                    Start = lvl.Child("start").Val().Integer() ?? 1,
                    Format = lvl.Child("numFmt").Val() ?? "decimal",
                    Text = lvl.Child("lvlText").Val() ?? "",
                    Suffix = lvl.Child("suff").Val() ?? "tab",
                    ParagraphStyle = lvl.Child("pStyle").Val(),
                    Legal = lvl.Child("isLgl") is not null,
                    Para = ParaFmt.Read(lvl.Child("pPr"), theme),
                    Run = RunFmt.Read(lvl.Child("rPr"), theme),
                };
            }

            _abstracts[id] = abs;
        }

        foreach (var num in numbering.Children("num"))
        {
            if (num.Attr("numId").Integer() is not { } numId || num.Child("abstractNumId").Val().Integer() is not { } abstractId)
            {
                continue;
            }

            var instance = new Instance(abstractId);
            foreach (var over in num.Children("lvlOverride"))
            {
                if (over.Attr("ilvl").Integer() is { } level && over.Child("startOverride").Val().Integer() is { } start)
                {
                    instance.StartOverrides[level] = start;
                }
            }

            _instances[numId] = instance;
        }
    }

    /// <summary>The level a paragraph style is tied to in the list (a heading style in a numbered outline), when the list says so.</summary>
    public int? LevelOfStyle(int numId, string? styleId)
    {
        if (styleId is null || !_instances.TryGetValue(numId, out var instance) || !_abstracts.TryGetValue(instance.AbstractId, out var abs))
        {
            return null;
        }

        for (var i = 0; i < abs.Levels.Length; i++)
        {
            if (abs.Levels[i]?.ParagraphStyle == styleId)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>The marker of the next paragraph at this level of this list (counting it), or null when the list does not exist.</summary>
    public ListMarker? Next(int numId, int level)
    {
        if (!_instances.TryGetValue(numId, out var instance) || !_abstracts.TryGetValue(instance.AbstractId, out var abs))
        {
            return null;
        }

        level = Math.Clamp(level, 0, 8);
        var current = abs.Levels[level];
        if (current is null)
        {
            return null;
        }

        instance.Counters[level]++;
        for (var deeper = level + 1; deeper < 9; deeper++)
        {
            instance.Counters[deeper] = 0;
        }

        var text = current.Format == "bullet" ? Bullet(current.Text) : Expand(current, abs, instance, level);
        return new ListMarker(text, current.Format == "none" ? "nothing" : current.Suffix, current.Para, current.Run);
    }

    private string Expand(Level current, Abstract abs, Instance instance, int level)
    {
        var sb = new StringBuilder();
        var text = current.Text;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%' || i + 1 >= text.Length || !char.IsAsciiDigit(text[i + 1]))
            {
                sb.Append(text[i]);
                continue;
            }

            var target = text[i + 1] - '1';
            i++;
            if (target is < 0 or > 8 || abs.Levels[target] is not { } source)
            {
                continue;
            }

            var start = instance.StartOverrides.TryGetValue(target, out var over) ? over : source.Start;
            // A level above that never appeared counts from its start, as Word does.
            var count = Math.Max(instance.Counters[target], 1);
            var format = current.Legal && target < level ? "decimal" : source.Format;
            sb.Append(Format(start + count - 1, format));
        }

        return sb.ToString();
    }

    private static string Bullet(string text)
    {
        if (text.Length == 0)
        {
            return "•";
        }

        // Bullets are characters of a symbol font (Symbol, Wingdings) that a browser does not have: the common ones as their Unicode look-alikes.
        return (int)text[0] switch
        {
            0xF0B7 or 0xF0A8 => "•",
            0xF0A7 => "▪",
            0xF0D8 => "➢",
            0xF076 => "❖",
            0xF0FC => "✓",
            0xF0FB => "✗",
            0xF06E => "■",
            0x6F => "○",
            >= 0xF000 and <= 0xF0FF => "•",
            _ => text[..1],
        };
    }

    public static string Format(int value, string format) => format switch
    {
        "decimalZero" => value.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
        "lowerLetter" => Letters(value, upper: false),
        "upperLetter" => Letters(value, upper: true),
        "lowerRoman" => Roman(value).ToLowerInvariant(),
        "upperRoman" => Roman(value),
        "none" => "",
        _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>a, b ... z, aa, bb ... (Word repeats the letter instead of counting on).</summary>
    private static string Letters(int value, bool upper)
    {
        if (value < 1)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var letter = (char)((upper ? 'A' : 'a') + ((value - 1) % 26));
        return new string(letter, ((value - 1) / 26) + 1);
    }

    private static string Roman(int value)
    {
        if (value is < 1 or > 3999)
        {
            return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var sb = new StringBuilder();
        ReadOnlySpan<(int Value, string Text)> map = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        foreach (var (number, text) in map)
        {
            while (value >= number)
            {
                sb.Append(text);
                value -= number;
            }
        }

        return sb.ToString();
    }
}
