using System.Globalization;
using System.Xml.Linq;

namespace KommaKorrektus.Core.Billing;

public enum EInvoiceProfile
{
    /// <summary>ZUGFeRD / Factur-X, profile EN 16931 (the "Comfort" level): the XML inside the PDF.</summary>
    Zugferd,
    /// <summary>XRechnung 3.0 (CII): the XML for public bodies, with the buyer reference (Leitweg-ID).</summary>
    XRechnung,
}

/// <summary>
/// The electronic invoice (EN 16931) in the Cross Industry Invoice syntax: the data of an <see cref="InvoiceDocument"/> as XML. Inside a PDF/A-3 it is a
/// ZUGFeRD / Factur-X invoice that a person can read and a program can process; on its own it is an XRechnung. The elements are written in the order
/// the schema wants. What the document does not know is not invented: a missing seller VAT id and tax number are reported by <see cref="Problems"/>
/// before an invoice is issued.
/// </summary>
public static class EInvoice
{
    private static readonly XNamespace Rsm = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";
    private static readonly XNamespace Ram = "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100";
    private static readonly XNamespace Udt = "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100";
    private static readonly XNamespace Qdt = "urn:un:unece:uncefact:data:standard:QualifiedDataType:100";

    public const string ZugferdGuideline = "urn:cen.eu:en16931:2017";

    public const string XRechnungGuideline = "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_3.0";

    public const string PeppolProcess = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

    /// <summary>What is missing for a valid e-invoice of this document, in words for the person who issues it. Empty: nothing.</summary>
    public static IReadOnlyList<string> Problems(InvoiceDocument d, EInvoiceProfile profile = EInvoiceProfile.Zugferd)
    {
        var problems = new List<string>();
        if (d.Kind == InvoiceKind.Offer)
        {
            problems.Add("Ein Angebot ist keine Rechnung: Es gibt dafür keine E-Rechnung.");
            return problems;
        }

        if (string.IsNullOrWhiteSpace(d.Seller.Name)) { problems.Add("Der Name des Absenders fehlt (Verwaltung > Firma)."); }
        if (string.IsNullOrWhiteSpace(d.Seller.Street) || string.IsNullOrWhiteSpace(d.Seller.City) || string.IsNullOrWhiteSpace(d.Seller.PostalCode)) { problems.Add("Die Anschrift des Absenders ist unvollständig (Verwaltung > Firma)."); }
        if (string.IsNullOrWhiteSpace(d.Seller.VatId) && string.IsNullOrWhiteSpace(d.Seller.TaxNumber)) { problems.Add("Es fehlt die Steuernummer oder die USt-IdNr. des Absenders (Verwaltung > Firma)."); }
        if (string.IsNullOrWhiteSpace(d.Buyer.Name)) { problems.Add("Der Name des Kunden fehlt."); }
        if (string.IsNullOrWhiteSpace(d.Buyer.City) || string.IsNullOrWhiteSpace(d.Buyer.Street)) { problems.Add("Die Anschrift des Kunden ist unvollständig."); }
        if (d.Lines.Count == 0) { problems.Add("Die Rechnung hat keine Positionen."); }
        if (d.ReverseCharge && string.IsNullOrWhiteSpace(d.Buyer.VatId)) { problems.Add("Bei Reverse Charge muss der Kunde eine USt-IdNr. haben."); }
        if (d.Bank is null || !Iban.IsValid(d.Bank.Iban)) { problems.Add("Es fehlt eine gültige IBAN (Verwaltung > Firma)."); }

        if (profile == EInvoiceProfile.XRechnung)
        {
            if (string.IsNullOrWhiteSpace(d.BuyerReference)) { problems.Add("Für eine XRechnung braucht der Kunde eine Leitweg-ID."); }
            if (string.IsNullOrWhiteSpace(d.Seller.Email) || string.IsNullOrWhiteSpace(d.Seller.Phone)) { problems.Add("Für eine XRechnung braucht der Absender E-Mail und Telefon (Verwaltung > Firma)."); }
        }

        return problems;
    }

