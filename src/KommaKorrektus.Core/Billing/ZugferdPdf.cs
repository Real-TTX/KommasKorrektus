using System.Text;
using QuestPDF.Fluent;

namespace KommaKorrektus.Core.Billing;

/// <summary>
/// A ZUGFeRD / Factur-X invoice: the PDF the customer reads, as PDF/A-3, with the invoice data as an XML file inside (<c>factur-x.xml</c>, related to the
/// document as its source) and the XMP metadata that says so. A program that reads e-invoices takes the XML, a person opens the PDF.
/// </summary>
public static class ZugferdPdf
{
    public const string FileName = "factur-x.xml";

    /// <summary>The XMP the PDF needs: the extension schema of Factur-X (PDF/A requires every property to be declared) and the properties themselves.</summary>
    public static string XmpMetadata(string conformanceLevel = "EN 16931") => $$"""
        <rdf:Description xmlns:pdfaExtension="http://www.aiim.org/pdfa/ns/extension/" xmlns:pdfaSchema="http://www.aiim.org/pdfa/ns/schema#" xmlns:pdfaProperty="http://www.aiim.org/pdfa/ns/property#" rdf:about="">
          <pdfaExtension:schemas>
            <rdf:Bag>
              <rdf:li rdf:parseType="Resource">
                <pdfaSchema:schema>Factur-X PDFA Extension Schema</pdfaSchema:schema>
                <pdfaSchema:namespaceURI>urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#</pdfaSchema:namespaceURI>
                <pdfaSchema:prefix>fx</pdfaSchema:prefix>
                <pdfaSchema:property>
                  <rdf:Seq>
                    <rdf:li rdf:parseType="Resource">
                      <pdfaProperty:name>DocumentFileName</pdfaProperty:name>
                      <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                      <pdfaProperty:category>external</pdfaProperty:category>
                      <pdfaProperty:description>name of the embedded XML invoice file</pdfaProperty:description>
                    </rdf:li>
                    <rdf:li rdf:parseType="Resource">
                      <pdfaProperty:name>DocumentType</pdfaProperty:name>
                      <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                      <pdfaProperty:category>external</pdfaProperty:category>
                      <pdfaProperty:description>INVOICE</pdfaProperty:description>
                    </rdf:li>
                    <rdf:li rdf:parseType="Resource">
                      <pdfaProperty:name>Version</pdfaProperty:name>
                      <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                      <pdfaProperty:category>external</pdfaProperty:category>
                      <pdfaProperty:description>The actual version of the Factur-X XML schema</pdfaProperty:description>
                    </rdf:li>
                    <rdf:li rdf:parseType="Resource">
                      <pdfaProperty:name>ConformanceLevel</pdfaProperty:name>
                      <pdfaProperty:valueType>Text</pdfaProperty:valueType>
                      <pdfaProperty:category>external</pdfaProperty:category>
                      <pdfaProperty:description>The conformance level of the embedded Factur-X data</pdfaProperty:description>
                    </rdf:li>
                  </rdf:Seq>
                </pdfaSchema:property>
              </rdf:li>
            </rdf:Bag>
          </pdfaExtension:schemas>
        </rdf:Description>
        <rdf:Description xmlns:fx="urn:factur-x:pdfa:CrossIndustryDocument:invoice:1p0#" rdf:about="">
          <fx:DocumentType>INVOICE</fx:DocumentType>
          <fx:DocumentFileName>{{FileName}}</fx:DocumentFileName>
          <fx:Version>1.0</fx:Version>
          <fx:ConformanceLevel>{{conformanceLevel}}</fx:ConformanceLevel>
        </rdf:Description>
        """;

    /// <summary>Makes the PDF of the invoice and puts the XML into it. Returns the PDF/A-3 file.</summary>
    public static byte[] Create(InvoiceDocument document, DateTime nowUtc)
    {
        var xml = EInvoice.Create(document, EInvoiceProfile.Zugferd);
        var pdf = InvoicePdf.Render(document, pdfA: true);
        return Embed(pdf, xml, nowUtc);
    }

    public static byte[] Embed(byte[] pdfA, string xml, DateTime nowUtc)
    {
        var folder = Path.Combine(Path.GetTempPath(), "kk-zugferd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var input = Path.Combine(folder, "in.pdf");
            var attachment = Path.Combine(folder, FileName);
            var output = Path.Combine(folder, "out.pdf");
            File.WriteAllBytes(input, pdfA);
            File.WriteAllText(attachment, xml, new UTF8Encoding(false));

            DocumentOperation.LoadFile(input)
                .AddAttachment(new DocumentOperation.DocumentAttachment
                {
                    Key = "factur-x",
                    FilePath = attachment,
                    AttachmentName = FileName,
                    MimeType = "text/xml",
                    Description = "Factur-X / ZUGFeRD invoice",
                    CreationDate = nowUtc,
                    ModificationDate = nowUtc,
                    Relationship = DocumentOperation.DocumentAttachmentRelationship.Source,
                })
                .ExtendMetadata(XmpMetadata())
                .Save(output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // a temporary folder that cannot be removed now is removed with the next clean-up of the system
            }
        }
    }
}
