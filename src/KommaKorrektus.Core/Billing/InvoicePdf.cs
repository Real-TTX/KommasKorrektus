using System.Globalization;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KommaKorrektus.Core.Billing;

/// <summary>
/// Draws an offer, an invoice or a credit note as a PDF. The look comes from the template (<see cref="InvoiceTemplateSettings"/>): logo, accent colour,
/// font, the columns of the table, the footer. The layout follows what a German business letter has (sender line, address, reference block, subject,
/// table, totals, payment information, a footer with the company data).
/// </summary>
public static class InvoicePdf
{
    static InvoicePdf()
    {
        PdfEnvironment.Initialize();
    }

    /// <param name="pdfA">Write PDF/A-3: the base of a ZUGFeRD invoice, where the XML invoice is attached afterwards.</param>
    public static byte[] Render(InvoiceDocument d, bool pdfA = false)
    {
        var settings = d.Settings;
        var accent = InvoiceTemplateSettings.SafeColor(settings.AccentColor);
        var fonts = settings.FontFamily == "Serif" ? new[] { "Liberation Serif", "Times New Roman", "DejaVu Serif", "Lato" } : ["Lato"];

        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(settings.PaperSize == "Letter" ? PageSizes.Letter : PageSizes.A4);
            page.Margin(Math.Clamp(settings.MarginMm, 10, 35), Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize((float)Math.Clamp(settings.FontSize, 8, 13)).FontFamily(fonts).LineHeight(1.3f));

            if (d.IsDraft)
            {
                page.Background().AlignCenter().AlignMiddle().Rotate(-30).Text("ENTWURF").FontSize(96).Bold().FontColor(Colors.Grey.Lighten3);
            }

            page.Content().Column(column =>
            {
                column.Spacing(8);
                column.Item().Element(c => Top(c, d, accent));
                column.Item().PaddingTop(14).Element(c => AddressAndReferences(c, d, accent));
                column.Item().PaddingTop(10).Element(c => TitleAndIntro(c, d, accent));
                column.Item().Element(c => Lines(c, d, accent));
                column.Item().Element(c => Totals(c, d, accent));
                column.Item().Element(c => Closing(c, d));
            });

            page.Footer().Element(c => Footer(c, d, accent));
        }));

        document.WithMetadata(new DocumentMetadata
        {
            Title = $"{d.Title} {d.Number ?? "(Entwurf)"}",
            Author = d.Seller.Name,
            Subject = d.Subject ?? d.Title,
            Creator = "KommaKorrektus",
            Language = "de-DE",
        });
        if (pdfA)
        {
            document.WithSettings(new DocumentSettings { PDFA_Conformance = PDFA_Conformance.PDFA_3B });
        }

        return document.GeneratePdf();
    }

    private static void Top(IContainer container, InvoiceDocument d, string accent)
    {
        var logoRight = d.Settings.LogoPosition != "Left";
        container.Row(row =>
        {
            void Company(IContainer c)
            {
                c.Column(col =>
                {
                    col.Item().Text(d.Seller.Name).FontSize(14).Bold().FontColor(accent);
                    if (!string.IsNullOrWhiteSpace(d.Seller.Contact))
                    {
                        col.Item().Text(d.Seller.Contact).FontSize(9);
                    }

                    col.Item().Text($"{d.Seller.Street}, {d.Seller.CityLine}").FontSize(9).FontColor(Colors.Grey.Darken2);
                });
            }

            void Logo(IContainer c)
            {
                if (d.Logo is { Length: > 0 } bytes)
                {
                    var h = Math.Clamp(d.Settings.LogoHeightMm, 8, 40);
                    var box = c.AlignRight().Height(h, Unit.Millimetre);
                    if (IsSvg(bytes))
                    {
                        box.Svg(System.Text.Encoding.UTF8.GetString(bytes));
                    }
                    else
                    {
                        box.Image(bytes).FitHeight();
                    }
                }
            }

            if (logoRight)
            {
                row.RelativeItem().Element(Company);
                row.RelativeItem().Element(Logo);
            }
            else
            {
                row.RelativeItem().Element(c => c.AlignLeft().Element(Logo));
                row.RelativeItem().AlignRight().Element(Company);
            }
        });
    }

    private static bool IsSvg(byte[] bytes)
    {
        var head = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300)).TrimStart();
        return head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddressAndReferences(IContainer container, InvoiceDocument d, string accent)
    {
        container.Row(row =>
        {
            row.RelativeItem(11).Column(col =>
            {
                if (d.Settings.ShowSenderLine)
                {
                    col.Item().PaddingBottom(3).Text($"{d.Seller.Name} · {d.Seller.Street} · {d.Seller.CityLine}").FontSize(7).FontColor(Colors.Grey.Darken1).Underline();
                }

                col.Item().Text(d.Buyer.Name).Bold();
                if (!string.IsNullOrWhiteSpace(d.Buyer.Contact))
                {
                    col.Item().Text(d.Buyer.Contact);
                }

                if (!string.IsNullOrWhiteSpace(d.Buyer.Street))
                {
                    col.Item().Text(d.Buyer.Street);
                }

                if (d.Buyer.CityLine.Length > 0)
                {
                    col.Item().Text(d.Buyer.CityLine);
                }

                if (!string.IsNullOrWhiteSpace(d.Buyer.Country) && !d.Buyer.Country.Equals("DE", StringComparison.OrdinalIgnoreCase))
                {
                    col.Item().Text(CountryName(d.Buyer.Country));
                }
            });

            row.ConstantItem(24);

            row.RelativeItem(9).Table(table =>
            {
                table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1.2f); });
                void Meta(string label, string? value)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return;
                    }

                    table.Cell().PaddingVertical(1).Text(label).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    table.Cell().PaddingVertical(1).AlignRight().Text(value).FontSize(9);
                }

                Meta($"{d.Title}-Nr.", d.Number ?? "Entwurf");
                Meta("Datum", GermanFormat.Date(d.IssueDate));
                Meta("Kundennummer", d.BuyerNumber);
                Meta("Leistungszeitraum", GermanFormat.Period(d.ServiceFrom, d.ServiceTo));
                Meta("Fällig am", d.Kind == InvoiceKind.Invoice ? GermanFormat.Date(d.DueDate) : null);
                Meta("Gültig bis", d.Kind == InvoiceKind.Offer ? GermanFormat.Date(d.ValidUntil) : null);
                Meta("Zu", d.RelatedNumber);
                Meta("Ihre Referenz", d.CustomerReference);
                Meta("Projekt", d.ProjectTitle);
            });
        });
    }

    private static string CountryName(string code) => code.ToUpperInvariant() switch
    {
        "AT" => "Österreich", "CH" => "Schweiz", "LU" => "Luxemburg", "NL" => "Niederlande", "BE" => "Belgien", "FR" => "Frankreich", "IT" => "Italien",
        "ES" => "Spanien", "PL" => "Polen", "GB" => "Vereinigtes Königreich", "US" => "USA", _ => code,
    };

    private static void TitleAndIntro(IContainer container, InvoiceDocument d, string accent)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text($"{d.Title} {d.Number ?? string.Empty}".Trim()).FontSize(17).Bold().FontColor(accent);
            if (!string.IsNullOrWhiteSpace(d.Subject))
            {
                col.Item().Text(d.Subject).Bold();
            }

            if (!string.IsNullOrWhiteSpace(d.Intro))
            {
                col.Item().PaddingTop(4).Text(d.Intro);
            }
        });
    }

    private static void Lines(IContainer container, InvoiceDocument d, string accent)
    {
        var s = d.Settings;
        container.PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(22);
                c.RelativeColumn(5);
                if (s.ShowQuantityColumn) { c.ConstantColumn(40); }
                if (s.ShowUnitColumn) { c.ConstantColumn(48); }
                c.ConstantColumn(58);
                if (s.ShowVatColumn && !d.SmallBusiness) { c.ConstantColumn(36); }
                if (s.ShowDiscountColumn) { c.ConstantColumn(40); }
                c.ConstantColumn(64);
            });

            IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor(accent).PaddingVertical(3).PaddingHorizontal(2);
            table.Header(h =>
            {
                h.Cell().Element(Head).Text("Pos.").FontSize(8.5f).Bold().FontColor(accent);
                h.Cell().Element(Head).Text("Leistung").FontSize(8.5f).Bold().FontColor(accent);
                if (s.ShowQuantityColumn) { h.Cell().Element(Head).AlignRight().Text("Menge").FontSize(8.5f).Bold().FontColor(accent); }
                if (s.ShowUnitColumn) { h.Cell().Element(Head).Text("Einheit").FontSize(8.5f).Bold().FontColor(accent); }
                h.Cell().Element(Head).AlignRight().Text("Einzelpreis").FontSize(8.5f).Bold().FontColor(accent);
                if (s.ShowVatColumn && !d.SmallBusiness) { h.Cell().Element(Head).AlignRight().Text("USt.").FontSize(8.5f).Bold().FontColor(accent); }
                if (s.ShowDiscountColumn) { h.Cell().Element(Head).AlignRight().Text("Rabatt").FontSize(8.5f).Bold().FontColor(accent); }
                h.Cell().Element(Head).AlignRight().Text("Betrag").FontSize(8.5f).Bold().FontColor(accent);
            });

            IContainer Body(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(2);
            foreach (var line in d.Lines)
            {
                table.Cell().Element(Body).Text(line.Position.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(Body).Column(col =>
                {
                    col.Item().Text(line.Title).SemiBold();
                    if (!string.IsNullOrWhiteSpace(line.Description))
                    {
                        col.Item().Text(line.Description).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    }
                });
                if (s.ShowQuantityColumn) { table.Cell().Element(Body).AlignRight().Text(GermanFormat.Quantity(line.Quantity)); }
                if (s.ShowUnitColumn) { table.Cell().Element(Body).Text(line.Unit); }
                table.Cell().Element(Body).AlignRight().Text(GermanFormat.Money(line.UnitPrice, d.Currency));
                if (s.ShowVatColumn && !d.SmallBusiness) { table.Cell().Element(Body).AlignRight().Text(GermanFormat.Percent(d.ReverseCharge ? 0 : line.VatRate)); }
                if (s.ShowDiscountColumn) { table.Cell().Element(Body).AlignRight().Text(line.DiscountPercent == 0 ? string.Empty : GermanFormat.Percent(line.DiscountPercent)); }
                table.Cell().Element(Body).AlignRight().Text(GermanFormat.Money(line.NetAmount, d.Currency));
            }
        });
    }

    private static void Totals(IContainer container, InvoiceDocument d, string accent)
    {
        var t = d.Totals;
        container.AlignRight().Width(230).Column(col =>
        {
            void Row(string label, string value, bool strong = false, bool rule = false)
            {
                var item = col.Item();
                if (rule)
                {
                    item = item.BorderTop(1).BorderColor(accent).PaddingTop(3);
                }

                item.Row(r =>
                {
                    var l = r.RelativeItem().Text(label);
                    var v = r.AutoItem().Text(value);
                    if (strong)
                    {
                        l.Bold();
                        v.Bold();
                    }
                });
            }

            if (d.DiscountPercent != 0 || t.DiscountAmount != 0)
            {
                Row("Zwischensumme", GermanFormat.Money(t.LineTotal, d.Currency));
                Row($"Rabatt {GermanFormat.Percent(d.DiscountPercent)}", GermanFormat.Money(-t.DiscountAmount, d.Currency));
            }

            Row(d.SmallBusiness ? "Gesamtbetrag" : "Nettobetrag", GermanFormat.Money(t.Net, d.Currency), strong: d.SmallBusiness, rule: d.SmallBusiness);
            if (!d.SmallBusiness)
            {
                foreach (var g in t.Groups.Where(g => g.Rate != 0 || d.ReverseCharge))
                {
                    Row($"zzgl. {GermanFormat.Percent(g.Rate)} Umsatzsteuer", GermanFormat.Money(g.Vat, d.Currency));
                }

                Row("Gesamtbetrag", GermanFormat.Money(t.Gross, d.Currency), strong: true, rule: true);
            }
        });
    }

    private static void Closing(IContainer container, InvoiceDocument d)
    {
        container.PaddingTop(6).Column(col =>
        {
            col.Spacing(8);
            if (d.TaxNote is { } note)
            {
                col.Item().Text(note).FontSize(9);
            }

            if (!string.IsNullOrWhiteSpace(d.Outro))
            {
                col.Item().Text(d.Outro);
            }

            if (d.Kind == InvoiceKind.Invoice && !string.IsNullOrWhiteSpace(d.Settings.InvoiceNote))
            {
                col.Item().Text(d.Settings.InvoiceNote).FontSize(9);
            }

            if (d.Kind == InvoiceKind.Invoice && d.Settings.ShowBankDetails && d.Bank is { } bank && !string.IsNullOrWhiteSpace(bank.Iban))
            {
                col.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Column(b =>
                    {
                        b.Item().Text("Bankverbindung").Bold().FontSize(9);
                        if (!string.IsNullOrWhiteSpace(bank.Holder)) { b.Item().Text($"Kontoinhaber: {bank.Holder}").FontSize(9); }
                        if (!string.IsNullOrWhiteSpace(bank.BankName)) { b.Item().Text($"Bank: {bank.BankName}").FontSize(9); }
                        b.Item().Text($"IBAN: {Iban.Format(bank.Iban)}").FontSize(9);
                        if (!string.IsNullOrWhiteSpace(bank.Bic)) { b.Item().Text($"BIC: {bank.Bic}").FontSize(9); }
                        if (!string.IsNullOrWhiteSpace(d.Number)) { b.Item().Text($"Verwendungszweck: {d.Number}").FontSize(9); }
                    });

                    if (d.Settings.ShowPaymentQr && Iban.IsValid(bank.Iban) && d.Totals.Gross > 0 && d.Currency == "EUR")
                    {
                        row.ConstantItem(70).Height(70).Svg(PaymentQr(d, bank));
                    }
                });
            }
        });
    }

    /// <summary>The payload of a GiroCode (EPC069-12): a banking app reads it and fills in the transfer.</summary>
    public static string EpcPayload(InvoiceDocument d, BankInfo bank)
    {
        static string Clean(string value, int max)
        {
            var text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > max ? text[..max] : text;
        }

        var amount = d.Totals.Gross.ToString("0.00", CultureInfo.InvariantCulture);
        var lines = new[]
        {
            "BCD", "002", "1", "SCT",
            Clean(bank.Bic ?? string.Empty, 11),
            Clean(string.IsNullOrWhiteSpace(bank.Holder) ? d.Seller.Name : bank.Holder, 70),
            Iban.Normalize(bank.Iban),
            "EUR" + amount,
            string.Empty, string.Empty,
            Clean($"{d.Title} {d.Number}".Trim(), 140),
            string.Empty,
        };
        return string.Join('\n', lines);
    }

    private static string PaymentQr(InvoiceDocument d, BankInfo bank)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(EpcPayload(d, bank), QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(4, "#000000", "#ffffff", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    private static void Footer(IContainer container, InvoiceDocument d, string accent)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.6f).LineColor(accent);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(d.Seller.Name).FontSize(7.5f).Bold();
                    if (!string.IsNullOrWhiteSpace(d.Seller.Contact)) { c.Item().Text(d.Seller.Contact).FontSize(7.5f); }
                    c.Item().Text(d.Seller.Street).FontSize(7.5f);
                    c.Item().Text(d.Seller.CityLine).FontSize(7.5f);
                });
                row.RelativeItem().Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(d.Seller.Phone)) { c.Item().Text($"Telefon: {d.Seller.Phone}").FontSize(7.5f); }
                    if (!string.IsNullOrWhiteSpace(d.Seller.Email)) { c.Item().Text(d.Seller.Email).FontSize(7.5f); }
                    if (!string.IsNullOrWhiteSpace(d.Seller.Website)) { c.Item().Text(d.Seller.Website).FontSize(7.5f); }
                });
                row.RelativeItem().Column(c =>
                {
                    if (!string.IsNullOrWhiteSpace(d.Seller.TaxNumber)) { c.Item().Text($"Steuernr.: {d.Seller.TaxNumber}").FontSize(7.5f); }
                    if (!string.IsNullOrWhiteSpace(d.Seller.VatId)) { c.Item().Text($"USt-IdNr.: {d.Seller.VatId}").FontSize(7.5f); }
                    if (d.Bank is { } bank && !string.IsNullOrWhiteSpace(bank.Iban))
                    {
                        c.Item().Text($"IBAN: {Iban.Format(bank.Iban)}").FontSize(7.5f);
                        if (!string.IsNullOrWhiteSpace(bank.Bic)) { c.Item().Text($"BIC: {bank.Bic}").FontSize(7.5f); }
                    }
                });
            });

            if (!string.IsNullOrWhiteSpace(d.Settings.FooterNote))
            {
                col.Item().PaddingTop(3).Text(d.Settings.FooterNote).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
            }

            if (d.Settings.ShowPageNumbers)
            {
                col.Item().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Colors.Grey.Darken1));
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                    t.Span(" von ");
                    t.TotalPages();
                });
            }
        });
    }
}
