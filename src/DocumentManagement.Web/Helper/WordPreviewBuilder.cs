using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DocumentManagement.Web.Helper;

// Browsers can't show .docx, so this builds a simple HTML copy: text, headings, lists, tables and pictures.
public interface IWordPreviewBuilder
{
    string BuildHtml(Stream oStream, string strFileName);
}

public class WordPreviewBuilder : IWordPreviewBuilder
{
    public const string WordContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const long lMaxPictureBytes = 5 * 1024 * 1024;

    private static readonly XNamespace xnsWord = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace xnsRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace xnsPackageRelationship = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace xnsDrawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static readonly Dictionary<string, string> dicPictureContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp"
    };

    private const string strPageStyle =
        "body{font-family:Segoe UI,Arial,sans-serif;font-size:15px;line-height:1.5;color:#212529;margin:0;padding:24px 32px;white-space:pre-wrap;}" +
        "p{margin:0 0 8px;min-height:1em}h1,h2,h3,h4,h5,h6{margin:16px 0 8px;line-height:1.25}" +
        "ul{margin:0 0 8px;padding-left:24px}table{border-collapse:collapse;margin:0 0 12px;white-space:normal}" +
        "td{border:1px solid #adb5bd;padding:4px 8px;vertical-align:top}img{max-width:100%;height:auto}" +
        ".preview-message{color:#6c757d;font-style:italic}";

    public string BuildHtml(Stream oStream, string strFileName)
    {
        try
        {
            using var oZip = new ZipArchive(oStream, ZipArchiveMode.Read, leaveOpen: true);
            ZipArchiveEntry? oDocumentEntry = oZip.GetEntry("word/document.xml");
            XElement? oBody = oDocumentEntry is null ? null : LoadXml(oDocumentEntry).Root?.Element(xnsWord + "body");
            if (oBody is null)
            {
                return BuildPage(strFileName, MessageHtml("This Word file has no readable content. Download it to open it in Word."));
            }

            var oPage = new WordPage(oZip, LoadPictureTargets(oZip));
            var sbHtml = new StringBuilder();
            oPage.AppendBlocks(sbHtml, oBody);
            return BuildPage(strFileName, sbHtml.ToString());
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            return BuildPage(strFileName, MessageHtml("The preview could not be built from this file. Download it to open it in Word."));
        }
    }

    private static XDocument LoadXml(ZipArchiveEntry oEntry)
    {
        // No DTDs, so the XML cannot pull in outside files.
        var oSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using Stream oEntryStream = oEntry.Open();
        using var oReader = XmlReader.Create(oEntryStream, oSettings);
        return XDocument.Load(oReader);
    }

    private static Dictionary<string, string> LoadPictureTargets(ZipArchive oZip)
    {
        var dicTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        ZipArchiveEntry? oRelationshipEntry = oZip.GetEntry("word/_rels/document.xml.rels");
        if (oRelationshipEntry is null)
        {
            return dicTargets;
        }

        foreach (XElement oRelationship in LoadXml(oRelationshipEntry).Root?.Elements(xnsPackageRelationship + "Relationship") ?? [])
        {
            string? strID = (string?)oRelationship.Attribute("Id");
            string? strTarget = (string?)oRelationship.Attribute("Target");
            if (strID is null || strTarget is null || (string?)oRelationship.Attribute("TargetMode") == "External")
            {
                continue;
            }
            dicTargets[strID] = strTarget.StartsWith('/') ? strTarget.TrimStart('/') : "word/" + strTarget;
        }
        return dicTargets;
    }

    private static string MessageHtml(string strMessage) => $"<p class=\"preview-message\">{WebUtility.HtmlEncode(strMessage)}</p>";

    private static string BuildPage(string strFileName, string strBodyHtml) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(strFileName) +
        "</title><style>" + strPageStyle + "</style></head><body>" + strBodyHtml + "</body></html>";

    private sealed class WordPage
    {
        private readonly ZipArchive oZip;
        private readonly Dictionary<string, string> dicPictureTargets;

        public WordPage(ZipArchive oZip, Dictionary<string, string> dicPictureTargets)
        {
            this.oZip = oZip;
            this.dicPictureTargets = dicPictureTargets;
        }

        public void AppendBlocks(StringBuilder sbHtml, XElement oContainer)
        {
            bool bInList = false;
            foreach (XElement oBlock in oContainer.Elements())
            {
                bool bIsListItem = oBlock.Name == xnsWord + "p" && IsListParagraph(oBlock.Element(xnsWord + "pPr"));
                if (bIsListItem != bInList)
                {
                    sbHtml.Append(bIsListItem ? "<ul>" : "</ul>");
                    bInList = bIsListItem;
                }

                if (oBlock.Name == xnsWord + "p")
                {
                    AppendParagraph(sbHtml, oBlock, bIsListItem);
                }
                else if (oBlock.Name == xnsWord + "tbl")
                {
                    AppendTable(sbHtml, oBlock);
                }
                else if (oBlock.Name == xnsWord + "sdt" && oBlock.Element(xnsWord + "sdtContent") is XElement oContent)
                {
                    AppendBlocks(sbHtml, oContent);
                }
            }
            if (bInList)
            {
                sbHtml.Append("</ul>");
            }
        }

        // Word's "List Bullet" / "List Number" styles keep the numbering in the style, not on the paragraph.
        private static bool IsListParagraph(XElement? oParagraphProperties)
        {
            if (oParagraphProperties?.Element(xnsWord + "numPr") is not null)
            {
                return true;
            }
            string? strStyle = (string?)oParagraphProperties?.Element(xnsWord + "pStyle")?.Attribute(xnsWord + "val");
            return strStyle is not null
                && (strStyle.StartsWith("ListBullet", StringComparison.OrdinalIgnoreCase) || strStyle.StartsWith("ListNumber", StringComparison.OrdinalIgnoreCase));
        }

        private void AppendParagraph(StringBuilder sbHtml, XElement oParagraph, bool bIsListItem)
        {
            string strTag = bIsListItem ? "li" : GetHeadingTag(oParagraph) ?? "p";
            sbHtml.Append('<').Append(strTag).Append('>');

            // Text boxes are skipped; deleted text (w:delText) is never written.
            foreach (XElement oRun in oParagraph.Descendants(xnsWord + "r"))
            {
                if (oRun.Ancestors().TakeWhile(oAncestor => oAncestor != oParagraph).Any(oAncestor => oAncestor.Name == xnsWord + "txbxContent"))
                {
                    continue;
                }
                AppendRun(sbHtml, oRun);
            }

            sbHtml.Append("</").Append(strTag).Append('>');
        }

        private void AppendRun(StringBuilder sbHtml, XElement oRun)
        {
            XElement? oRunProperties = oRun.Element(xnsWord + "rPr");
            var lstClosingTags = new List<string>();
            foreach ((string strProperty, string strTag) in new[] { ("b", "strong"), ("i", "em"), ("u", "u"), ("strike", "s") })
            {
                if (IsOn(oRunProperties?.Element(xnsWord + strProperty)))
                {
                    sbHtml.Append('<').Append(strTag).Append('>');
                    lstClosingTags.Insert(0, $"</{strTag}>");
                }
            }

            foreach (XElement oPart in oRun.Elements())
            {
                if (oPart.Name == xnsWord + "t")
                {
                    sbHtml.Append(WebUtility.HtmlEncode(oPart.Value));
                }
                else if (oPart.Name == xnsWord + "tab")
                {
                    sbHtml.Append('\t');
                }
                else if (oPart.Name == xnsWord + "br" || oPart.Name == xnsWord + "cr")
                {
                    sbHtml.Append("<br>");
                }
                else if (oPart.Name == xnsWord + "drawing")
                {
                    AppendPictures(sbHtml, oPart);
                }
            }

            lstClosingTags.ForEach(strTag => sbHtml.Append(strTag));
        }

        private void AppendPictures(StringBuilder sbHtml, XElement oDrawing)
        {
            foreach (XElement oBlip in oDrawing.Descendants(xnsDrawing + "blip"))
            {
                string? strRelationshipID = (string?)oBlip.Attribute(xnsRelationship + "embed");
                if (strRelationshipID is null || !dicPictureTargets.TryGetValue(strRelationshipID, out string? strPath)
                    || !dicPictureContentTypes.TryGetValue(Path.GetExtension(strPath), out string? strContentType))
                {
                    continue;  // linked or unsupported pictures (e.g. .emf) are not shown
                }

                ZipArchiveEntry? oPicture = oZip.GetEntry(strPath);
                if (oPicture is null || oPicture.Length > lMaxPictureBytes)
                {
                    continue;
                }

                using var oPictureStream = new MemoryStream();
                using (Stream oEntryStream = oPicture.Open())
                {
                    oEntryStream.CopyTo(oPictureStream);
                }
                sbHtml.Append("<img alt=\"\" src=\"data:").Append(strContentType).Append(";base64,")
                      .Append(Convert.ToBase64String(oPictureStream.GetBuffer(), 0, (int)oPictureStream.Length)).Append("\">");
            }
        }

        private void AppendTable(StringBuilder sbHtml, XElement oTable)
        {
            sbHtml.Append("<table>");
            foreach (XElement oRow in oTable.Elements(xnsWord + "tr"))
            {
                sbHtml.Append("<tr>");
                foreach (XElement oCell in oRow.Elements(xnsWord + "tc"))
                {
                    int iColumnSpan = (int?)oCell.Element(xnsWord + "tcPr")?.Element(xnsWord + "gridSpan")?.Attribute(xnsWord + "val") ?? 1;
                    sbHtml.Append(iColumnSpan > 1 ? $"<td colspan=\"{iColumnSpan}\">" : "<td>");
                    AppendBlocks(sbHtml, oCell);
                    sbHtml.Append("</td>");
                }
                sbHtml.Append("</tr>");
            }
            sbHtml.Append("</table>");
        }

        private static string? GetHeadingTag(XElement oParagraph)
        {
            string? strStyle = (string?)oParagraph.Element(xnsWord + "pPr")?.Element(xnsWord + "pStyle")?.Attribute(xnsWord + "val");
            if (strStyle is null)
            {
                return null;
            }
            if (strStyle.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                return "h1";
            }
            if (strStyle.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) && int.TryParse(strStyle.AsSpan(7), out int iLevel) && iLevel is >= 1 and <= 6)
            {
                return "h" + Math.Min(iLevel + 1, 6);
            }
            return null;
        }

        // <w:b/> is on; <w:b w:val="0"/> or "false" is off.
        private static bool IsOn(XElement? oProperty)
        {
            if (oProperty is null)
            {
                return false;
            }
            string? strValue = (string?)oProperty.Attribute(xnsWord + "val");
            return strValue is null || !(strValue is "0" or "false" or "none");
        }
    }
}
