namespace KommaKorrektus.Core.Billing;

/// <summary>A line as the calculation sees it.</summary>
public sealed record LineInput(decimal Quantity, decimal UnitPrice, decimal VatRate, decimal DiscountPercent = 0);

/// <summary>The sum of one tax rate: the net amount it is charged on and the tax.</summary>
/// <summary><see cref="Net"/> is the amount the tax is charged on (after the discount of the document); <see cref="Discount"/> is what the discount took off this group.</summary>
public sealed record VatGroup(decimal Rate, decimal Net, decimal Vat, decimal Discount = 0);

public sealed record InvoiceTotals(decimal LineTotal, decimal DiscountAmount, decimal Net, decimal Vat, decimal Gross, IReadOnlyList<VatGroup> Groups, IReadOnlyList<decimal> LineNets);

/// <summary>
/// Invoice maths the German way. A line amount is rounded to cents. The discount of the whole document is taken off the net amount of every tax rate before
/// the tax is worked out (so the tax is right on what is really charged). The tax of a rate is rounded once, on the sum of its lines, not line by line.
/// A small business (paragraph 19 UStG) and a reverse charge show no tax at all.
/// </summary>
public static class InvoiceCalculator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal LineNet(LineInput line) => Round(line.Quantity * line.UnitPrice * (1 - line.DiscountPercent / 100m));

    public static InvoiceTotals Calculate(IEnumerable<LineInput> lines, decimal documentDiscountPercent, bool noVat)
    {
        var list = lines.ToList();
        var lineNets = list.Select(LineNet).ToList();
        var lineTotal = lineNets.Sum();

        var groups = new List<VatGroup>();
        var discountTotal = 0m;
        foreach (var rate in list.Select(l => noVat ? 0m : l.VatRate).Distinct().OrderBy(r => r))
        {
            var sum = list.Select((l, i) => (Rate: noVat ? 0m : l.VatRate, Net: lineNets[i])).Where(x => x.Rate == rate).Sum(x => x.Net);
            var discount = Round(sum * documentDiscountPercent / 100m);
            var net = sum - discount;
            discountTotal += discount;
            groups.Add(new VatGroup(rate, net, noVat ? 0m : Round(net * rate / 100m), discount));
        }

        var netTotal = groups.Sum(g => g.Net);
        var vat = groups.Sum(g => g.Vat);
        return new InvoiceTotals(lineTotal, discountTotal, netTotal, vat, netTotal + vat, groups, lineNets);
    }
}
