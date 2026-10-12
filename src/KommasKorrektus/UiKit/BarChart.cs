using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace KommasKorrektus.UiKit;

/// <summary>One bar: a label for the axis, the value, and the text that says it in words (the tooltip and the screen reader).</summary>
public sealed record Bar(string Label, double Value, string Description);

/// <summary>
/// A bar chart with one series, drawn as SVG (no library). One series needs no legend: the title names it. The marks are thin with rounded tops on a
/// quiet baseline; every bar has a title for the tooltip and the numbers are also in a table that the page puts next to it. Colours are tokens
/// (the accent of the app), so the chart follows the theme and the dark mode.
/// </summary>
public static class BarChart
{
    public static IHtmlContent Render(string id, string title, IReadOnlyList<Bar> bars, string unit)
    {
        if (bars.Count == 0)
        {
            return HtmlString.Empty;
        }

        const double width = 640, height = 160, left = 8, right = 8, top = 12, bottom = 26;
        var max = Math.Max(bars.Max(b => b.Value), 0.0001);
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var slot = plotWidth / bars.Count;
        var barWidth = Math.Clamp(slot * 0.55, 4, 28);
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.Append("<figure class=\"kk-chart\" id=\"").Append(WebUtility.HtmlEncode(id)).Append("\">");
        sb.Append("<svg viewBox=\"0 0 ").Append(width.ToString(inv)).Append(' ').Append(height.ToString(inv)).Append("\" role=\"img\" aria-label=\"").Append(WebUtility.HtmlEncode(title)).Append("\" preserveAspectRatio=\"xMidYMid meet\">");
        sb.Append("<line class=\"kk-chart__base\" x1=\"").Append(left.ToString(inv)).Append("\" y1=\"").Append((top + plotHeight).ToString(inv)).Append("\" x2=\"").Append((width - right).ToString(inv)).Append("\" y2=\"").Append((top + plotHeight).ToString(inv)).Append("\" />");

        var labelEvery = bars.Count > 14 ? 2 : 1;
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var h = bar.Value <= 0 ? 0 : Math.Max(2, bar.Value / max * plotHeight);
            var x = left + slot * i + (slot - barWidth) / 2;
            var y = top + plotHeight - h;
            sb.Append("<g class=\"kk-chart__bar\" tabindex=\"0\"><title>").Append(WebUtility.HtmlEncode(bar.Description)).Append("</title>");
            sb.Append("<rect class=\"kk-chart__hit\" x=\"").Append((left + slot * i).ToString(inv)).Append("\" y=\"").Append(top.ToString(inv)).Append("\" width=\"").Append(slot.ToString(inv)).Append("\" height=\"").Append(plotHeight.ToString(inv)).Append("\" />");
            if (h > 0)
            {
                // The rounded end is the top only: the bar stands on the baseline.
                var r = Math.Min(4, barWidth / 2);
                sb.Append("<path class=\"kk-chart__mark\" d=\"M").Append(x.ToString(inv)).Append(' ').Append((top + plotHeight).ToString(inv))
                  .Append(" V").Append((y + r).ToString(inv)).Append(" Q").Append(x.ToString(inv)).Append(' ').Append(y.ToString(inv)).Append(' ').Append((x + r).ToString(inv)).Append(' ').Append(y.ToString(inv))
                  .Append(" H").Append((x + barWidth - r).ToString(inv)).Append(" Q").Append((x + barWidth).ToString(inv)).Append(' ').Append(y.ToString(inv)).Append(' ').Append((x + barWidth).ToString(inv)).Append(' ').Append((y + r).ToString(inv))
                  .Append(" V").Append((top + plotHeight).ToString(inv)).Append(" Z\" />");
            }

            if (i % labelEvery == 0)
            {
                sb.Append("<text class=\"kk-chart__label\" x=\"").Append((left + slot * i + slot / 2).ToString(inv)).Append("\" y=\"").Append((height - 8).ToString(inv)).Append("\" text-anchor=\"middle\">").Append(WebUtility.HtmlEncode(bar.Label)).Append("</text>");
            }

            sb.Append("</g>");
        }

        sb.Append("</svg>");
        sb.Append("<figcaption class=\"mat-sr-only\">").Append(WebUtility.HtmlEncode(title)).Append(" in ").Append(WebUtility.HtmlEncode(unit)).Append(". Die Zahlen stehen auch in der Tabelle.</figcaption></figure>");
        return new HtmlString(sb.ToString());
    }
}
