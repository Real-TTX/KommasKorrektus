using KommaKorrektus.Core.Billing;

namespace KommaKorrektus.Tests.Core;

public class InvoiceCalculatorTests
{
    [Fact]
    public void A_line_is_quantity_times_price_rounded_to_cents()
    {
        Assert.Equal(135.00m, InvoiceCalculator.LineNet(new LineInput(75, 1.80m, 19)));
        Assert.Equal(0.67m, InvoiceCalculator.LineNet(new LineInput(0.37m, 1.8m, 19)));
        Assert.Equal(90.00m, InvoiceCalculator.LineNet(new LineInput(1, 100m, 19, 10)));
    }

    [Fact]
    public void The_tax_is_worked_out_on_the_sum_of_a_rate_not_line_by_line()
    {
        // Line by line 0.095 would round to 0.10 three times (0.30); on the sum 1.50 the tax is 0.285, which is 0.29.
        var totals = InvoiceCalculator.Calculate([new LineInput(1, 0.50m, 19), new LineInput(1, 0.50m, 19), new LineInput(1, 0.50m, 19)], 0, false);
        Assert.Equal(1.50m, totals.Net);
        Assert.Equal(0.29m, totals.Vat);
        Assert.Equal(1.79m, totals.Gross);
    }

    [Fact]
    public void Different_rates_make_different_groups()
    {
        var totals = InvoiceCalculator.Calculate([new LineInput(1, 100m, 19), new LineInput(1, 50m, 7), new LineInput(1, 20m, 19)], 0, false);
        Assert.Equal(2, totals.Groups.Count);
        Assert.Equal(new VatGroup(7m, 50m, 3.50m), totals.Groups[0]);
        Assert.Equal(new VatGroup(19m, 120m, 22.80m), totals.Groups[1]);
        Assert.Equal(170m, totals.Net);
        Assert.Equal(26.30m, totals.Vat);
        Assert.Equal(196.30m, totals.Gross);
    }

    [Fact]
    public void The_discount_of_the_document_is_taken_off_before_the_tax()
    {
        var totals = InvoiceCalculator.Calculate([new LineInput(1, 200m, 19)], 10, false);
        Assert.Equal(20m, totals.DiscountAmount);
        Assert.Equal(180m, totals.Net);
        Assert.Equal(34.20m, totals.Vat);
        Assert.Equal(214.20m, totals.Gross);
    }

    [Fact]
    public void A_small_business_shows_no_tax()
    {
        var totals = InvoiceCalculator.Calculate([new LineInput(1, 100m, 19)], 0, noVat: true);
        Assert.Equal(100m, totals.Gross);
        Assert.Equal(0m, totals.Vat);
        Assert.Equal([new VatGroup(0m, 100m, 0m)], totals.Groups);
    }

    [Fact]
    public void A_credit_note_has_negative_amounts_and_the_same_rules()
    {
        var totals = InvoiceCalculator.Calculate([new LineInput(-1, 135m, 19)], 0, false);
        Assert.Equal(-135m, totals.Net);
        Assert.Equal(-25.65m, totals.Vat);
        Assert.Equal(-160.65m, totals.Gross);
    }

    [Fact]
    public void An_empty_document_is_zero()
    {
        var totals = InvoiceCalculator.Calculate([], 0, false);
        Assert.Equal(0m, totals.Gross);
        Assert.Empty(totals.Groups);
    }

    [Theory]
    [InlineData("DE89370400440532013000", true)]
    [InlineData("DE89 3704 0044 0532 0130 00", true)]
    [InlineData("DE89370400440532013001", false)]
    [InlineData("", false)]
    [InlineData("12345", false)]
    [InlineData("GB82WEST12345698765432", true)]
    public void Iban_checks_use_the_check_digits(string iban, bool valid) => Assert.Equal(valid, Iban.IsValid(iban));

    [Fact]
    public void An_iban_is_written_in_blocks_of_four()
        => Assert.Equal("DE89 3704 0044 0532 0130 00", Iban.Format("de89370400440532013000"));

    [Theory]
    [InlineData("Frau", "Annika", "Federmann", false, "Sehr geehrte Frau Federmann,")]
    [InlineData("Herr", "Paul", "Muster", false, "Sehr geehrter Herr Muster,")]
    [InlineData("", "Alex", "Beispiel", false, "Guten Tag Alex Beispiel,")]
    [InlineData("", "", "", true, "Sehr geehrte Damen und Herren,")]
    [InlineData("Divers", "Kim", "Lang", false, "Guten Tag Kim Lang,")]
    public void The_salutation_follows_what_is_known(string salutation, string first, string last, bool company, string expected)
        => Assert.Equal(expected, Salutations.Letter(salutation, first, last, company));
}
