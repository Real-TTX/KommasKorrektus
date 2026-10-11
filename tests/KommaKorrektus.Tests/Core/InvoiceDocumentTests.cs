using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using KommaKorrektus.Core.Billing;
using KommaKorrektus.Core.Documents;

namespace KommaKorrektus.Tests.Core;

internal static class Samples
{
    public static InvoiceDocument Invoice(Action<Builder>? change = null)
    {
        var b = new Builder();
        change?.Invoke(b);
        var lines = b.Lines.Select((l, i) => new DocumentLine(i + 1, l.Title, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.VatRate, l.DiscountPercent, InvoiceCalculator.LineNet(new LineInput(l.Quantity, l.UnitPrice, l.VatRate, l.DiscountPercent)))).ToList();
        var totals = InvoiceCalculator.Calculate(b.Lines.Select(l => new LineInput(l.Quantity, l.UnitPrice, l.VatRate, l.DiscountPercent)), b.Discount, b.SmallBusiness || b.ReverseCharge);
        return new InvoiceDocument
        {
            Kind = b.Kind,
            Number = b.Number,
            IssueDate = new DateOnly(2026, 10, 11),
            ServiceFrom = new DateOnly(2026, 9, 1),
            ServiceTo = new DateOnly(2026, 9, 30),
            DueDate = new DateOnly(2026, 10, 25),
            Subject = "Lektorat „Die Hexe vom Rabenwald“",
            Intro = "Sehr geehrte Frau Federmann,\n\nvielen Dank für die gute Zusammenarbeit.",
            Outro = "Bitte überweisen Sie den Betrag bis zum 25.10.2026.",
            Seller = new Party("Kommahexe Lektorat", "Miriam Schmoldt", "Hexenweg 1", "20095", "Hamburg", "DE", "info@kommahexe.example.com", "040 123456", b.VatId, b.TaxNumber, "kommahexe.example.com"),
            Buyer = new Party("Annika Federmann", null, "Beispielweg 3", "20095", "Hamburg", "DE", "annika@example.test", null, b.BuyerVatId),
            BuyerNumber = "K-0001",
            BuyerReference = b.BuyerReference,
            CustomerReference = "Bestellung 4711",
            RelatedNumber = b.Related,
            ProjectTitle = "Die Hexe vom Rabenwald",
            Lines = lines,
            DiscountPercent = b.Discount,
            Totals = totals,
            SmallBusiness = b.SmallBusiness,
            ReverseCharge = b.ReverseCharge,
            PaymentTermDays = 14,
            Bank = new BankInfo("Miriam Schmoldt", "Hamburger Sparkasse", "DE89370400440532013000", "COBADEFFXXX"),
            Settings = InvoiceTemplateSettings.Default,
        };
    }

    public sealed class Builder
    {
        public InvoiceKind Kind { get; set; } = InvoiceKind.Invoice;
        public string? Number { get; set; } = "RE-2026-0001";
        public string? VatId { get; set; } = "DE123456789";
        public string? TaxNumber { get; set; } = "27/123/45678";
        public string? BuyerVatId { get; set; }
        public string? BuyerReference { get; set; }
        public string? Related { get; set; }
        public decimal Discount { get; set; }
        public bool SmallBusiness { get; set; }
        public bool ReverseCharge { get; set; }
        public List<(string Title, string? Description, decimal Quantity, string Unit, decimal UnitPrice, decimal VatRate, decimal DiscountPercent)> Lines { get; } =
        [
            ("Lektorat", "Stil, Ausdruck und Logik", 75, "Normseite", 4.50m, 19, 0),
            ("Stundensatz", null, 2.5m, "Stunde", 55m, 19, 0),
        ];
    }
}