    public static string Create(InvoiceDocument d, EInvoiceProfile profile = EInvoiceProfile.Zugferd)
    {
        if (d.Kind == InvoiceKind.Offer)
        {
            throw new InvalidOperationException("Für ein Angebot gibt es keine E-Rechnung.");
        }

        var t = d.Totals;
        var currency = d.Currency;
        var category = d.SmallBusiness ? "E" : d.ReverseCharge ? "AE" : "S";
        var exemption = d.SmallBusiness
            ? "Kein Ausweis von Umsatzsteuer aufgrund der Kleinunternehmerregelung gemäß § 19 UStG."
            : d.ReverseCharge ? "Reverse Charge: Steuerschuldnerschaft des Leistungsempfängers." : null;

        var context = new XElement(Rsm + "ExchangedDocumentContext");
        if (profile == EInvoiceProfile.XRechnung)
        {
            context.Add(new XElement(Ram + "BusinessProcessSpecifiedDocumentContextParameter", new XElement(Ram + "ID", PeppolProcess)));
        }

        context.Add(new XElement(Ram + "GuidelineSpecifiedDocumentContextParameter", new XElement(Ram + "ID", profile == EInvoiceProfile.XRechnung ? XRechnungGuideline : ZugferdGuideline)));

        var document = new XElement(Rsm + "ExchangedDocument",
            new XElement(Ram + "ID", d.Number ?? throw new InvalidOperationException("Ein Entwurf hat keine Nummer.")),
            new XElement(Ram + "TypeCode", d.Kind == InvoiceKind.CreditNote ? "381" : "380"),
            new XElement(Ram + "IssueDateTime", Date(Udt, d.IssueDate)));
        if (d.TaxNote is { } note)
        {
            document.Add(new XElement(Ram + "IncludedNote", new XElement(Ram + "Content", note)));
        }

        var transaction = new XElement(Rsm + "SupplyChainTradeTransaction");
        for (var i = 0; i < d.Lines.Count; i++)
        {
            var line = d.Lines[i];
            var netPrice = line.DiscountPercent == 0 ? line.UnitPrice : Math.Round(line.UnitPrice * (1 - line.DiscountPercent / 100m), 4, MidpointRounding.AwayFromZero);
            var product = new XElement(Ram + "SpecifiedTradeProduct", new XElement(Ram + "Name", Truncate(line.Title, 250)));
            if (!string.IsNullOrWhiteSpace(line.Description))
            {
                product.Add(new XElement(Ram + "Description", Truncate(line.Description, 1000)));
            }

            transaction.Add(new XElement(Ram + "IncludedSupplyChainTradeLineItem",
                new XElement(Ram + "AssociatedDocumentLineDocument", new XElement(Ram + "LineID", (i + 1).ToString(CultureInfo.InvariantCulture))),
                product,
                new XElement(Ram + "SpecifiedLineTradeAgreement", new XElement(Ram + "NetPriceProductTradePrice", new XElement(Ram + "ChargeAmount", Num(netPrice, 4)))),
                new XElement(Ram + "SpecifiedLineTradeDelivery", new XElement(Ram + "BilledQuantity", new XAttribute("unitCode", UnitCode(line.Unit)), Num(line.Quantity, 4))),
                new XElement(Ram + "SpecifiedLineTradeSettlement",
                    new XElement(Ram + "ApplicableTradeTax",
                        new XElement(Ram + "TypeCode", "VAT"),
                        new XElement(Ram + "CategoryCode", category),
                        new XElement(Ram + "RateApplicablePercent", Num(d.SmallBusiness || d.ReverseCharge ? 0m : line.VatRate, 2))),
                    new XElement(Ram + "SpecifiedTradeSettlementLineMonetarySummation", new XElement(Ram + "LineTotalAmount", Num(line.NetAmount, 2))))));
        }

        transaction.Add(Agreement(d, profile));
        transaction.Add(new XElement(Ram + "ApplicableHeaderTradeDelivery",
            new XElement(Ram + "ActualDeliverySupplyChainEvent", new XElement(Ram + "OccurrenceDateTime", Date(Udt, d.ServiceTo ?? d.ServiceFrom ?? d.IssueDate)))));

        var settlement = new XElement(Ram + "ApplicableHeaderTradeSettlement");
        if (!string.IsNullOrWhiteSpace(d.Number))
        {
            settlement.Add(new XElement(Ram + "PaymentReference", d.Number));
        }

        settlement.Add(new XElement(Ram + "InvoiceCurrencyCode", currency));
        if (d.Bank is { } bank && Iban.IsValid(bank.Iban) && d.Kind == InvoiceKind.Invoice)
        {
            var means = new XElement(Ram + "SpecifiedTradeSettlementPaymentMeans",
                new XElement(Ram + "TypeCode", "58"),
                new XElement(Ram + "PayeePartyCreditorFinancialAccount", new XElement(Ram + "IBANID", Iban.Normalize(bank.Iban))));
            if (!string.IsNullOrWhiteSpace(bank.Bic))
            {
                means.Add(new XElement(Ram + "PayeeSpecifiedCreditorFinancialInstitution", new XElement(Ram + "BICID", bank.Bic.Trim().ToUpperInvariant())));
            }

            settlement.Add(means);
        }

        foreach (var g in t.Groups)
        {
            var tax = new XElement(Ram + "ApplicableTradeTax",
                new XElement(Ram + "CalculatedAmount", Num(g.Vat, 2)),
                new XElement(Ram + "TypeCode", "VAT"));
            if (exemption is not null)
            {
                tax.Add(new XElement(Ram + "ExemptionReason", exemption));
            }

            tax.Add(new XElement(Ram + "BasisAmount", Num(g.Net, 2)),
                new XElement(Ram + "CategoryCode", category),
                new XElement(Ram + "RateApplicablePercent", Num(g.Rate, 2)));
            settlement.Add(tax);
        }

        if (d.ServiceFrom is not null || d.ServiceTo is not null)
        {
            var period = new XElement(Ram + "BillingSpecifiedPeriod");
            if (d.ServiceFrom is { } from) { period.Add(new XElement(Ram + "StartDateTime", Date(Udt, from))); }
            if (d.ServiceTo is { } to) { period.Add(new XElement(Ram + "EndDateTime", Date(Udt, to))); }
            settlement.Add(period);
        }

        foreach (var g in t.Groups.Where(g => g.Discount != 0))
        {
            settlement.Add(new XElement(Ram + "SpecifiedTradeAllowanceCharge",
                new XElement(Ram + "ChargeIndicator", new XElement(Udt + "Indicator", "false")),
                new XElement(Ram + "CalculationPercent", Num(d.DiscountPercent, 2)),
                new XElement(Ram + "ActualAmount", Num(g.Discount, 2)),
                new XElement(Ram + "Reason", "Rabatt"),
                new XElement(Ram + "CategoryTradeTax",
                    new XElement(Ram + "TypeCode", "VAT"),
                    new XElement(Ram + "CategoryCode", category),
                    new XElement(Ram + "RateApplicablePercent", Num(g.Rate, 2)))));
        }

        if (d.Kind == InvoiceKind.Invoice && d.DueDate is { } due)
        {
            settlement.Add(new XElement(Ram + "SpecifiedTradePaymentTerms",
                new XElement(Ram + "Description", $"Zahlbar innerhalb von {d.PaymentTermDays} Tagen bis zum {GermanFormat.Date(due)}."),
                new XElement(Ram + "DueDateDateTime", Date(Udt, due))));
        }

        var summation = new XElement(Ram + "SpecifiedTradeSettlementHeaderMonetarySummation",
            new XElement(Ram + "LineTotalAmount", Num(t.LineTotal, 2)));
        if (t.DiscountAmount != 0)
        {
            summation.Add(new XElement(Ram + "AllowanceTotalAmount", Num(t.DiscountAmount, 2)));
        }

        summation.Add(
            new XElement(Ram + "TaxBasisTotalAmount", Num(t.Net, 2)),
            new XElement(Ram + "TaxTotalAmount", new XAttribute("currencyID", currency), Num(t.Vat, 2)),
            new XElement(Ram + "GrandTotalAmount", Num(t.Gross, 2)),
            new XElement(Ram + "DuePayableAmount", Num(t.Gross, 2)));
        settlement.Add(summation);

        if (d.Kind == InvoiceKind.CreditNote && !string.IsNullOrWhiteSpace(d.RelatedNumber))
        {
            settlement.Add(new XElement(Ram + "InvoiceReferencedDocument", new XElement(Ram + "IssuerAssignedID", d.RelatedNumber)));
        }

        transaction.Add(settlement);

        var root = new XElement(Rsm + "CrossIndustryInvoice",
            new XAttribute(XNamespace.Xmlns + "rsm", Rsm),
            new XAttribute(XNamespace.Xmlns + "ram", Ram),
            new XAttribute(XNamespace.Xmlns + "udt", Udt),
            new XAttribute(XNamespace.Xmlns + "qdt", Qdt),
            context, document, transaction);
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root).ToString(SaveOptions.None).Insert(0, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
    }

