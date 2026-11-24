using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Klementina
{
    /// Writes a .docx straight from the blocks PdfText read out of a PDF — no Word needed. Keeps the text with its
    /// fonts, sizes, colours, bold/italic; headings, bullet lists and tables; pictures in their place; shaded
    /// paragraphs and cells; a page colour and full-page background pictures; right-to-left paragraphs for Arabic.
    static class DocxWriter
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        class Media { public string Id, Name, Source; }

        // pictures and drawing ids of the document being written (runs inside tables add pictures too)
        [ThreadStatic] static List<Media> curMedia;
        [ThreadStatic] static int curDocPr;
        [ThreadStatic] static int curFitId;

        /// With exact pages (and their text-free backgrounds) a page is written as an exact copy: the background
        /// carries every line, box and picture, and each text line sits on top exactly where it was, still editable.
        /// Pages without them (scans) use the reflowed blocks.
        public static void Write(List<PdfText.Page> pages, string dest, List<PdfText.ExactPage> exact = null, List<string> backgrounds = null)
        {
            var media = new List<Media>();
            var first = pages.FirstOrDefault(p => !p.Ocr) ?? pages.FirstOrDefault() ?? new PdfText.Page();
            var allRuns = pages.SelectMany(p => PdfText.Flatten(p.Blocks)).SelectMany(b => b.Kind == PdfText.Kind.Row ? b.CellRuns.SelectMany(c => c) : b.Runs).ToList();
            if (exact != null) allRuns.AddRange(exact.Where(e => e != null).SelectMany(e => e.Lines).SelectMany(l => l.Runs));
            string bodyFont = allRuns.Where(r => !string.IsNullOrEmpty(r.Font)).GroupBy(r => r.Font).OrderByDescending(g => g.Sum(r => r.Text.Length)).Select(g => g.Key).FirstOrDefault() ?? "Segoe UI";
            double bodySize = allRuns.GroupBy(r => r.Size).OrderByDescending(g => g.Sum(r => r.Text.Length)).Select(g => g.Key).FirstOrDefault();
            if (bodySize < 6 || bodySize > 30) bodySize = 11;
            bool docRtl = pages.SelectMany(p => PdfText.Flatten(p.Blocks)).Count(b => b.Rtl) * 2 > Math.Max(1, pages.SelectMany(p => PdfText.Flatten(p.Blocks)).Count(b => b.Kind != PdfText.Kind.Image));
            // one set of margins for the whole document: the narrowest found on any page
            var real = pages.Where(p => !p.Ocr).ToList();
            if (real.Count > 0) { first.Left = real.Min(p => p.Left); first.Right = real.Min(p => p.Right); first.TopMargin = real.Min(p => p.TopMargin); }
            double contentWidth = first.Width - first.Left - first.Right;
            double boxL = first.X0 + first.Left, boxR = first.X0 + first.Width - first.Right;
            curMedia = media;
            curDocPr = 0;

            // every page is its own section (its own size), so pages never run into each other
            var body = new StringBuilder();
            string lastSect = null;
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var page = pages[pi];
                var ex = exact != null && pi < exact.Count ? exact[pi] : null;
                string bg = backgrounds != null && pi < backgrounds.Count ? backgrounds[pi] : null;
                string sect;
                if (ex != null && ex.HasText && bg != null && File.Exists(bg))
                {
                    body.Append(ExactPageXml(ex, bg, bodyFont, bodySize));
                    sect = SectPr(ex.Width, ex.Height, ExactTop, 0, 0, 0, docRtl);
                }
                else
                {
                    bool breakBefore = false;
                    string anchor = null;
                    if (!string.IsNullOrEmpty(page.BackgroundImage) && File.Exists(page.BackgroundImage))
                    {
                        var m = AddMedia(media, page.BackgroundImage);
                        var rc = page.BackgroundRect;
                        anchor = AnchorXml(m, ++curDocPr, Emu(rc.X - page.X0), Emu(page.Y0 + page.Height - rc.Bottom), Emu(rc.Width), Emu(rc.Height));
                    }
                    if (page.Blocks.Count == 0) body.Append(Paragraph(Ppr(null, false, false, null, -1, 0, false), anchor != null ? "<w:r>" + anchor + "</w:r>" : ""));
                    else body.Append(RenderBlocks(page.Blocks, contentWidth, boxL, boxR, docRtl, bodyFont, bodySize, ref breakBefore, ref anchor));
                    sect = SectPr(page.Width, page.Height, first.TopMargin, first.Right, Math.Max(36, first.TopMargin * 0.8), first.Left, docRtl);
                }
                if (pi < pages.Count - 1) body.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/>" + sect + "</w:pPr></w:p>");
                else lastSect = sect;
            }

            // page colour: from the first page that has one
            var colored = pages.FirstOrDefault(p => p.Background >= 0);
            string background = colored != null ? "<w:background w:color=\"" + Hex(colored.Background) + "\"/>" : "";

            var doc = new StringBuilder();
            doc.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            doc.Append("<w:document xmlns:w=\"" + W + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"" +
                       " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"" +
                       " xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"" +
                       " xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">");
            doc.Append(background).Append("<w:body>").Append(body);
            doc.Append(lastSect ?? SectPr(first.Width, first.Height, first.TopMargin, first.Right, Math.Max(36, first.TopMargin * 0.8), first.Left, docRtl));
            doc.Append("</w:body></w:document>");

            if (File.Exists(dest)) File.Delete(dest);
            using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
            {
                Entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                    "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
                    "<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>" +
                    "</Types>");
                Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                    "<Relationship Id=\"rIdSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
                    "<Relationship Id=\"rIdNumbering\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>");
                foreach (var m in media)
                {
                    rels.Append("<Relationship Id=\"" + m.Id + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/" + m.Name + "\"/>");
                    zip.CreateEntryFromFile(m.Source, "word/media/" + m.Name);
                }
                rels.Append("</Relationships>");
                Entry(zip, "word/_rels/document.xml.rels", rels.ToString());
                Entry(zip, "word/document.xml", doc.ToString());
                Entry(zip, "word/styles.xml", Styles(bodyFont, bodySize, docRtl));
                Entry(zip, "word/settings.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"" + W + "\">" +
                    // open in Print Layout even when Word is set to Draft/Web view, which ignore page positions
                    "<w:view w:val=\"print\"/><w:zoom w:percent=\"100\"/>" +
                    // no red/blue proofing squiggles over a copied document
                    "<w:hideSpellingErrors/><w:hideGrammaticalErrors/>" +
                    (colored != null ? "<w:displayBackgroundShape/>" : "") + "<w:defaultTabStop w:val=\"720\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
                Entry(zip, "word/numbering.xml", Numbering(docRtl));
            }
        }

        static string SectPr(double w, double h, double top, double right, double bottom, double left, bool rtl)
        {
            return "<w:sectPr><w:pgSz w:w=\"" + Tw(w) + "\" w:h=\"" + Tw(h) + "\"" + (w > h ? " w:orient=\"landscape\"" : "") + "/>" +
                   "<w:pgMar w:top=\"" + Tw(top) + "\" w:right=\"" + Tw(right) + "\" w:bottom=\"" + Tw(bottom) + "\" w:left=\"" + Tw(left) +
                   "\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/>" + (rtl ? "<w:bidi/>" : "") + "</w:sectPr>";
        }

        /// Top margin of an exact page; the rows are placed from here down with measured spacing.
        const double ExactTop = 0;   // the page starts at its very top, so the background needs no offset (Word ignores negative ones)

        /// An exact copy of a page, built from ordinary lines so it looks right in every view and program: the
        /// text-free rendering sits behind the page (anchored to its first line), then each row of text follows with
        /// exactly the spacing that puts it back on its original baseline. A row holding one piece is a paragraph
        /// indented to its original edge; a row with several pieces side by side (a title on the right, a date on the
        /// left) is an invisible one-row table with a cell per piece.
        static string ExactPageXml(PdfText.ExactPage p, string background, string bodyFont, double bodySize)
        {
            var sb = new StringBuilder();
            var m = AddMedia(curMedia, background);
            sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"20\" w:lineRule=\"exact\"/></w:pPr><w:r>")
              .Append(AnchorXml(m, ++curDocPr, 0, 0, Emu(p.Width), Emu(p.Height), "paragraph")).Append("</w:r></w:p>");
            double y = ExactTop + 1;      // where the next row starts, in points from the top of the page

            // rows: pieces on (about) the same baseline that don't overlap each other
            var rows = new List<List<PdfText.ExactLine>>();
            foreach (var l in p.Lines.OrderByDescending(l => l.Baseline).ThenBy(l => l.Left))
            {
                var row = rows.Count > 0 ? rows[rows.Count - 1] : null;
                if (row != null && Math.Abs(row[0].Baseline - l.Baseline) < Math.Min(row[0].Size, l.Size) * 0.5
                    && !row.Any(o => o.Left < l.Right + 2 && l.Left < o.Right + 2))
                    row.Add(l);
                else rows.Add(new List<PdfText.ExactLine> { l });
            }
            for (int ri = 0; ri < rows.Count; ri++)
            {
                var row = rows[ri];
                var main = row.OrderByDescending(l => l.Size).First();
                double size = row.Max(l => l.Size);
                double baseFromTop = p.Y0 + p.Height - main.Baseline;
                // the line is as tall as the distance to the next row (never taller than 1.3× the size), so rows
                // never push each other down and the page doesn't drift
                double lineH = Math.Max(4, size * 1.3);
                if (ri + 1 < rows.Count)
                {
                    double pitch = (p.Y0 + p.Height - rows[ri + 1].OrderByDescending(l => l.Size).First().Baseline) - baseFromTop;
                    if (pitch > 0 && pitch < lineH) lineH = Math.Max(size * 1.05, pitch);
                }
                var mainRun = main.Runs.FirstOrDefault(r => r.Image == null && !string.IsNullOrEmpty(r.Text));
                double share = AscentShare(mainRun == null || string.IsNullOrEmpty(mainRun.Font) ? bodyFont : mainRun.Font);
                // a row close under the previous one gets a shorter line, so its top doesn't collide with that row
                // and push the text below its baseline (and off its underline)
                double room = (baseFromTop - y) / share;
                if (room < lineH) lineH = Math.Max(size * 0.95, room);
                double top = baseFromTop - lineH * share;
                double gap = Math.Max(0, top - y);
                if (row.Count == 1)
                {
                    sb.Append(PiecePara(row[0], p.X0, p.X0 + p.Width, gap, lineH, bodyFont, bodySize));
                }
                else
                {
                    if (gap >= 0.5)
                        sb.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"" + Math.Max(1, Tw(gap)) + "\" w:lineRule=\"exact\"/></w:pPr></w:p>");
                    var pieces = row.OrderBy(l => l.Left).ToList();
                    var edges = new List<double> { p.X0 };
                    for (int i = 0; i + 1 < pieces.Count; i++) edges.Add((pieces[i].Right + pieces[i + 1].Left) / 2);
                    edges.Add(p.X0 + p.Width);
                    var widths = new List<int>();
                    for (int i = 0; i < pieces.Count; i++) widths.Add(Math.Max(1, Tw(edges[i + 1] - edges[i])));
                    sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"" + widths.Sum() + "\" w:type=\"dxa\"/><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>");
                    foreach (var side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) sb.Append("<w:" + side + " w:val=\"nil\"/>");
                    sb.Append("</w:tblBorders><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"0\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
                    foreach (int w in widths) sb.Append("<w:gridCol w:w=\"" + w + "\"/>");
                    sb.Append("</w:tblGrid><w:tr><w:trPr><w:trHeight w:val=\"" + Tw(lineH) + "\" w:hRule=\"exact\"/></w:trPr>");
                    for (int i = 0; i < pieces.Count; i++)
                        sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"" + widths[i] + "\" w:type=\"dxa\"/></w:tcPr>")
                          .Append(PiecePara(pieces[i], edges[i], edges[i + 1], 0, lineH, bodyFont, bodySize)).Append("</w:tc>");
                    sb.Append("</w:tr></w:tbl>");
                }
                y = Math.Max(y, top) + lineH;
            }
            return sb.ToString();
        }

        /// One piece of text as a paragraph between two horizontal limits (page edges or a table cell): indented so
        /// its reading-start edge lands where the PDF had it — the right edge for Arabic, the left edge otherwise.
        static string PiecePara(PdfText.ExactLine l, double from, double to, double before, double lineH, string bodyFont, double bodySize)
        {
            var sb = new StringBuilder("<w:p><w:pPr>");
            if (l.Rtl) sb.Append("<w:bidi/>");
            sb.Append("<w:spacing w:before=\"" + Tw(before) + "\" w:after=\"0\" w:line=\"" + Tw(lineH) + "\" w:lineRule=\"exact\"/>");
            // in a right-to-left paragraph Word reads w:left as the start side, which is the right
            // (Word starts Arabic lines about 1 pt further out than the indent says; measured, so take it back)
            if (l.Rtl) sb.Append("<w:ind w:left=\"" + Tw(Math.Max(0, to - l.Right + RtlStartShift)) + "\" w:right=\"0\"/>");
            else sb.Append("<w:ind w:left=\"" + Tw(Math.Max(0, l.Left - from)) + "\" w:right=\"0\"/>");
            sb.Append("</w:pPr>");
            // Word draws the text in its own fonts, a little wider or narrower than the PDF did; make the line keep
            // its original width so it stays on its underline, dots and cell borders
            bool fit;
            int scale = WidthScale(l, bodyFont, out fit);
            if (!fit)
            {
                foreach (var r in l.Runs) sb.Append(RunXml(r, bodyFont, bodySize, scale));
                return sb.Append("</w:p>").ToString();
            }
            // Fit text lays its characters out one by one, which loses the bidi reordering of a number or a Latin word
            // inside Arabic ("1101" came out as "1011"), and of Arabic inside a left-to-right line. So those pieces
            // keep their natural width, and the rest of the line is fitted around them, piece by piece.
            var foreign = l.Rtl ? LtrPiece : RtlPiece;
            var pieces = new List<PdfText.Run>();
            var fits = new List<bool>();
            foreach (var r in l.Runs)
            {
                if (r.Image != null || string.IsNullOrEmpty(r.Text)) { pieces.Add(r); fits.Add(false); continue; }
                int at = 0;
                foreach (System.Text.RegularExpressions.Match m in foreign.Matches(r.Text))
                {
                    if (m.Index > at) { pieces.Add(Piece(r, r.Text.Substring(at, m.Index - at))); fits.Add(true); }
                    pieces.Add(Piece(r, m.Value)); fits.Add(false);
                    at = m.Index + m.Length;
                }
                if (at < r.Text.Length) { pieces.Add(Piece(r, r.Text.Substring(at))); fits.Add(true); }
            }
            double eff = ScaleEffect(scale) * (l.Size < 13 ? 0.984 : 1);
            var widths = pieces.Select(r => MeasurePt(r, bodyFont, l.Rtl) * eff).ToList();
            double fixedW = 0, fitW = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                if (fits[i] && widths[i] < 2) fits[i] = false;      // a lone quote or space isn't worth a fit region
                if (fits[i]) fitW += widths[i]; else fixedW += widths[i];
            }
            double ratio = fitW >= 3 ? (l.Right - l.Left - fixedW) / fitW : 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                bool f = fits[i] && ratio > 0.7 && ratio < 1.1;
                // (in an Arabic line every piece is marked right-to-left: Word ignores a fit region that follows a
                // left-to-right run, and a number in a right-to-left run still reads left to right)
                sb.Append(RunXml(pieces[i], bodyFont, bodySize, scale, f ? Tw(widths[i] * ratio) : 0, f ? ++curFitId : 0, l.Rtl));
            }
            return sb.Append("</w:p>").ToString();
        }

        internal static readonly System.Text.RegularExpressions.Regex LtrPiece = new System.Text.RegularExpressions.Regex(
            @"[0-9A-Za-z\u00C0-\u024F\u0660-\u0669\u06F0-\u06F9]+(?:[.,/:\-][0-9A-Za-z\u00C0-\u024F\u0660-\u0669\u06F0-\u06F9]+)*[.:\-]?");
        static readonly System.Text.RegularExpressions.Regex RtlPiece = new System.Text.RegularExpressions.Regex(
            @"[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]+(?:\s+[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]+)*");

        static PdfText.Run Piece(PdfText.Run r, string text)
        {
            return new PdfText.Run { Text = text, Font = r.Font, Size = r.Size, Bold = r.Bold, Italic = r.Italic, Color = r.Color, Rise = r.Rise };
        }

        const double RtlStartShift = 1.0;

        [ThreadStatic] static Dictionary<string, System.Drawing.Font> measureFonts;

        /// Fonts for measuring, made once per family/size/style (a page measures hundreds of runs in a few fonts).
        static System.Drawing.Font MeasureFont(string family, float size, System.Drawing.FontStyle style)
        {
            if (measureFonts == null) measureFonts = new Dictionary<string, System.Drawing.Font>();
            string key = family + "|" + size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)style;
            System.Drawing.Font f;
            if (!measureFonts.TryGetValue(key, out f))
            {
                if (measureFonts.Count > 64) { foreach (var old in measureFonts.Values) old.Dispose(); measureFonts.Clear(); }
                f = new System.Drawing.Font(family, size, style, System.Drawing.GraphicsUnit.Point);
                measureFonts[key] = f;
            }
            return f;
        }

        [ThreadStatic] static System.Drawing.Bitmap measureBmp;
        [ThreadStatic] static System.Drawing.Graphics measureG;

        /// How to make Word's rendering of a line exactly as wide as the PDF's: a letter scale (percent) that brings it
        /// to the target or a little past it, then Word's "fit text" to squeeze out the rest. Fit text works by
        /// character spacing, which is exact; it is only ever used to tighten, because widening Arabic that way opens
        /// gaps between joined letters, while the letter scale keeps them joined.
        internal static int WidthScale(PdfText.ExactLine l, string bodyFont, out bool fit)
        {
            fit = false;
            try
            {
                double target = l.Right - l.Left;
                if (target < 6 || l.Runs.Any(r => r.Image != null)) return 100;
                double natural = 0;
                foreach (var r in l.Runs) natural += MeasurePt(r, bodyFont, l.Rtl);
                // at body sizes GDI's whole-pixel font height comes out about 1.6% wider than Word's layout (measured
                // at 11 and 12 pt; from 14 pt up the two agree)
                if (l.Size < 13) natural *= 0.984;
                if (natural < 3) return 100;
                double k = target / natural;
                if (k < 0.7 || k > 1.4) return 100;
                if (Math.Abs(k - 1) < 0.004) return 100;
                fit = true;
                if (k < 1)
                {
                    if (k >= 0.9) return 100;
                    for (int w = 99; w >= 70; w--) if (ScaleEffect(w - 1) < k) return w;
                    return 70;
                }
                for (int w = 102; w <= 140; w++) if (ScaleEffect(w) >= k + 0.01) return w;
                return 140;
            }
            catch { fit = false; return 100; }
        }

        /// How much wider (or narrower) Word really draws text with a character scale of w percent. Measured in
        /// Word: below 100% it narrows the text about 3% more than asked (99% gives about 0.968), 101% does nothing,
        /// and above that each percent adds about 0.85%.
        static double ScaleEffect(int w)
        {
            if (w == 100 || w == 101) return 1;
            if (w < 100) return 0.968 - (99 - w) * 0.0095;
            return 1 + (w - 101) * 0.0085;
        }

        /// Width of a run as Word lays it out (GDI measurement at the real size on a 96-DPI surface: Word rounds glyph advances
        /// the same way, which a measurement at a larger size misses), in points.
        internal static double MeasurePt(PdfText.Run r, string bodyFont, bool rtl)
        {
            if (r.Image != null || string.IsNullOrEmpty(r.Text)) return 0;
            if (measureG == null)
            {
                measureBmp = new System.Drawing.Bitmap(1, 1);
                measureBmp.SetResolution(96, 96);
                measureG = System.Drawing.Graphics.FromImage(measureBmp);
            }
            string family = string.IsNullOrEmpty(r.Font) ? bodyFont : r.Font;
            var style = (r.Bold ? System.Drawing.FontStyle.Bold : 0) | (r.Italic ? System.Drawing.FontStyle.Italic : 0);
            var f = MeasureFont(family ?? "Segoe UI", (float)Math.Max(1, r.Size), style);
            {
                var flags = System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.SingleLine |
                            System.Windows.Forms.TextFormatFlags.NoPrefix | (rtl ? System.Windows.Forms.TextFormatFlags.RightToLeft : 0);
                var size = System.Windows.Forms.TextRenderer.MeasureText(measureG, r.Text.Replace("\n", " "), f, new System.Drawing.Size(int.MaxValue, int.MaxValue), flags);
                return size.Width * 72.0 / 96;
            }
        }

        /// Where Word puts the baseline inside an exact-height line. Measured in Word: it shares the line between the
        /// font's ascent and descent in their own proportion (Arial: 81% above the baseline), whatever the font size,
        /// so the baseline sits lineH × AscentShare below the line's top.
        static readonly Dictionary<string, double> ascentShares = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        static double AscentShare(string font)
        {
            font = string.IsNullOrEmpty(font) ? "Arial" : font;
            lock (ascentShares)
            {
                double v;
                if (ascentShares.TryGetValue(font, out v)) return v;
                v = 0.81;
                try
                {
                    using (var ff = new System.Drawing.FontFamily(font))
                    {
                        var st = ff.IsStyleAvailable(System.Drawing.FontStyle.Regular) ? System.Drawing.FontStyle.Regular : System.Drawing.FontStyle.Bold;
                        double a = ff.GetCellAscent(st), d = ff.GetCellDescent(st);
                        if (a > 0 && d >= 0) v = Math.Max(0.6, Math.Min(0.95, a / (a + d)));
                    }
                }
                catch { }   // a font that isn't installed: Word falls back to something Arial-like
                ascentShares[font] = v;
                return v;
            }
        }

        /// Paragraphs, pictures, tables and side-by-side columns of one page (or of one column inside a page).
        static string RenderBlocks(List<PdfText.Block> blocks, double contentWidth, double boxL, double boxR, bool docRtl, string bodyFont, double bodySize,
                                   ref bool breakBefore, ref string anchor)
        {
            var body = new StringBuilder();
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Kind == PdfText.Kind.Row || b.Kind == PdfText.Kind.Columns)
                {
                    // a table can't carry a page break or an anchored background: put them in a paragraph first
                    if (breakBefore || anchor != null)
                    {
                        body.Append(Paragraph(Ppr(null, breakBefore, b.Rtl, null, -1, 0, false), anchor != null ? "<w:r>" + anchor + "</w:r>" : ""));
                        breakBefore = false; anchor = null;
                    }
                    if (b.Kind == PdfText.Kind.Columns)
                    {
                        body.Append(ColumnsXml(b, contentWidth, boxL, boxR, docRtl, bodyFont, bodySize));
                        continue;
                    }
                    int end = i;
                    while (end + 1 < blocks.Count && blocks[end + 1].Kind == PdfText.Kind.Row) end++;
                    body.Append(Table(blocks.GetRange(i, end - i + 1), contentWidth, bodyFont, bodySize));
                    i = end;
                    continue;
                }
                var runs = new StringBuilder();
                if (anchor != null) { runs.Append("<w:r>").Append(anchor).Append("</w:r>"); anchor = null; }
                if (b.Kind == PdfText.Kind.Image)
                {
                    if (b.Image == null || !File.Exists(b.Image)) continue;
                    var m = AddMedia(curMedia, b.Image);
                    double w = b.ImageW, h = b.ImageH;
                    if (w > contentWidth && w > 0) { h = h * contentWidth / w; w = contentWidth; }
                    runs.Append("<w:r>").Append(InlineXml(m, ++curDocPr, Emu(w), Emu(h))).Append("</w:r>");
                    body.Append(Paragraph(Ppr(null, breakBefore, docRtl, ImageAlign(b.ImageRect, boxL, boxR), -1, b.SpaceAfter, false), runs.ToString()));
                    breakBefore = false;
                    continue;
                }
                foreach (var r in b.Runs) runs.Append(RunXml(r, bodyFont, bodySize));
                string style = b.Kind == PdfText.Kind.Heading1 ? "Heading1" : b.Kind == PdfText.Kind.Heading2 ? "Heading2" : b.Kind == PdfText.Kind.Heading3 ? "Heading3" : null;
                body.Append(Paragraph(Ppr(style, breakBefore, b.Rtl, b.Align, b.Shade, b.SpaceAfter, b.Kind == PdfText.Kind.Bullet), runs.ToString()));
                breakBefore = false;
            }
            return body.ToString();
        }

        /// Side-by-side columns as a borderless table: one cell per column, each laid out like a small page.
        static string ColumnsXml(PdfText.Block b, double contentWidth, double boxL, double boxR, bool docRtl, string bodyFont, double bodySize)
        {
            int n = b.Columns.Count;
            int cellW = (int)Math.Round(contentWidth * 20 / n);
            double gutter = b.ImageW > boxL && b.ImageW < boxR ? b.ImageW : (boxL + boxR) / 2;
            var sb = new StringBuilder("<w:tbl><w:tblPr>");
            if (b.Rtl) sb.Append("<w:bidiVisual/>");
            sb.Append("<w:tblW w:w=\"" + cellW * n + "\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>");
            foreach (var side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) sb.Append("<w:" + side + " w:val=\"nil\"/>");
            sb.Append("</w:tblBorders><w:tblCellMar><w:left w:w=\"80\" w:type=\"dxa\"/><w:right w:w=\"80\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
            for (int c = 0; c < n; c++) sb.Append("<w:gridCol w:w=\"" + cellW + "\"/>");
            sb.Append("</w:tblGrid><w:tr>");
            for (int c = 0; c < n; c++)
            {
                // the horizontal range of this column on the page, for aligning its pictures
                bool rightSide = b.Rtl ? c == 0 : c == n - 1;
                double l = n == 2 ? (rightSide ? gutter : boxL) : boxL, r = n == 2 ? (rightSide ? boxR : gutter) : boxR;
                bool noBreak = false; string noAnchor = null;
                string inner = RenderBlocks(b.Columns[c], contentWidth / n - 8, l, r, docRtl, bodyFont, bodySize, ref noBreak, ref noAnchor);
                if (inner.Length == 0 || inner.EndsWith("</w:tbl>", StringComparison.Ordinal)) inner += "<w:p/>";
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"" + cellW + "\" w:type=\"dxa\"/></w:tcPr>").Append(inner).Append("</w:tc>");
            }
            sb.Append("</w:tr></w:tbl>");
            return sb.ToString();
        }

        // ---------- pieces ----------

        static string Paragraph(string ppr, string runs) { return "<w:p>" + ppr + runs + "</w:p>"; }

        static string Ppr(string style, bool pageBreak, bool rtl, string align, int shade, double spaceAfter, bool bullet)
        {
            var sb = new StringBuilder("<w:pPr>");
            if (style != null) sb.Append("<w:pStyle w:val=\"" + style + "\"/>");
            if (pageBreak) sb.Append("<w:pageBreakBefore/>");
            if (bullet) sb.Append("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>");
            if (shade >= 0) sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"" + Hex(shade) + "\"/>");
            if (rtl) sb.Append("<w:bidi/>");
            sb.Append("<w:spacing w:before=\"0\" w:after=\"" + Math.Max(0, (int)Math.Round(spaceAfter * 20 * 0.8)) + "\"/>");
            string jc = Jc(align, rtl);
            if (jc != null) sb.Append("<w:jc w:val=\"" + jc + "\"/>");
            sb.Append("</w:pPr>");
            return sb.ToString();
        }

        /// In a right-to-left paragraph Word reads "left"/"right" from the reading side, so the natural side needs no value.
        static string Jc(string align, bool rtl)
        {
            switch (align)
            {
                case "center": return "center";
                case "both": return "both";
                case "right": return rtl ? null : "right";
                case "left": return rtl ? "right" : null;
                default: return null;
            }
        }

        static string RunXml(PdfText.Run r, string bodyFont, double bodySize, int scale = 100, int fitTw = 0, int fitId = 0, bool rtl = false)
        {
            if (r.Image != null)
            {
                if (!File.Exists(r.Image) || curMedia == null) return "";
                var m = AddMedia(curMedia, r.Image);
                return "<w:r>" + InlineXml(m, ++curDocPr, Emu(r.ImageW), Emu(r.ImageH)) + "</w:r>";
            }
            if (string.IsNullOrEmpty(r.Text)) return "";
            var sb = new StringBuilder("<w:r><w:rPr>");
            string font = string.IsNullOrEmpty(r.Font) ? bodyFont : r.Font;
            if (font != bodyFont) sb.Append("<w:rFonts w:ascii=\"" + X(font) + "\" w:hAnsi=\"" + X(font) + "\" w:cs=\"" + X(font) + "\" w:eastAsia=\"" + X(font) + "\"/>");
            if (r.Bold) sb.Append("<w:b/><w:bCs/>");
            if (r.Italic) sb.Append("<w:i/><w:iCs/>");
            if (!IsBlack(r.Color)) sb.Append("<w:color w:val=\"" + Hex(r.Color) + "\"/>");
            if (scale != 100) sb.Append("<w:w w:val=\"" + scale + "\"/>");
            if (Math.Abs(r.Rise) >= 0.5) sb.Append("<w:position w:val=\"" + (int)Math.Round(r.Rise * 2) + "\"/>");
            if (r.Size > 0 && Math.Abs(r.Size - bodySize) > 0.25) { int hp = (int)Math.Round(r.Size * 2); sb.Append("<w:sz w:val=\"" + hp + "\"/><w:szCs w:val=\"" + hp + "\"/>"); }
            if (fitTw > 0) sb.Append("<w:fitText w:val=\"" + fitTw + "\" w:id=\"" + fitId + "\"/>");
            if (rtl || r.Text.Any(IsRtlChar)) sb.Append("<w:rtl/>");
            // a line break inside a cell ("\n") becomes a Word line break
            string body = string.Join("</w:t><w:br/><w:t xml:space=\"preserve\">", r.Text.Split('\n').Select(X));
            sb.Append("</w:rPr><w:t xml:space=\"preserve\">").Append(body).Append("</w:t></w:r>");
            return sb.ToString();
        }

        /// Where a picture sat across the text column: centred, against one side, or nearer one side.
        static string ImageAlign(System.Drawing.RectangleF rc, double boxL, double boxR)
        {
            double width = boxR - boxL, centre = (rc.X + rc.Right) / 2;
            if (Math.Abs(centre - (boxL + boxR) / 2) < width * 0.06) return "center";
            if (rc.Right >= boxR - 15 && rc.X > boxL + 15) return "right";
            if (rc.X <= boxL + 15 && rc.Right < boxR - 15) return "left";
            return centre > (boxL + boxR) / 2 ? "right" : "left";
        }

        static string Table(List<PdfText.Block> rows, double contentWidth, string bodyFont, double bodySize)
        {
            int cols = rows.Max(r => r.CellRuns.Count);
            bool rtl = rows.Any(r => r.Rtl);
            // column widths from how wide each column's text is in the PDF, plus cell padding
            var widths = new double[cols];
            foreach (var r in rows)
                for (int c = 0; c < cols && r.CellLeft != null && c < r.CellLeft.Count; c++)
                    widths[c] = Math.Max(widths[c], r.CellRight[c] - r.CellLeft[c]);
            for (int c = 0; c < cols; c++) widths[c] = Math.Max(36, widths[c] + 18);
            double sum = widths.Sum();
            if (sum > contentWidth) for (int c = 0; c < cols; c++) widths[c] *= contentWidth / sum;
            var colW = widths.Select(w => (int)Math.Round(w * 20)).ToArray();
            var sb = new StringBuilder("<w:tbl><w:tblPr>");
            if (rtl) sb.Append("<w:bidiVisual/>");
            sb.Append("<w:tblW w:w=\"" + colW.Sum() + "\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders>");
            foreach (var side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
                sb.Append("<w:" + side + " w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/>");
            sb.Append("</w:tblBorders><w:tblCellMar><w:left w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:tblGrid>");
            for (int c = 0; c < cols; c++) sb.Append("<w:gridCol w:w=\"" + colW[c] + "\"/>");
            sb.Append("</w:tblGrid>");
            foreach (var row in rows)
            {
                sb.Append("<w:tr>");
                for (int c = 0; c < cols; c++)
                {
                    sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"" + colW[c] + "\" w:type=\"dxa\"/>");
                    int shade = row.CellShade != null && c < row.CellShade.Count ? row.CellShade[c] : -1;
                    if (shade >= 0) sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"" + Hex(shade) + "\"/>");
                    sb.Append("</w:tcPr><w:p><w:pPr>" + (row.Rtl ? "<w:bidi/>" : "") + "<w:spacing w:before=\"40\" w:after=\"40\"/></w:pPr>");
                    if (c < row.CellRuns.Count)
                        foreach (var r in row.CellRuns[c])
                        {
                            double room = colW[c] / 20.0 - 10;
                            if (r.Image != null && r.ImageW > room && room > 10) { r.ImageH = r.ImageH * room / r.ImageW; r.ImageW = room; }
                            sb.Append(RunXml(r, bodyFont, bodySize));
                        }
                    sb.Append("</w:p></w:tc>");
                }
                sb.Append("</w:tr>");
            }
            sb.Append("</w:tbl>");
            return sb.ToString();
        }

        static Media AddMedia(List<Media> media, string file)
        {
            var existing = media.FirstOrDefault(m => string.Equals(m.Source, file, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            var m2 = new Media { Id = "rIdImg" + (media.Count + 1), Name = "image" + (media.Count + 1) + Path.GetExtension(file).ToLowerInvariant(), Source = file };
            media.Add(m2);
            return m2;
        }

        static string Graphic(Media m, int id, long cx, long cy)
        {
            return "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>" +
                   "<pic:nvPicPr><pic:cNvPr id=\"" + id + "\" name=\"" + m.Name + "\"/><pic:cNvPicPr/></pic:nvPicPr>" +
                   "<pic:blipFill><a:blip r:embed=\"" + m.Id + "\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
                   "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"" + cx + "\" cy=\"" + cy + "\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>" +
                   "</pic:pic></a:graphicData></a:graphic>";
        }

        static string InlineXml(Media m, int id, long cx, long cy)
        {
            return "<w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"" + cx + "\" cy=\"" + cy + "\"/>" +
                   "<wp:docPr id=\"" + id + "\" name=\"Picture " + id + "\"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
                   Graphic(m, id, cx, cy) + "</wp:inline></w:drawing>";
        }

        /// A picture pinned to the page behind the text (full-page backgrounds).
        static string AnchorXml(Media m, int id, long x, long y, long cx, long cy, string vFrom = "page")
        {
            return "<w:drawing><wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"0\" behindDoc=\"1\" locked=\"1\" layoutInCell=\"1\" allowOverlap=\"1\">" +
                   "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"page\"><wp:posOffset>" + x + "</wp:posOffset></wp:positionH>" +
                   "<wp:positionV relativeFrom=\"" + vFrom + "\"><wp:posOffset>" + y + "</wp:posOffset></wp:positionV>" +
                   "<wp:extent cx=\"" + cx + "\" cy=\"" + cy + "\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapNone/>" +
                   "<wp:docPr id=\"" + id + "\" name=\"Background " + id + "\"/><wp:cNvGraphicFramePr/>" + Graphic(m, id, cx, cy) + "</wp:anchor></w:drawing>";
        }

        static string Styles(string font, double size, bool rtl)
        {
            int hp = (int)Math.Round(size * 2);
            string f = X(font);
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"" + W + "\">");
            sb.Append("<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"" + f + "\" w:hAnsi=\"" + f + "\" w:cs=\"" + f + "\" w:eastAsia=\"" + f + "\"/>" +
                      "<w:sz w:val=\"" + hp + "\"/><w:szCs w:val=\"" + hp + "\"/><w:lang w:val=\"en-US\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>" +
                      "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
            sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
            for (int i = 1; i <= 3; i++)
                sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"Heading" + i + "\"><w:name w:val=\"heading " + i + "\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
                          "<w:pPr><w:keepNext/><w:outlineLvl w:val=\"" + (i - 1) + "\"/></w:pPr></w:style>");
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/></w:style>");
            sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:tblPr><w:tblCellMar><w:left w:w=\"108\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
            sb.Append("</w:styles>");
            return sb.ToString();
        }

        static string Numbering(bool rtl)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"" + W + "\">" +
                   "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"singleLevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/>" +
                   "<w:lvlText w:val=\"•\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/></w:rPr></w:lvl></w:abstractNum>" +
                   "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";
        }

        static void Entry(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var s = e.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        // ---------- small helpers ----------

        static bool IsRtlChar(char c) { return (c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF); }
        static bool IsBlack(int c) { return ((c >> 16) & 255) < 40 && ((c >> 8) & 255) < 40 && (c & 255) < 40; }
        static string Hex(int c) { return (c & 0xFFFFFF).ToString("X6"); }
        static long Emu(double pt) { return (long)Math.Round(Math.Max(1, pt) * 12700); }
        static int Tw(double pt) { return (int)Math.Round(pt * 20); }

        /// XML text: escaped, without characters XML does not allow.
        static string X(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c < 0x20 && c != '\t') continue;
                if (c == 0xFFFE || c == 0xFFFF) continue;
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