public class InvoiceDocumentTests
{
    private static XmlSchemaSet Schemas()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cii");
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add(null, Path.Combine(folder, "CrossIndustryInvoice_100pD16B.xsd"));
        set.Compile();
        return set;
    }

    private static List<string> Validate(string xml)
    {
        var errors = new List<string>();
        var doc = XDocument.Parse(xml);
        doc.Validate(Schemas(), (_, e) => errors.Add($"{e.Severity}: {e.Message}"), addSchemaInfo: false);
        return errors;
    }

    [Fact]
    public void An_invoice_pdf_shows_what_the_document_says()
    {
        var pdf = InvoicePdf.Render(Samples.Invoice());
        var text = string.Join('\n', PdfTextReader.Read(pdf).Paragraphs);
        Assert.Contains("RE-2026-0001", text);
        Assert.Contains("Annika Federmann", text);
        Assert.Contains("Lektorat", text);
        Assert.Contains("IBAN: DE89 3704 0044 0532 0130 00", text);
        Assert.Contains("Gesamtbetrag", text);
        Assert.Contains("1", text);
    }

    [Fact]
    public void The_totals_on_the_pdf_are_the_totals_of_the_calculation()
    {
        var d = Samples.Invoice();
        Assert.Equal(337.50m + 137.50m, d.Totals.Net);
        var text = string.Join(' ', PdfTextReader.Read(InvoicePdf.Render(d)).Paragraphs).Replace(' ', ' ');
        Assert.Contains(GermanFormat.Money(d.Totals.Gross).Replace(' ', ' '), text);
        Assert.Contains(GermanFormat.Money(d.Totals.Net).Replace(' ', ' '), text);
    }

    [Fact]
    public void A_draft_has_no_number_and_says_so()
    {
        var d = Samples.Invoice(b => b.Number = null);
        Assert.True(d.IsDraft);
        var text = string.Join(' ', PdfTextReader.Read(InvoicePdf.Render(d)).Paragraphs);
        Assert.Contains("Entwurf", text);
    }

    [Fact]
    public void A_small_business_invoice_has_no_tax_and_the_note()
    {
        var d = Samples.Invoice(b => { b.SmallBusiness = true; b.VatId = null; });
        Assert.Equal(0m, d.Totals.Vat);
        var text = string.Join(' ', PdfTextReader.Read(InvoicePdf.Render(d)).Paragraphs);
        Assert.Contains("§ 19 UStG", text);
        Assert.DoesNotContain("zzgl.", text);
    }

    [Fact]
    public void The_e_invoice_is_valid_against_the_schema_of_en_16931()
    {
        var errors = Validate(EInvoice.Create(Samples.Invoice()));
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void The_xrechnung_is_valid_against_the_schema_and_carries_the_leitweg_id()
    {
        var d = Samples.Invoice(b => b.BuyerReference = "04011000-1234512345-35");
        var xml = EInvoice.Create(d, EInvoiceProfile.XRechnung);
        var errors = Validate(xml);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.Contains("xrechnung_3.0", xml);
        Assert.Contains("<ram:BuyerReference>04011000-1234512345-35</ram:BuyerReference>", xml);
        Assert.Empty(EInvoice.Problems(d, EInvoiceProfile.XRechnung));
    }

    [Fact]
    public void The_amounts_in_the_xml_are_those_of_the_document()
    {
        var d = Samples.Invoice();
        var xml = XDocument.Parse(EInvoice.Create(d));
        XNamespace ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
        var sum = xml.Descendants(ram + "SpecifiedTradeSettlementHeaderMonetarySummation").Single();
        Assert.Equal(d.Totals.Net.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), sum.Element(ram + "TaxBasisTotalAmount")!.Value);
        Assert.Equal(d.Totals.Gross.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), sum.Element(ram + "GrandTotalAmount")!.Value);
        Assert.Equal("380", xml.Descendants(ram + "TypeCode").First().Value);
        Assert.Equal("DE89370400440532013000", xml.Descendants(ram + "IBANID").Single().Value);
        Assert.Equal(2, xml.Descendants(ram + "IncludedSupplyChainTradeLineItem").Count());
        Assert.Contains(xml.Descendants(ram + "BilledQuantity"), q => q.Attribute("unitCode")!.Value == "HUR");
    }

    [Fact]
    public void A_credit_note_is_type_381_and_points_to_the_invoice()
    {
        var d = Samples.Invoice(b => { b.Kind = InvoiceKind.CreditNote; b.Number = "GS-2026-0001"; b.Related = "RE-2026-0001"; });
        var xml = EInvoice.Create(d);
        Assert.Contains("<ram:TypeCode>381</ram:TypeCode>", xml);
        Assert.Contains("<ram:IssuerAssignedID>RE-2026-0001</ram:IssuerAssignedID>", xml);
        var errors = Validate(xml);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void A_small_business_e_invoice_uses_the_exempt_category_with_a_reason()
    {
        var d = Samples.Invoice(b => { b.SmallBusiness = true; b.VatId = null; });
        var xml = EInvoice.Create(d);
        Assert.Contains("<ram:CategoryCode>E</ram:CategoryCode>", xml);
        Assert.Contains("Kleinunternehmerregelung", xml);
        Assert.DoesNotContain("<ram:CategoryCode>S</ram:CategoryCode>", xml);
        var errors = Validate(xml);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void A_reverse_charge_invoice_uses_the_category_AE_and_needs_a_buyer_vat_id()
    {
        var d = Samples.Invoice(b => { b.ReverseCharge = true; b.BuyerVatId = "ATU12345678"; });
        var xml = EInvoice.Create(d);
        Assert.Contains("<ram:CategoryCode>AE</ram:CategoryCode>", xml);
        Assert.Empty(EInvoice.Problems(d));
        var without = Samples.Invoice(b => b.ReverseCharge = true);
        Assert.Contains(EInvoice.Problems(without), p => p.Contains("USt-IdNr."));
    }

    [Fact]
    public void A_document_discount_is_written_as_an_allowance_per_tax_rate()
    {
        var d = Samples.Invoice(b => b.Discount = 10);
        var xml = EInvoice.Create(d);
        Assert.Contains("<ram:AllowanceTotalAmount>", xml);
        var errors = Validate(xml);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void What_is_missing_for_an_e_invoice_is_named()
    {
        var d = Samples.Invoice(b => { b.VatId = null; b.TaxNumber = null; });
        Assert.Contains(EInvoice.Problems(d), p => p.Contains("Steuernummer") || p.Contains("USt-IdNr"));
        Assert.Contains(EInvoice.Problems(Samples.Invoice(), EInvoiceProfile.XRechnung), p => p.Contains("Leitweg"));
        Assert.Contains(EInvoice.Problems(Samples.Invoice(b => b.Kind = InvoiceKind.Offer)), p => p.Contains("Angebot"));
    }

    [Fact]
    public void An_offer_has_no_e_invoice()
        => Assert.Throws<InvalidOperationException>(() => EInvoice.Create(Samples.Invoice(b => b.Kind = InvoiceKind.Offer)));

    [Fact]
    public void The_girocode_has_the_fields_a_banking_app_expects()
    {
        var d = Samples.Invoice();
        var lines = InvoicePdf.EpcPayload(d, d.Bank!).Split('\n');
        Assert.Equal("BCD", lines[0]);
        Assert.Equal("002", lines[1]);
        Assert.Equal("SCT", lines[3]);
        Assert.Equal("COBADEFFXXX", lines[4]);
        Assert.Equal("DE89370400440532013000", lines[6]);
        Assert.Equal("EUR" + d.Totals.Gross.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), lines[7]);
        Assert.Equal("Rechnung RE-2026-0001", lines[10]);
    }

    [Fact]
    public void A_zugferd_pdf_holds_the_xml_and_says_so_in_its_metadata()
    {
        var pdf = ZugferdPdf.Create(Samples.Invoice(), new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("factur-x.xml", text);
        Assert.Contains("urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#", text);
        Assert.Contains("EN 16931", text);
        Assert.Contains("/AFRelationship", text);
        // The invoice is still a PDF that can be read by a person.
        Assert.Contains("RE-2026-0001", string.Join(' ', PdfTextReader.Read(pdf).Paragraphs));
    }

    [Fact]
    public void The_logo_and_the_look_of_the_template_are_used()
    {
        var settings = InvoiceTemplateSettings.Default;
        settings.AccentColor = "#2a7f62";
        settings.FontFamily = "Serif";
        settings.LogoPosition = "Left";
        settings.ShowDiscountColumn = true;
        var d = Samples.Invoice();
        var withLook = new InvoiceDocument
        {
            Kind = d.Kind, Number = d.Number, IssueDate = d.IssueDate, Seller = d.Seller, Buyer = d.Buyer, Lines = d.Lines, Totals = d.Totals, Bank = d.Bank,
            Settings = settings,
            Logo = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><circle cx='20' cy='20' r='18' fill='#2a7f62'/></svg>"),
        };
        var pdf = InvoicePdf.Render(withLook);
        Assert.True(pdf.Length > 1000);
        Assert.Contains("RE-2026-0001", string.Join(' ', PdfTextReader.Read(pdf).Paragraphs));
    }

    [Fact]
    public void Placeholders_are_filled_and_an_unknown_one_stays_visible()
    {
        var values = new Dictionary<string, string> { ["Anrede"] = "Sehr geehrte Frau Federmann,", ["Nummer"] = "RE-1" };
        Assert.Equal("Sehr geehrte Frau Federmann,\n\nRechnung RE-1 {{Tippfehler}}", TextPlaceholders.Resolve("{{Anrede}}\n\nRechnung {{ Nummer }} {{Tippfehler}}", values));
    }

    [Theory]
    [InlineData("Stunde", "HUR")]
    [InlineData("Normseite", "C62")]
    [InlineData("pauschal", "LS")]
    [InlineData("Tag", "DAY")]
    public void Units_are_mapped_to_the_codes_of_the_standard(string unit, string code) => Assert.Equal(code, EInvoice.UnitCode(unit));
}