    private static XElement Agreement(InvoiceDocument d, EInvoiceProfile profile)
    {
        var agreement = new XElement(Ram + "ApplicableHeaderTradeAgreement");
        if (!string.IsNullOrWhiteSpace(d.BuyerReference))
        {
            agreement.Add(new XElement(Ram + "BuyerReference", d.BuyerReference));
        }
        else if (profile == EInvoiceProfile.XRechnung)
        {
            agreement.Add(new XElement(Ram + "BuyerReference", d.BuyerNumber ?? "-"));
        }

        agreement.Add(PartyElement(Ram + "SellerTradeParty", d.Seller, seller: true, d.Seller.Contact));
        agreement.Add(PartyElement(Ram + "BuyerTradeParty", d.Buyer, seller: false, null));
        if (!string.IsNullOrWhiteSpace(d.CustomerReference))
        {
            agreement.Add(new XElement(Ram + "BuyerOrderReferencedDocument", new XElement(Ram + "IssuerAssignedID", d.CustomerReference)));
        }

        return agreement;
    }

    private static XElement PartyElement(XName name, Party p, bool seller, string? contactName)
    {
        var party = new XElement(name, new XElement(Ram + "Name", Truncate(p.Name, 250)));
        if (seller)
        {
            var contact = new XElement(Ram + "DefinedTradeContact", new XElement(Ram + "PersonName", Truncate(string.IsNullOrWhiteSpace(contactName) ? p.Name : contactName, 250)));
            if (!string.IsNullOrWhiteSpace(p.Phone)) { contact.Add(new XElement(Ram + "TelephoneUniversalCommunication", new XElement(Ram + "CompleteNumber", p.Phone))); }
            if (!string.IsNullOrWhiteSpace(p.Email)) { contact.Add(new XElement(Ram + "EmailURIUniversalCommunication", new XElement(Ram + "URIID", p.Email))); }
            party.Add(contact);
        }

        var address = new XElement(Ram + "PostalTradeAddress");
        if (!string.IsNullOrWhiteSpace(p.PostalCode)) { address.Add(new XElement(Ram + "PostcodeCode", p.PostalCode)); }
        if (!string.IsNullOrWhiteSpace(p.Street)) { address.Add(new XElement(Ram + "LineOne", p.Street)); }
        if (!string.IsNullOrWhiteSpace(p.City)) { address.Add(new XElement(Ram + "CityName", p.City)); }
        address.Add(new XElement(Ram + "CountryID", string.IsNullOrWhiteSpace(p.Country) ? "DE" : p.Country.ToUpperInvariant()));
        party.Add(address);

        if (!string.IsNullOrWhiteSpace(p.Email))
        {
            party.Add(new XElement(Ram + "URIUniversalCommunication", new XElement(Ram + "URIID", new XAttribute("schemeID", "EM"), p.Email)));
        }

        if (!string.IsNullOrWhiteSpace(p.VatId))
        {
            party.Add(new XElement(Ram + "SpecifiedTaxRegistration", new XElement(Ram + "ID", new XAttribute("schemeID", "VA"), p.VatId.Replace(" ", string.Empty))));
        }

        if (seller && !string.IsNullOrWhiteSpace(p.TaxNumber))
        {
            party.Add(new XElement(Ram + "SpecifiedTaxRegistration", new XElement(Ram + "ID", new XAttribute("schemeID", "FC"), p.TaxNumber)));
        }

        return party;
    }

    private static XElement Date(XNamespace ns, DateOnly date)
        => new(ns + "DateTimeString", new XAttribute("format", "102"), date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));

    private static string Num(decimal value, int decimals) => Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>UN/ECE recommendation 20 codes for the units an editor uses; the rest is "one" (C62).</summary>
    public static string UnitCode(string unit) => unit.Trim().ToLowerInvariant() switch
    {
        "stunde" or "stunden" or "std" or "std." or "h" => "HUR",
        "tag" or "tage" => "DAY",
        "monat" or "monate" => "MON",
        "pauschal" or "pauschale" => "LS",
        "prozent" or "%" => "P1",
        _ => "C62",
    };
}
