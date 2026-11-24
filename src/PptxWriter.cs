using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Klementina
{
    /// Writes a PowerPoint file straight from a PDF's pages, no PowerPoint needed: every slide is the page's size,
    /// the page without its text sits behind as the picture, and every text line is a real text box on top, in its
    /// place, in its font, size and colour, Arabic right to left. So the slides look like the PDF and the text can
    /// still be edited, moved and restyled.
    static class PptxWriter
    {
        const long EmuPerPt = 12700;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// pages: the text lines of every page (null entries for pages with no text); pictures: what goes behind
        /// each slide (the text-free page, or the whole page when it has no text).
        public static void Write(List<PdfText.ExactPage> pages, List<string> pictures, string dest)
        {
            if (pages.Count == 0) throw new Exception("PDF has no pages");
            // PowerPoint allows 1 to 56 inches a side; every slide in a file has the same size (the first page's)
            var first = pages[0];
            long cx = Clamp((long)(first.Width * EmuPerPt), 914400, 51206400), cy = Clamp((long)(first.Height * EmuPerPt), 914400, 51206400);

            if (File.Exists(dest)) File.Delete(dest);
            using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
            {
                var media = new List<string>();
                var slideIds = new StringBuilder();
                var presRels = new StringBuilder();
                var ct = new StringBuilder();
                presRels.Append(Rel("rId1", "slideMaster", "slideMasters/slideMaster1.xml"));
                presRels.Append(Rel("rId2", "theme", "theme/theme1.xml"));
                presRels.Append(Rel("rId3", "presProps", "presProps.xml"));
                presRels.Append(Rel("rId4", "viewProps", "viewProps.xml"));
                presRels.Append(Rel("rId5", "tableStyles", "tableStyles.xml"));

                for (int i = 0; i < pages.Count; i++)
                {
                    var p = pages[i];
                    // a page of another size is scaled to fit the slide
                    double sx = cx / (p.Width * EmuPerPt), sy = cy / (p.Height * EmuPerPt), k = Math.Min(sx, sy);
                    double offX = (cx - p.Width * EmuPerPt * k) / 2, offY = (cy - p.Height * EmuPerPt * k) / 2;

                    var shapes = new StringBuilder();
                    int id = 2;
                    var slideRels = new StringBuilder(Rel("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml"));
                    string pic = i < pictures.Count ? pictures[i] : null;
                    if (pic != null && File.Exists(pic))
                    {
                        string ext = Path.GetExtension(pic).TrimStart('.').ToLowerInvariant();
                        if (ext == "jpeg") ext = "jpg";
                        string name = "image" + (media.Count + 1) + "." + ext;
                        media.Add(name);
                        zip.CreateEntryFromFile(pic, "ppt/media/" + name, CompressionLevel.Optimal);
                        slideRels.Append(Rel("rId2", "image", "../media/" + name));
                        shapes.Append(Picture(id++, "rId2", (long)offX, (long)offY, (long)(p.Width * EmuPerPt * k), (long)(p.Height * EmuPerPt * k)));
                    }
                    if (p.HasText)
                        foreach (var l in p.Lines)
                            shapes.Append(TextBox(id++, l, p, k, offX, offY));

                    Add(zip, "ppt/slides/slide" + (i + 1) + ".xml", Slide(shapes.ToString()));
                    Add(zip, "ppt/slides/_rels/slide" + (i + 1) + ".xml.rels", Rels(slideRels.ToString()));
                    presRels.Append(Rel("rId" + (10 + i), "slide", "slides/slide" + (i + 1) + ".xml"));
                    slideIds.Append("<p:sldId id=\"" + (256 + i) + "\" r:id=\"rId" + (10 + i) + "\"/>");
                    ct.Append("<Override PartName=\"/ppt/slides/slide" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");
                }

                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>" +
                    "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>" +
                    "<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>" +
                    "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>" +
                    "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
                    "<Override PartName=\"/ppt/presProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presProps+xml\"/>" +
                    "<Override PartName=\"/ppt/viewProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml\"/>" +
                    "<Override PartName=\"/ppt/tableStyles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml\"/>" +
                    "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                    "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
                    ct + "</Types>");
                Add(zip, "_rels/.rels", Rels(
                    Rel("rId1", "officeDocument", "ppt/presentation.xml") +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    Rel("rId3", "extended-properties", "docProps/app.xml")));
                Add(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    "<dc:title>" + X(Path.GetFileNameWithoutExtension(dest)) + "</dc:title><dc:creator>Klementina</dc:creator></cp:coreProperties>");
                Add(zip, "docProps/app.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>Klementina</Application><Slides>" + pages.Count + "</Slides></Properties>");
                Add(zip, "ppt/presentation.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentation " + Ns + " saveSubsetFonts=\"1\">" +
                    "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
                    "<p:sldIdLst>" + slideIds + "</p:sldIdLst>" +
                    "<p:sldSz cx=\"" + cx + "\" cy=\"" + cy + "\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>" +
                    "<p:defaultTextStyle><a:defPPr><a:defRPr lang=\"ar-SA\"/></a:defPPr></p:defaultTextStyle></p:presentation>");
                Add(zip, "ppt/_rels/presentation.xml.rels", Rels(presRels.ToString()));
                Add(zip, "ppt/presProps.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:presentationPr " + Ns + "/>");
                Add(zip, "ppt/viewProps.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:viewPr " + Ns + "><p:normalViewPr><p:restoredLeft sz=\"15620\"/><p:restoredTop sz=\"94660\"/></p:normalViewPr><p:gridSpacing cx=\"76200\" cy=\"76200\"/></p:viewPr>");
                Add(zip, "ppt/tableStyles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a:tblStyleLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" def=\"{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}\"/>");
                Add(zip, "ppt/slideMasters/slideMaster1.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldMaster " + Ns + "><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>" + EmptyTree + "</p:cSld>" +
                    "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
                    "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
                    "<p:txStyles><p:titleStyle><a:lvl1pPr><a:defRPr sz=\"4400\"/></a:lvl1pPr></p:titleStyle><p:bodyStyle><a:lvl1pPr><a:defRPr sz=\"1800\"/></a:lvl1pPr></p:bodyStyle><p:otherStyle><a:lvl1pPr><a:defRPr sz=\"1800\"/></a:lvl1pPr></p:otherStyle></p:txStyles></p:sldMaster>");
                Add(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels", Rels(Rel("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml") + Rel("rId2", "theme", "../theme/theme1.xml")));
                Add(zip, "ppt/slideLayouts/slideLayout1.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sldLayout " + Ns + " type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\">" + EmptyTree + "</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
                Add(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", Rels(Rel("rId1", "slideMaster", "../slideMasters/slideMaster1.xml")));
                Add(zip, "ppt/theme/theme1.xml", ThemeXml);
            }
        }

        // ---------------- shapes ----------------

        static string Picture(int id, string rel, long x, long y, long w, long h)
        {
            return "<p:pic><p:nvPicPr><p:cNvPr id=\"" + id + "\" name=\"Page\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                   "<p:blipFill><a:blip r:embed=\"" + rel + "\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                   "<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + Math.Max(1, w) + "\" cy=\"" + Math.Max(1, h) + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>";
        }

        /// One text line as a borderless, unwrapped text box whose baseline lands on the PDF's baseline. Arabic boxes
        /// are anchored at their right edge, others at their left, so a font that runs a little wider or narrower
        /// grows away from the start of the line like the original.
        static string TextBox(int id, PdfText.ExactLine l, PdfText.ExactPage p, double k, double offX, double offY)
        {
            double size = Math.Max(1, l.Size);
            double baseFromTop = p.Y0 + p.Height - l.Baseline;
            var main = l.Runs.FirstOrDefault(r => r.Image == null && !string.IsNullOrEmpty(r.Text));
            string font = main != null && !string.IsNullOrEmpty(main.Font) ? main.Font : "Arial";
            double lineH = size * LineFactor(font);
            // (measured in PowerPoint: the baseline sits a further 0.095 em below the metric position)
            double top = baseFromTop - lineH * AscentShare(font) + size * 0.095;
            double width = Math.Max(4, l.Right - l.Left), slack = Math.Max(size * 2, width * 0.15);
            double left = (l.Rtl ? l.Left - slack : l.Left) - p.X0;
            long x = (long)(offX + left * EmuPerPt * k), y = (long)(offY + top * EmuPerPt * k);
            long w = (long)((width + slack) * EmuPerPt * k), h = (long)(lineH * EmuPerPt * k);

            // match the original line's width with character spacing (1/100 pt); PowerPoint lays text out a little
            // narrower than GDI measures it (measured: 3% at 11 pt, 0.5% from 14 pt up). Widening is kept small so
            // joined Arabic letters never visibly part.
            int spc = 0;
            try
            {
                double natural = l.Runs.Sum(r => DocxWriter.MeasurePt(r, "Arial", l.Rtl)) * (l.Size < 13 ? 0.97 : 0.995);
                int chars = l.Runs.Sum(r => r.Text == null ? 0 : r.Text.Length);
                if (chars > 1 && Math.Abs(natural - width) > width * 0.004)
                    spc = (int)Math.Round(Math.Max(-size * 0.12, Math.Min(size * 0.06, (width - natural) / chars)) * 100 * k);
            }
            catch { }

            var sb = new StringBuilder();
            sb.Append("<p:sp><p:nvSpPr><p:cNvPr id=\"" + id + "\" name=\"Text " + id + "\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr>");
            sb.Append("<p:spPr><a:xfrm><a:off x=\"" + x + "\" y=\"" + y + "\"/><a:ext cx=\"" + Math.Max(1, w) + "\" cy=\"" + Math.Max(1, h) + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>");
            sb.Append("<p:txBody><a:bodyPr wrap=\"none\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" anchor=\"t\" anchorCtr=\"0\"><a:noAutofit/></a:bodyPr><a:lstStyle/>");
            sb.Append("<a:p><a:pPr algn=\"" + (l.Rtl ? "r" : "l") + "\"" + (l.Rtl ? " rtl=\"1\"" : "") + "><a:lnSpc><a:spcPct val=\"100000\"/></a:lnSpc><a:spcBef><a:spcPts val=\"0\"/></a:spcBef><a:spcAft><a:spcPts val=\"0\"/></a:spcAft></a:pPr>");
            // numbers and Latin words get their own runs marked English: in a run marked Arabic PowerPoint would
            // draw 1101 as ١١٠١
            var pieces = new List<PdfText.Run>();
            foreach (var r in l.Runs)
            {
                if (r.Image != null || string.IsNullOrEmpty(r.Text)) continue;
                int at = 0;
                foreach (System.Text.RegularExpressions.Match m in DocxWriter.LtrPiece.Matches(r.Text))
                {
                    if (m.Index > at) pieces.Add(Piece(r, r.Text.Substring(at, m.Index - at)));
                    // "1." / "4-": Word (which made most of these PDFs) shows the dot or dash after the number, while
                    // the Unicode rules PowerPoint follows put it on the far side (and PowerPoint ignores embedding
                    // marks); stored as ".1" it shows as "1." just like the PDF
                    string v = m.Value;
                    if (l.Rtl && v.Length > 1 && !char.IsLetterOrDigit(v[v.Length - 1])) v = v[v.Length - 1] + v.Substring(0, v.Length - 1);
                    pieces.Add(Piece(r, v));
                    at = m.Index + m.Length;
                }
                if (at < r.Text.Length) pieces.Add(Piece(r, r.Text.Substring(at)));
            }
            foreach (var r in pieces)
            {
                double rs = Math.Max(1, r.Size > 0 ? r.Size : size) * k;
                string f = string.IsNullOrEmpty(r.Font) ? font : r.Font;
                // Arabic language for Arabic text and for the spaces and punctuation of an Arabic line; English for
                // the number and Latin pieces split out above
                bool hasArabic = r.Text.Any(c => c >= 0x0590 && c <= 0x08FF || c >= 0xFB1D && c <= 0xFEFF);
                bool arabic = hasArabic || (l.Rtl && !DocxWriter.LtrPiece.IsMatch(r.Text));
                sb.Append("<a:r><a:rPr lang=\"" + (arabic ? "ar-SA" : "en-US") + "\" altLang=\"en-US\" sz=\"" + (int)Math.Round(rs * 100) + "\"");
                if (r.Bold) sb.Append(" b=\"1\"");
                if (r.Italic) sb.Append(" i=\"1\"");
                if (spc != 0) sb.Append(" spc=\"" + spc + "\"");
                if (Math.Abs(r.Rise) >= 0.5) sb.Append(" baseline=\"" + (int)Math.Round(r.Rise / Math.Max(1, r.Size) * 100000) + "\"");
                sb.Append(" dirty=\"0\"><a:solidFill><a:srgbClr val=\"" + r.Color.ToString("X6") + "\"/></a:solidFill>");
                sb.Append("<a:latin typeface=\"" + X(f) + "\"/><a:cs typeface=\"" + X(f) + "\"/></a:rPr>");
                sb.Append("<a:t>" + X(r.Text.Replace('\n', ' ')) + "</a:t></a:r>");
            }
            sb.Append("</a:p></p:txBody></p:sp>");
            return sb.ToString();
        }

        static PdfText.Run Piece(PdfText.Run r, string text)
        {
            return new PdfText.Run { Text = text, Font = r.Font, Size = r.Size, Bold = r.Bold, Italic = r.Italic, Color = r.Color, Rise = r.Rise };
        }

        /// PowerPoint's single line height and where it puts the baseline, from the font's own metrics (the same
        /// numbers PowerPoint uses): the line is ascent + descent tall (× 1.2 for the usual leading), the baseline
        /// shares it in the ascent's proportion.
        static readonly Dictionary<string, double[]> metrics = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

        static double[] Metrics(string font)
        {
            lock (metrics)
            {
                double[] v;
                if (metrics.TryGetValue(font, out v)) return v;
                v = new[] { 1.15, 0.81 };
                try
                {
                    using (var ff = new System.Drawing.FontFamily(font))
                    {
                        var st = ff.IsStyleAvailable(System.Drawing.FontStyle.Regular) ? System.Drawing.FontStyle.Regular : System.Drawing.FontStyle.Bold;
                        double em = ff.GetEmHeight(st), a = ff.GetCellAscent(st), d = ff.GetCellDescent(st);
                        if (em > 0 && a > 0) v = new[] { (a + d) / em, a / (a + d) };
                    }
                }
                catch { }
                metrics[font] = v;
                return v;
            }
        }

        internal static double LineFactor(string font) { return Metrics(font)[0] * LineScale; }
        internal static double AscentShare(string font) { return Metrics(font)[1]; }
        internal static double LineScale = 1.2;

        // ---------------- package helpers ----------------

        const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";
        const string EmptyTree = "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr></p:spTree>";

        static string Slide(string shapes)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><p:sld " + Ns + "><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
                   "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
                   shapes + "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
        }

        static string Rel(string id, string type, string target)
        {
            return "<Relationship Id=\"" + id + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + type + "\" Target=\"" + target + "\"/>";
        }

        static string Rels(string body)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + body + "</Relationships>";
        }

        static void Add(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }

        static long Clamp(long v, long lo, long hi) { return Math.Max(lo, Math.Min(hi, v)); }

        static string X(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c < 0x20 && c != '\t') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        const string ThemeXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Klementina\"><a:themeElements>" +
            "<a:clrScheme name=\"Klementina\"><a:dk1><a:srgbClr val=\"000000\"/></a:dk1><a:lt1><a:srgbClr val=\"FFFFFF\"/></a:lt1><a:dk2><a:srgbClr val=\"3A2416\"/></a:dk2><a:lt2><a:srgbClr val=\"FFF6EE\"/></a:lt2>" +
            "<a:accent1><a:srgbClr val=\"F06014\"/></a:accent1><a:accent2><a:srgbClr val=\"FFC85C\"/></a:accent2><a:accent3><a:srgbClr val=\"2E9F5A\"/></a:accent3><a:accent4><a:srgbClr val=\"3A7BD5\"/></a:accent4>" +
            "<a:accent5><a:srgbClr val=\"8E5AC8\"/></a:accent5><a:accent6><a:srgbClr val=\"D04848\"/></a:accent6><a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>" +
            "<a:fontScheme name=\"Klementina\"><a:majorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"Arial\"/></a:majorFont><a:minorFont><a:latin typeface=\"Arial\"/><a:ea typeface=\"\"/><a:cs typeface=\"Arial\"/></a:minorFont></a:fontScheme>" +
            "<a:fmtScheme name=\"Klementina\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
            "<a:lnStyleLst><a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
            "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
            "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme>" +
            "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
    }
}
