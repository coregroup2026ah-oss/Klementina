using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Klementina
{
    /// Reads the content of a PDF without OCR: qpdf decodes the file to JSON, then each page's content stream is
    /// interpreted to place every glyph (with its font, size and colour), every picture and every filled shape.
    /// Glyph text comes from the font's ToUnicode map; glyphs are rebuilt into lines, paragraphs, headings, lists
    /// and table rows, and Arabic/Hebrew lines are put back into reading order (PDFs store them left to right).
    static class PdfText
    {
        public enum Kind { Para, Heading1, Heading2, Heading3, Bullet, Row, Image, Columns }

        /// A stretch of text with one look.
        public class Run
        {
            public string Text;
            public string Font;
            public double Size;
            public bool Bold, Italic;
            public int Color;                   // 0xRRGGBB
            public string Image;                // a picture inside a table cell
            public double ImageW, ImageH;
            public double Rise;                 // raised (+) or lowered (-) from the line, in points (x², footnote marks)
        }

        public class Block
        {
            public Kind Kind;
            public string Text = "";            // paragraph / heading / bullet text, in reading order
            public List<Run> Runs = new List<Run>();
            public List<string> Cells;          // table row cells, in reading order
            public List<List<Run>> CellRuns;
            public List<int> CellShade;         // background colour per cell, -1 = none
            public List<double> CellLeft, CellRight;   // horizontal extent of each cell's text on the page
            public bool Rtl;
            public bool Bold;
            public string Align = "";           // "", "left", "right", "center", "both"
            public double Size = 11;            // main font size in points
            public int Shade = -1;              // paragraph background colour, -1 = none
            public double SpaceAfter;           // gap to the next block, in points
            public double Top, Bottom;          // position on the page (PDF points, origin bottom-left)
            public List<List<Block>> Columns;  // Kind.Columns: side-by-side columns, in reading order
            public string Image;                // picture file for Kind.Image
            public double ImageW, ImageH;       // picture size in points
            public RectangleF ImageRect;        // where it sits on the page (for cropping a render)
            internal List<Line> Lines = new List<Line>();
        }

        public class Page
        {
            public double Width = 595, Height = 842, X0, Y0;
            public List<Block> Blocks = new List<Block>();
            public int Background = -1;         // page colour, -1 = none
            public string BackgroundImage;      // full-page picture behind the text
            public RectangleF BackgroundRect;
            public double Left = 72, Right = 72, TopMargin = 72;
            public bool Ocr;                    // text came from OCR (scanned page)
            public bool NeedsOcr;               // the page is one picture: ScanImage is it, the caller reads it with OCR
            public string ScanImage;
            public RectangleF ScanRect;
        }

        /// A word read by OCR, already placed on the page (PDF points).
        public class OcrGlyph
        {
            public string Text;
            public double X, Y, W, Size;
            public int Color;
            public bool Bold;
        }

        /// Lays out a picture-only page from OCR'd words, coloured areas and cut-out photos, like a real PDF page.
        public static Page FromOcr(Page scan, List<OcrGlyph> words, List<Tuple<RectangleF, int>> shapes, List<Tuple<RectangleF, string>> pictures)
        {
            var content = new PageContent { Media = new RectangleF((float)scan.X0, (float)scan.Y0, (float)scan.Width, (float)scan.Height), FromOcr = true };
            foreach (var w in words)
                content.Glyphs.Add(new Glyph { Text = w.Text, X = w.X, Y = w.Y, W = w.W, Size = w.Size, Color = w.Color, Bold = w.Bold, Font = "" });
            foreach (var sh in shapes) content.Shapes.Add(new Shape { Rect = sh.Item1, Color = sh.Item2 });
            foreach (var pc in pictures) content.Pictures.Add(new Picture { Rect = pc.Item1, File = pc.Item2 });
            return Layout(content);
        }

        /// One Page per PDF page; a page with no usable text gets null (scanned page: the caller can OCR it).
        /// With imageDir set, the pictures are written there and referenced from Image blocks.
        public static List<Page> Read(string pdf, string imageDir = null)
        {
            if (Engines.Qpdf == null) throw new Exception("qpdf is missing");
            string tmp = Ops.TempDir();
            try
            {
                string json = Path.Combine(tmp, "pdf.json");
                string log;
                int code = Ops.Run(Engines.Qpdf, Ops.Q(pdf) + " --json=2 --json-key=pages --json-key=qpdf --json-stream-data=file --json-stream-prefix=" +
                                   Ops.Q(Path.Combine(tmp, "s")) + " --decode-level=generalized " + Ops.Q(json), out log);
                if ((code != 0 && code != 3) || !File.Exists(json)) throw new Exception("qpdf could not read the PDF");
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
                var root = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(json, Encoding.UTF8));
                var doc = new Doc { Objects = (Dictionary<string, object>)((object[])root["qpdf"])[1], ImageDir = imageDir };
                var result = new List<Page>();
                foreach (Dictionary<string, object> page in (object[])root["pages"])
                {
                    var content = new PageContent();
                    try { content = doc.PageContent((string)page["object"]); }
                    catch { }
                    int letters = content.Glyphs.Sum(g => g.Text.Count(char.IsLetterOrDigit));
                    double area = content.Media.Width * content.Media.Height;
                    bool pictures = content.Pictures.Any(pc => pc.Rect.Width * pc.Rect.Height < area * 0.6);
                    if (letters < 3 && !pictures)
                    {
                        // scanned page (or a design saved as a picture): hand the page picture over for OCR
                        var big = content.Pictures.OrderByDescending(pc => pc.Rect.Width * pc.Rect.Height).FirstOrDefault();
                        string file = big != null ? doc.SaveImage(big.Ref) : null;
                        if (file == null) { result.Add(null); continue; }
                        var m = content.Media;
                        result.Add(new Page { NeedsOcr = true, ScanImage = file, ScanRect = big.Rect, X0 = m.X, Y0 = m.Y, Width = m.Width, Height = m.Height });
                        continue;
                    }
                    foreach (var pc in content.Pictures) pc.File = doc.SaveImage(pc.Ref);
                    result.Add(Layout(content));
                }
                return result;
            }
            finally { Ops.TryDelete(tmp); }
        }

        // ================= exact layout =================

        /// One line of text exactly where the PDF draws it.
        public class ExactLine
        {
            public List<Run> Runs;
            public bool Rtl;
            public double Left, Right, Baseline, Size;
        }

        public class ExactPage
        {
            public double X0, Y0, Width, Height;
            public List<ExactLine> Lines = new List<ExactLine>();
            public bool HasText;
        }

        /// For a copy that looks exactly like the PDF: every text line with its position, fonts and colours, and a
        /// copy of the PDF with all text removed (noTextPdf), whose rendering becomes each page's background —
        /// lines, boxes, logos, fraction bars and pictures stay exactly as they were.
        public static List<ExactPage> ReadExact(string pdf, string noTextPdf)
        {
            if (Engines.Qpdf == null) throw new Exception("qpdf is missing");
            string tmp = Ops.TempDir();
            try
            {
                string json = Path.Combine(tmp, "pdf.json");
                string log;
                int code = Ops.Run(Engines.Qpdf, Ops.Q(pdf) + " --json=2 --json-key=pages --json-key=qpdf --json-stream-data=file --json-stream-prefix=" +
                                   Ops.Q(Path.Combine(tmp, "s")) + " --decode-level=generalized " + Ops.Q(json), out log);
                if ((code != 0 && code != 3) || !File.Exists(json)) throw new Exception("qpdf could not read the PDF");
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
                var root = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(json, Encoding.UTF8));
                var parts = (object[])root["qpdf"];
                var doc = new Doc { Objects = (Dictionary<string, object>)parts[1] };
                var result = new List<ExactPage>();
                var streams = new HashSet<string>();
                foreach (Dictionary<string, object> page in (object[])root["pages"])
                {
                    var ep = new ExactPage();
                    try
                    {
                        var content = doc.PageContent((string)page["object"]);
                        var m = content.Media;
                        ep.X0 = m.X; ep.Y0 = m.Y; ep.Width = m.Width; ep.Height = m.Height;
                        ep.Lines = ExactLines(content.Glyphs);
                        ep.HasText = content.Glyphs.Sum(g => g.Text.Count(char.IsLetterOrDigit)) >= 3;
                        foreach (var c in (object[])page["contents"]) streams.Add((string)c);
                        CollectForms(doc, doc.Dict((string)page["object"]), streams, 0);
                    }
                    catch { }
                    result.Add(ep);
                }

                // the same PDF with every text object (BT … ET) cut out of the page and form content streams
                var updates = new Dictionary<string, object>();
                int n = 0;
                foreach (var sref in streams)
                {
                    var st = doc.Resolve(sref) as Dictionary<string, object>;
                    var dict = st != null ? st["dict"] as Dictionary<string, object> : null;
                    if (dict == null) continue;
                    var data = doc.StreamData(sref);
                    var stripped = StripText(data);
                    if (stripped.Length == data.Length) continue;
                    string f = Path.Combine(tmp, "notext" + (++n));
                    File.WriteAllBytes(f, stripped);
                    var nd = new Dictionary<string, object>(dict);
                    nd.Remove("/Filter"); nd.Remove("/DecodeParms"); nd.Remove("/Length");
                    updates["obj:" + sref] = new Dictionary<string, object> { { "stream", new Dictionary<string, object> { { "dict", nd }, { "datafile", f } } } };
                }
                if (updates.Count == 0) File.Copy(pdf, noTextPdf, true);
                else
                {
                    string mod = Path.Combine(tmp, "update.json");
                    File.WriteAllText(mod, ser.Serialize(new Dictionary<string, object> { { "qpdf", new object[] { parts[0], updates } } }), new UTF8Encoding(false));
                    code = Ops.Run(Engines.Qpdf, Ops.Q(pdf) + " " + Ops.Q(noTextPdf) + " --update-from-json=" + Ops.Q(mod), out log);
                    if ((code != 0 && code != 3) || !File.Exists(noTextPdf)) throw new Exception("qpdf could not write the background copy");
                }
                return result;
            }
            finally { Ops.TryDelete(tmp); }
        }

        /// Form XObjects used by a page or another form (headers, logos and stamps are often forms with text inside).
        static void CollectForms(Doc doc, Dictionary<string, object> owner, HashSet<string> streams, int depth)
        {
            if (owner == null || depth > 8) return;
            object resObj = Doc.Get(owner, "/Resources");
            for (var n = owner; resObj == null && n != null; n = doc.Dict(Doc.Get(n, "/Parent"))) resObj = Doc.Get(n, "/Resources");
            var xobjs = doc.Dict(Doc.Get(doc.Dict(resObj), "/XObject"));
            if (xobjs == null) return;
            foreach (var kv in xobjs)
            {
                var xref = kv.Value as string;
                if (xref == null || streams.Contains(xref)) continue;
                var xd = doc.Dict(xref);
                if (Doc.Get(xd, "/Subtype") as string != "/Form") continue;
                streams.Add(xref);
                CollectForms(doc, xd, streams, depth + 1);
            }
        }

        /// A content stream without its text objects. Strings, dictionaries, comments and inline images are skipped
        /// properly, so a "BT" inside a string or an image never confuses it.
        public static byte[] StripText(byte[] s)
        {
            var output = new List<byte>(s.Length);
            int p = 0, copyFrom = 0, btStart = -1;
            Func<byte, bool> ws = b => b == 32 || b == 10 || b == 13 || b == 9 || b == 12 || b == 0;
            Func<byte, bool> delim = b => b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%';
            while (p < s.Length)
            {
                byte c = s[p];
                if (ws(c)) { p++; continue; }
                if (c == '%') { while (p < s.Length && s[p] != 10 && s[p] != 13) p++; continue; }
                if (c == '(')
                {
                    int depth = 0;
                    for (; p < s.Length; p++)
                    {
                        if (s[p] == '\\') { p++; continue; }
                        if (s[p] == '(') depth++;
                        else if (s[p] == ')' && --depth == 0) { p++; break; }
                    }
                    continue;
                }
                if (c == '<') { while (p < s.Length && s[p] != '>') p++; p++; continue; }   // hex string, or "<<" (keys are names)
                if (c == '>' || c == '[' || c == ']' || c == '{' || c == '}') { p++; continue; }
                if (c == '/') { p++; while (p < s.Length && !ws(s[p]) && !delim(s[p])) p++; continue; }
                int start = p;
                while (p < s.Length && !ws(s[p]) && !delim(s[p])) p++;
                if (p == start) { p++; continue; }
                int len = p - start;
                if (len == 2 && s[start] == 'B' && s[start + 1] == 'T' && btStart < 0) btStart = start;
                else if (len == 2 && s[start] == 'E' && s[start + 1] == 'T' && btStart >= 0)
                {
                    for (int i = copyFrom; i < btStart; i++) output.Add(s[i]);
                    output.Add((byte)'\n');
                    copyFrom = p;
                    btStart = -1;
                }
                else if (len == 2 && s[start] == 'B' && s[start + 1] == 'I')
                {
                    // inline image: jump past "ID" … "EI"
                    while (p + 1 < s.Length && !(s[p] == 'I' && s[p + 1] == 'D' && ws(s[p - 1]))) p++;
                    p += 2;
                    while (p + 2 < s.Length && !(ws(s[p]) && s[p + 1] == 'E' && s[p + 2] == 'I' && (p + 3 >= s.Length || ws(s[p + 3])))) p++;
                    p += 3;
                }
            }
            if (copyFrom == 0) return s;
            for (int i = copyFrom; i < s.Length; i++) output.Add(s[i]);
            return output.ToArray();
        }

        /// Lines of a page for the exact layout: grouped by baseline, in reading order, with raised/lowered bits
        /// (superscripts) kept as such.
        static List<ExactLine> ExactLines(List<Glyph> all)
        {
            var glyphs = all.Where(g => g.Text.Length > 0 && !g.Invisible).ToList();
            foreach (var g in glyphs) g.Text = Plain(g.Text.Replace((char)0xA0, ' '));
            var lines = GroupLines(glyphs);
            // a small raised or lowered line (x², log₂, footnote marks) that sits within a bigger line belongs to it
            for (int i = 0; i < lines.Count; i++)
            {
                var small = lines[i];
                var host = lines.Where(h => h != small && h.Size > small.Size * 1.15 && small.Y > h.Y - h.Size * 0.35 && small.Y < h.Y + h.Size * 0.75
                                            && small.Left < h.Right + h.Size && small.Right > h.Left - h.Size)
                                .OrderBy(h => Math.Abs(h.Y - small.Y)).FirstOrDefault();
                if (host == null) continue;
                host.Glyphs.AddRange(small.Glyphs);
                host.Glyphs = host.Glyphs.OrderBy(g => g.X).ToList();
                host.Left = Math.Min(host.Left, small.Left); host.Right = Math.Max(host.Right, small.Right);
                lines.RemoveAt(i); i = -1;
            }
            int pageR = 0, pageL = 0;
            foreach (var g in glyphs) foreach (char c in g.Text) if (char.IsLetter(c)) { if (IsRtlChar(c)) pageR++; else pageL++; }
            bool pageRtl = pageR > pageL;
            var result = new List<ExactLine>();
            // items that merely share a height (a title on the right, a date on the left, a centred heading) are
            // separate: split each line at its wide gaps so every piece keeps its own position
            var pieces = new List<Line>();
            foreach (var l in lines)
            {
                var solid = l.Glyphs.Where(g => g.Text.Trim().Length > 0).OrderBy(g => g.X).ToList();
                var cur = new Line { Y = l.Y, Size = l.Size };
                Glyph last = null;
                foreach (var g in solid)
                {
                    if (last != null && g.X - (last.X + last.W) > Math.Max(l.Size * 1.5, 8)) { pieces.Add(cur); cur = new Line { Y = l.Y, Size = l.Size }; }
                    cur.Glyphs.Add(g);
                    last = g;
                }
                pieces.Add(cur);
            }
            foreach (var pc in pieces)
            {
                pc.Left = pc.Glyphs.Min(g => g.X); pc.Right = pc.Glyphs.Max(g => g.X + g.W);
                pc.Size = pc.Glyphs.Where(g => !g.Mark).Select(g => g.Size).DefaultIfEmpty(pc.Size).Max();
                // spaces between the kept glyphs come back from the gaps in Logical
            }
            foreach (var l in pieces)
            {
                if (!l.Glyphs.Any(g => g.Text.Trim().Length > 0)) continue;
                int r = 0, lt = 0;
                foreach (var g in l.Glyphs) foreach (char c in g.Text) if (char.IsLetter(c)) { if (IsRtlChar(c)) r++; else lt++; }
                bool rtl = r > 0 && (pageRtl || r >= lt);
                // the baseline most of the line's characters sit on
                double baseline = l.Glyphs.Where(g => g.Text.Trim().Length > 0).GroupBy(g => Math.Round(g.Y * 2) / 2)
                                   .OrderByDescending(gr => gr.Sum(x => x.Text.Length)).First().Key;
                var runs = Logical(l.Glyphs, rtl, baseline);
                TrimRuns(runs);
                if (runs.Count == 0) continue;
                result.Add(new ExactLine { Runs = runs, Rtl = rtl, Left = l.Left, Right = l.Right, Baseline = baseline, Size = l.Size });
            }
            return result;
        }

        // ================= PDF objects =================

        internal class Glyph
        {
            public string Text;
            public double X, Y, W, Size;
            public bool Bold, Italic, Invisible;
            public string Font;
            public int Color;
            public bool Mark { get { return Text.Length > 0 && Text.All(c => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark); } }
        }

        class Shape { public RectangleF Rect; public int Color; }

        class Picture { public string Ref; public RectangleF Rect; public string File; }

        class PageContent
        {
            public List<Glyph> Glyphs = new List<Glyph>();
            public List<Shape> Shapes = new List<Shape>();
            public List<Picture> Pictures = new List<Picture>();
            public RectangleF Media = new RectangleF(0, 0, 595, 842);
            public bool FromOcr;            // words came from OCR of a page picture (gaps can be dropped symbols)
            public bool NoColumns;          // already one column of a two-column page
        }

        class Font
        {
            public bool TwoByte;
            public Dictionary<int, string> Unicode = new Dictionary<int, string>();
            public Dictionary<int, double> Widths = new Dictionary<int, double>();
            public double DefaultWidth = 1000;
            public bool Bold, Italic;
            public string Family = "";
            public Encoding Fallback;   // simple fonts without ToUnicode
        }

        class Doc
        {
            public Dictionary<string, object> Objects;
            public string ImageDir;
            readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
            readonly Dictionary<string, string> savedImages = new Dictionary<string, string>();
            int imageCount;

            public object Resolve(object v)
            {
                for (int i = 0; i < 32; i++)
                {
                    var s = v as string;
                    if (s == null || !s.EndsWith(" R", StringComparison.Ordinal)) return v;
                    object o;
                    if (!Objects.TryGetValue("obj:" + s, out o)) return null;
                    var d = o as Dictionary<string, object>;
                    if (d == null) return null;
                    object val;
                    if (d.TryGetValue("value", out val)) { v = val; continue; }
                    object st;
                    if (d.TryGetValue("stream", out st)) return st;   // stream: {"dict":..., "datafile":...}
                    return null;
                }
                return null;
            }

            public Dictionary<string, object> Dict(object v)
            {
                var r = Resolve(v) as Dictionary<string, object>;
                if (r != null && r.ContainsKey("dict") && r["dict"] is Dictionary<string, object> && !r.ContainsKey("/Type")) return (Dictionary<string, object>)r["dict"];
                return r;
            }

            public byte[] StreamData(object v)
            {
                var st = Resolve(v) as Dictionary<string, object>;
                object f;
                if (st == null || !st.TryGetValue("datafile", out f) || !File.Exists((string)f)) return new byte[0];
                return File.ReadAllBytes((string)f);
            }

            public static object Get(Dictionary<string, object> d, string k) { object v; return d != null && k != null && d.TryGetValue(k, out v) ? v : null; }

            public PageContent PageContent(string pageRef)
            {
                var page = Dict(pageRef);
                // Resources and MediaBox may be inherited from a parent Pages node
                object res = null, media = null;
                for (var n = page; n != null && (res == null || media == null); n = Dict(Get(n, "/Parent")))
                {
                    if (res == null) res = Get(n, "/Resources");
                    if (media == null) media = Get(n, "/MediaBox");
                }
                var content = new PageContent();
                var mb = Resolve(media) as object[];
                if (mb != null && mb.Length == 4)
                {
                    double x0 = Num(Resolve(mb[0])), y0 = Num(Resolve(mb[1])), x1 = Num(Resolve(mb[2])), y1 = Num(Resolve(mb[3]));
                    content.Media = new RectangleF((float)Math.Min(x0, x1), (float)Math.Min(y0, y1), (float)Math.Abs(x1 - x0), (float)Math.Abs(y1 - y0));
                }
                var contents = Resolve(Get(page, "/Contents"));
                var data = new List<byte>();
                if (contents is object[]) foreach (var c in (object[])contents) { data.AddRange(StreamData(c)); data.Add((byte)'\n'); }
                else if (Get(page, "/Contents") != null) data.AddRange(StreamData(Get(page, "/Contents")));
                new Interpreter(this, content).Run(data.ToArray(), Dict(res), Matrix.Identity, 0);
                return content;
            }

            public static double Num(object v) { try { return v == null ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return 0; } }

            public Font GetFont(object fontRef)
            {
                string key = fontRef as string ?? Guid.NewGuid().ToString();
                Font f;
                if (fonts.TryGetValue(key, out f)) return f;
                f = new Font();
                var d = Dict(fontRef);
                string sub = Get(d, "/Subtype") as string;
                string baseFont = (Get(d, "/BaseFont") as string ?? "");
                var desc0 = Dict(Get(d, "/FontDescriptor"));
                f.Bold = HasWord(baseFont, "Bold") || HasWord(baseFont, "Black") || HasWord(baseFont, "Heavy") || HasWord(baseFont, "Semibold") || HasWord(baseFont, "Demi");
                f.Italic = HasWord(baseFont, "Italic") || HasWord(baseFont, "Oblique");
                f.Family = FamilyName(baseFont);
                if (sub == "/Type0")
                {
                    f.TwoByte = true;
                    var desc = Resolve(Get(d, "/DescendantFonts")) as object[];
                    var cid = desc != null && desc.Length > 0 ? Dict(desc[0]) : null;
                    if (desc0 == null) desc0 = Dict(Get(cid, "/FontDescriptor"));
                    var dw = Resolve(Get(cid, "/DW"));
                    if (dw != null) f.DefaultWidth = Num(dw);
                    var w = Resolve(Get(cid, "/W")) as object[];
                    if (w != null)
                    {
                        for (int i = 0; i + 1 < w.Length; )
                        {
                            int c1 = (int)Num(Resolve(w[i]));
                            var next = Resolve(w[i + 1]);
                            if (next is object[])
                            {
                                var arr = (object[])next;
                                for (int k = 0; k < arr.Length; k++) f.Widths[c1 + k] = Num(Resolve(arr[k]));
                                i += 2;
                            }
                            else if (i + 2 < w.Length)
                            {
                                int c2 = (int)Num(next);
                                double wd = Num(Resolve(w[i + 2]));
                                for (int c = c1; c <= c2 && c - c1 < 65536; c++) f.Widths[c] = wd;
                                i += 3;
                            }
                            else break;
                        }
                    }
                }
                else
                {
                    var first = Resolve(Get(d, "/FirstChar"));
                    var widths = Resolve(Get(d, "/Widths")) as object[];
                    if (first != null && widths != null)
                    {
                        int fc = (int)Num(first);
                        for (int i = 0; i < widths.Length; i++) f.Widths[fc + i] = Num(Resolve(widths[i]));
                    }
                    f.DefaultWidth = 500;
                    f.Fallback = Encoding.GetEncoding(1252);
                    var enc = Dict(Get(d, "/Encoding"));
                    var diffs = enc != null ? Resolve(Get(enc, "/Differences")) as object[] : null;
                    if (diffs != null)
                    {
                        int code = 0;
                        foreach (var item in diffs)
                        {
                            var it = Resolve(item);
                            if (it is string && ((string)it).StartsWith("/", StringComparison.Ordinal))
                            {
                                string u = GlyphName(((string)it).Substring(1));
                                if (u != null) f.Unicode[code] = u;
                                code++;
                            }
                            else code = (int)Num(it);
                        }
                    }
                }
                // the descriptor's flags and weight also tell bold/italic when the name does not
                if (desc0 != null)
                {
                    int flags = (int)Num(Resolve(Get(desc0, "/Flags")));
                    if ((flags & 64) != 0) f.Italic = true;
                    if ((flags & (1 << 18)) != 0) f.Bold = true;
                    if (Num(Resolve(Get(desc0, "/FontWeight"))) >= 600) f.Bold = true;
                    if (Num(Resolve(Get(desc0, "/ItalicAngle"))) < -5) f.Italic = true;
                }
                var tu = Get(d, "/ToUnicode");
                if (tu != null) ParseCMap(Encoding.GetEncoding(28591).GetString(StreamData(tu)), f.Unicode);
                FixLamAlef(f);
                fonts[key] = f;
                return f;
            }

            static bool HasWord(string s, string w) { return s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0; }

            /// Word sometimes maps one form of the lam-alef ligature to a lone lam, which drops the alef ("الاول" → "الول").
            static void FixLamAlef(Font f)
            {
                char lam = (char)0x0644;
                Func<int, double> width = c => { double w; return f.Widths.TryGetValue(c, out w) ? w : f.DefaultWidth; };
                // the ligature's forms sit next to each other in the font: a lone-lam glyph right beside a glyph mapped
                // to "لا" (and about as wide) is the other form of that ligature
                var la = f.Unicode.Where(kv => kv.Value.Length == 2 && kv.Value[0] == lam && (kv.Value[1] == (char)0x0627 || kv.Value[1] == (char)0x0623 ||
                                                kv.Value[1] == (char)0x0625 || kv.Value[1] == (char)0x0622)).ToList();
                if (la.Count == 0) return;
                foreach (var kv in la)
                    foreach (int c in new[] { kv.Key - 1, kv.Key + 1 })
                    {
                        string t;
                        if (f.Unicode.TryGetValue(c, out t) && t.Length == 1 && t[0] == lam && width(c) >= width(kv.Key) * 0.7)
                            f.Unicode[c] = kv.Value;
                    }
            }

            /// "/ABCDEF+TimesNewRomanPS-BoldMT" → "Times New Roman".
            static string FamilyName(string baseFont)
            {
                string n = baseFont.TrimStart('/');
                int plus = n.IndexOf('+');
                if (plus == 6) n = n.Substring(7);
                int cut = n.IndexOfAny(new[] { '-', ',' });
                if (cut > 0) n = n.Substring(0, cut);
                foreach (var tail in new[] { "PSMT", "PS", "MT", "Bold", "Italic", "Regular" })
                    if (n.Length > tail.Length + 2 && n.EndsWith(tail)) n = n.Substring(0, n.Length - tail.Length);
                // CamelCase → words, keeping runs of capitals ("SegoeUI" → "Segoe UI")
                var sb = new StringBuilder();
                for (int i = 0; i < n.Length; i++)
                {
                    char c = n[i];
                    if (i > 0 && char.IsUpper(c) && (char.IsLower(n[i - 1]) || (i + 1 < n.Length && char.IsLower(n[i + 1]) && char.IsUpper(n[i - 1])))) sb.Append(' ');
                    sb.Append(c);
                }
                string family = sb.ToString().Trim();
                return family.Length >= 2 ? family : "";
            }

            /// Writes an image XObject as PNG/JPEG into ImageDir; null when it can't be decoded here.
            public string SaveImage(string xref)
            {
                if (ImageDir == null || xref == null) return null;
                string done;
                if (savedImages.TryGetValue(xref, out done)) return done;
                string file = null;
                try { file = DecodeImage(xref); } catch { file = null; }
                savedImages[xref] = file;
                return file;
            }

            string DecodeImage(string xref)
            {
                var st = Resolve(xref) as Dictionary<string, object>;
                var d = Dict(xref);
                object df;
                if (st == null || d == null || !st.TryGetValue("datafile", out df) || !File.Exists((string)df)) return null;
                if (Get(d, "/ImageMask") is bool && (bool)Get(d, "/ImageMask")) return null;
                var filters = new List<string>();
                var fv = Resolve(Get(d, "/Filter"));
                if (fv is string) filters.Add((string)fv);
                else if (fv is object[]) foreach (var x in (object[])fv) filters.Add(Resolve(x) as string ?? "");
                byte[] data = File.ReadAllBytes((string)df);
                Directory.CreateDirectory(ImageDir);
                string name = Path.Combine(ImageDir, "image" + (++imageCount));
                if (filters.Contains("/DCTDecode"))
                {
                    // JPEG data passes through untouched; CMYK JPEGs are re-encoded since Word shows them badly
                    var cs = Resolve(Get(d, "/ColorSpace"));
                    if (Components(cs) == 4) return ReencodeJpeg(data, name);
                    File.WriteAllBytes(name + ".jpg", data);
                    return name + ".jpg";
                }
                if (filters.Any(f => f == "/JPXDecode" || f == "/JBIG2Decode" || f == "/CCITTFaxDecode")) return null;

                int w = (int)Num(Resolve(Get(d, "/Width"))), h = (int)Num(Resolve(Get(d, "/Height")));
                int bpc = (int)Num(Resolve(Get(d, "/BitsPerComponent")));
                if (bpc == 0) bpc = 8;
                if (w <= 0 || h <= 0 || (long)w * h > 40000000L) return null;
                var colorSpace = Resolve(Get(d, "/ColorSpace"));
                byte[] palette = null;
                int comps = Components(colorSpace);
                if (colorSpace is object[] && ((object[])colorSpace).Length >= 4 && Resolve(((object[])colorSpace)[0]) as string == "/Indexed")
                {
                    var arr = (object[])colorSpace;
                    int baseComps = Components(Resolve(arr[1]));
                    if (baseComps != 1 && baseComps != 3 && baseComps != 4) return null;
                    palette = LookupBytes(Resolve(arr[3]));
                    if (palette == null) return null;
                    comps = -baseComps;   // negative = indexed into a palette of |comps| components
                }
                if (comps == 0) return null;
                var decode = Resolve(Get(d, "/Decode")) as object[];
                bool invert = decode != null && decode.Length >= 2 && Num(Resolve(decode[0])) == 1 && Num(Resolve(decode[1])) == 0;

                byte[] alpha = null;
                var smask = Get(d, "/SMask") as string;
                if (smask != null) alpha = GrayPlane(smask, w, h);

                int samples = comps < 0 ? 1 : comps;
                int rowBytes = (w * samples * bpc + 7) / 8;
                if (data.Length < rowBytes * h) return null;
                var argb = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int r, g, b;
                        if (comps < 0)
                        {
                            int idx = Sample(data, y * rowBytes, x, bpc);
                            int pc = -comps;
                            int o = idx * pc;
                            if (o + pc > palette.Length) { r = g = b = 0; }
                            else if (pc == 1) r = g = b = palette[o];
                            else if (pc == 3) { r = palette[o]; g = palette[o + 1]; b = palette[o + 2]; }
                            else Cmyk(palette[o], palette[o + 1], palette[o + 2], palette[o + 3], out r, out g, out b);
                        }
                        else if (comps == 1)
                        {
                            int v = Sample(data, y * rowBytes, x, bpc) * 255 / ((1 << bpc) - 1);
                            if (invert) v = 255 - v;
                            r = g = b = v;
                        }
                        else
                        {
                            if (bpc != 8) return null;
                            int o = y * rowBytes + x * comps;
                            if (comps == 3) { r = data[o]; g = data[o + 1]; b = data[o + 2]; }
                            else Cmyk(data[o], data[o + 1], data[o + 2], data[o + 3], out r, out g, out b);
                        }
                        int p = (y * w + x) * 4;
                        argb[p] = (byte)b; argb[p + 1] = (byte)g; argb[p + 2] = (byte)r;
                        argb[p + 3] = alpha != null ? alpha[y * w + x] : (byte)255;
                    }
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(argb, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
                    bmp.UnlockBits(bd);
                    if (alpha == null && w * h > 250000)
                    {
                        // a large opaque picture is most likely a photo: JPEG keeps the Word file small
                        SaveJpeg(bmp, name + ".jpg", 90);
                        return name + ".jpg";
                    }
                    bmp.Save(name + ".png", ImageFormat.Png);
                }
                return name + ".png";
            }

            /// Soft mask (alpha) of an image, scaled to w×h; null if it can't be read.
            byte[] GrayPlane(string xref, int w, int h)
            {
                var d = Dict(xref);
                if (d == null) return null;
                int mw = (int)Num(Resolve(Get(d, "/Width"))), mh = (int)Num(Resolve(Get(d, "/Height")));
                int bpc = (int)Num(Resolve(Get(d, "/BitsPerComponent")));
                var fv = Resolve(Get(d, "/Filter"));
                if (fv != null && !(fv is object[] && ((object[])fv).Length == 0) && !(fv as string == "/FlateDecode") && !(fv is object[] && ((object[])fv).All(x => Resolve(x) as string == "/FlateDecode"))) return null;
                var data = StreamData(xref);
                if (mw <= 0 || mh <= 0 || bpc <= 0) return null;
                int rowBytes = (mw * bpc + 7) / 8;
                if (data.Length < rowBytes * mh) return null;
                var a = new byte[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int sx = x * mw / w, sy = y * mh / h;
                        a[y * w + x] = (byte)(Sample(data, sy * rowBytes, sx, bpc) * 255 / ((1 << bpc) - 1));
                    }
                return a;
            }

            byte[] LookupBytes(object v)
            {
                var s = v as string;
                if (s != null && s.StartsWith("b:", StringComparison.Ordinal))
                {
                    string hex = s.Substring(2);
                    var b = new byte[hex.Length / 2];
                    for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    return b;
                }
                if (s != null && s.StartsWith("u:", StringComparison.Ordinal)) return Encoding.GetEncoding(28591).GetBytes(s.Substring(2));
                if (v is Dictionary<string, object>) return StreamData(v);
                return null;
            }

            /// Colour components of a colour space: 1, 3 or 4; 0 when unsupported. Indexed is handled by the caller.
            public int Components(object cs)
            {
                cs = Resolve(cs);
                var name = cs as string;
                if (name == "/DeviceGray" || name == "/CalGray" || name == "/G") return 1;
                if (name == "/DeviceRGB" || name == "/CalRGB" || name == "/RGB" || name == "/Lab") return 3;
                if (name == "/DeviceCMYK" || name == "/CMYK") return 4;
                var arr = cs as object[];
                if (arr != null && arr.Length > 0)
                {
                    string kind = Resolve(arr[0]) as string;
                    if (kind == "/ICCBased" && arr.Length > 1)
                    {
                        var sd = Dict(arr[1]);
                        int n = (int)Num(Resolve(Get(sd, "/N")));
                        return n == 1 || n == 3 || n == 4 ? n : 0;
                    }
                    if (kind == "/CalRGB" || kind == "/Lab") return 3;
                    if (kind == "/CalGray") return 1;
                    if (kind == "/Indexed") return 1;
                }
                return 0;
            }

            static int Sample(byte[] data, int rowStart, int x, int bpc)
            {
                if (bpc == 8) return data[rowStart + x];
                if (bpc == 16) return data[rowStart + x * 2] * 255 / 255;
                int bit = x * bpc;
                int by = data[rowStart + bit / 8];
                int shift = 8 - bpc - (bit % 8);
                return (by >> shift) & ((1 << bpc) - 1);
            }

            static void Cmyk(int c, int m, int y, int k, out int r, out int g, out int b)
            {
                r = 255 - Math.Min(255, c + k);
                g = 255 - Math.Min(255, m + k);
                b = 255 - Math.Min(255, y + k);
            }

            static string ReencodeJpeg(byte[] data, string name)
            {
                try
                {
                    using (var ms = new MemoryStream(data))
                    using (var img = Image.FromStream(ms))
                    using (var bmp = new Bitmap(img))
                    {
                        SaveJpeg(bmp, name + ".jpg", 90);
                        return name + ".jpg";
                    }
                }
                catch { return null; }
            }

            static void SaveJpeg(Bitmap bmp, string path, long quality)
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    bmp.Save(path, codec, p);
                }
            }
        }

        static string GlyphName(string n)
        {
            if (n.Length == 1) return n;
            if (n.StartsWith("uni", StringComparison.Ordinal) && n.Length >= 7)
            {
                int cp;
                if (int.TryParse(n.Substring(3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp)) return ((char)cp).ToString();
            }
            switch (n)
            {
                case "space": return " ";
                case "period": return ".";
                case "comma": return ",";
                case "hyphen": return "-";
                case "colon": return ":";
                case "semicolon": return ";";
                case "parenleft": return "(";
                case "parenright": return ")";
                case "quotesingle": return "'";
                case "quotedbl": return "\"";
                case "bullet": return "•";
                case "zero": return "0"; case "one": return "1"; case "two": return "2"; case "three": return "3"; case "four": return "4";
                case "five": return "5"; case "six": return "6"; case "seven": return "7"; case "eight": return "8"; case "nine": return "9";
            }
            return null;
        }

        static void ParseCMap(string s, Dictionary<int, string> map)
        {
            var tok = new List<string>();
            for (int i = 0; i < s.Length; )
            {
                char c = s[i];
                if (c == '<')
                {
                    int j = s.IndexOf('>', i + 1);
                    if (j < 0) break;
                    tok.Add(s.Substring(i, j - i + 1)); i = j + 1;
                }
                else if (c == '[' || c == ']') { tok.Add(c.ToString()); i++; }
                else if (char.IsWhiteSpace(c)) i++;
                else if (c == '%') { while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++; }
                else
                {
                    int j = i;
                    while (j < s.Length && !char.IsWhiteSpace(s[j]) && s[j] != '<' && s[j] != '[' && s[j] != ']') j++;
                    tok.Add(s.Substring(i, j - i)); i = j;
                }
            }
            for (int i = 0; i < tok.Count; i++)
            {
                if (tok[i] == "beginbfchar")
                {
                    for (i++; i + 1 < tok.Count && tok[i] != "endbfchar"; i += 2)
                        map[HexInt(tok[i])] = HexText(tok[i + 1]);
                }
                else if (tok[i] == "beginbfrange")
                {
                    for (i++; i + 2 < tok.Count && tok[i] != "endbfrange"; )
                    {
                        int lo = HexInt(tok[i]), hi = HexInt(tok[i + 1]);
                        if (tok[i + 2] == "[")
                        {
                            int k = i + 3, c = lo;
                            for (; k < tok.Count && tok[k] != "]"; k++, c++) map[c] = HexText(tok[k]);
                            i = k + 1;
                        }
                        else
                        {
                            string dst = HexText(tok[i + 2]);
                            for (int c = lo; c <= hi && c - lo < 65536; c++)
                            {
                                if (dst.Length == 0) break;
                                char last = (char)(dst[dst.Length - 1] + (c - lo));
                                map[c] = dst.Substring(0, dst.Length - 1) + last;
                            }
                            i += 3;
                        }
                    }
                }
            }
        }

        static int HexInt(string t)
        {
            string h = t.Trim('<', '>');
            int v;
            return int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) ? v : -1;
        }

        static string HexText(string t)
        {
            string h = t.Trim('<', '>');
            if (h.Length % 4 != 0) h = h.PadRight((h.Length + 3) / 4 * 4, '0');
            var sb = new StringBuilder();
            for (int i = 0; i + 4 <= h.Length; i += 4)
            {
                int v;
                if (int.TryParse(h.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) sb.Append((char)v);
            }
            return sb.ToString();
        }

        // ================= content stream =================

        struct Matrix
        {
            public double A, B, C, D, E, F;
            public static readonly Matrix Identity = new Matrix { A = 1, D = 1 };
            public static Matrix Of(double a, double b, double c, double d, double e, double f) { return new Matrix { A = a, B = b, C = c, D = d, E = e, F = f }; }
            public static Matrix operator *(Matrix m, Matrix n)
            {
                return Of(m.A * n.A + m.B * n.C, m.A * n.B + m.B * n.D, m.C * n.A + m.D * n.C, m.C * n.B + m.D * n.D,
                          m.E * n.A + m.F * n.C + n.E, m.E * n.B + m.F * n.D + n.F);
            }
            public PointF Apply(double x, double y) { return new PointF((float)(x * A + y * C + E), (float)(x * B + y * D + F)); }
        }

        class GState
        {
            public Matrix Ctm;
            public object FontRef;
            public double Size = 12, Tc, Tw, Th = 1, TL, Rise;
            public int Fill;               // non-stroking colour, 0xRRGGBB
            public int FillComps = 1;      // components of the current fill colour space (0 = unsupported)
            public int Render;             // text rendering mode (3 = invisible)
        }

        class Interpreter
        {
            readonly Doc doc;
            readonly PageContent output;
            public Interpreter(Doc d, PageContent o) { doc = d; output = o; }

            byte[] s;
            int p;

            // current path: bounding box, and whether it is made only of straight axis-aligned edges
            float pathX0, pathY0, pathX1, pathY1;
            bool pathEmpty = true, pathRect = true;
            PointF pathLast;

            void PathPoint(PointF pt)
            {
                if (pathEmpty) { pathX0 = pathX1 = pt.X; pathY0 = pathY1 = pt.Y; pathEmpty = false; }
                else { pathX0 = Math.Min(pathX0, pt.X); pathX1 = Math.Max(pathX1, pt.X); pathY0 = Math.Min(pathY0, pt.Y); pathY1 = Math.Max(pathY1, pt.Y); }
            }

            void ClearPath() { pathEmpty = true; pathRect = true; }

            public void Run(byte[] data, Dictionary<string, object> res, Matrix ctm, int depth)
            {
                if (depth > 8) return;
                var saved = s; int savedP = p;
                s = data; p = 0;
                var gs = new GState { Ctm = ctm };
                var stack = new Stack<GState>();
                Matrix tm = Matrix.Identity, tlm = Matrix.Identity;
                var ops = new List<object>();
                var actual = new Stack<string>();     // /ActualText of enclosing marked-content spans
                int actualDepthUsed = -1;
                while (true)
                {
                    object t = Next();
                    if (t == null) break;
                    var op = t as Op;
                    if (op == null) { ops.Add(t); continue; }
                    try
                    {
                        switch (op.Name)
                        {
                            case "q": stack.Push(Clone(gs)); break;
                            case "Q": if (stack.Count > 0) gs = stack.Pop(); break;
                            case "cm": gs.Ctm = M(ops) * gs.Ctm; break;
                            // ----- colour (fill only; text and shapes are drawn with it) -----
                            case "g": gs.Fill = Gray(Num(Last(ops))); gs.FillComps = 1; break;
                            case "rg": if (ops.Count >= 3) { gs.Fill = Rgb(Num(ops[ops.Count - 3]), Num(ops[ops.Count - 2]), Num(ops[ops.Count - 1])); gs.FillComps = 3; } break;
                            case "k": if (ops.Count >= 4) { gs.Fill = CmykColor(Num(ops[ops.Count - 4]), Num(ops[ops.Count - 3]), Num(ops[ops.Count - 2]), Num(ops[ops.Count - 1])); gs.FillComps = 4; } break;
                            case "cs":
                            {
                                string name = Last(ops) as string;
                                object csv = name;
                                var spaces = doc.Dict(Doc.Get(res, "/ColorSpace"));
                                if (spaces != null && Doc.Get(spaces, name) != null) csv = Doc.Get(spaces, name);
                                gs.FillComps = doc.Components(csv);
                                var arr = doc.Resolve(csv) as object[];
                                if (arr != null && arr.Length > 0 && doc.Resolve(arr[0]) as string == "/Indexed") gs.FillComps = 0;
                                gs.Fill = 0;
                                break;
                            }
                            case "sc":
                            case "scn":
                            {
                                var nums = ops.Where(o => o is double).Select(o => (double)o).ToList();
                                if (gs.FillComps == 1 && nums.Count >= 1) gs.Fill = Gray(nums[nums.Count - 1]);
                                else if (gs.FillComps == 3 && nums.Count >= 3) gs.Fill = Rgb(nums[0], nums[1], nums[2]);
                                else if (gs.FillComps == 4 && nums.Count >= 4) gs.Fill = CmykColor(nums[0], nums[1], nums[2], nums[3]);
                                break;
                            }
                            // ----- paths: only filled rectangles matter (coloured bars, cell shading, page colour) -----
                            case "re":
                                if (ops.Count >= 4)
                                {
                                    double x = Num(ops[ops.Count - 4]), y = Num(ops[ops.Count - 3]), w = Num(ops[ops.Count - 2]), h = Num(ops[ops.Count - 1]);
                                    var a = gs.Ctm.Apply(x, y); var b = gs.Ctm.Apply(x + w, y + h);
                                    var c2 = gs.Ctm.Apply(x + w, y); var d2 = gs.Ctm.Apply(x, y + h);
                                    if (Math.Abs(gs.Ctm.B) > 1e-6 || Math.Abs(gs.Ctm.C) > 1e-6) pathRect = false;
                                    PathPoint(a); PathPoint(b); PathPoint(c2); PathPoint(d2);
                                }
                                break;
                            case "m":
                                if (ops.Count >= 2) { pathLast = gs.Ctm.Apply(Num(ops[ops.Count - 2]), Num(ops[ops.Count - 1])); PathPoint(pathLast); }
                                break;
                            case "l":
                                if (ops.Count >= 2)
                                {
                                    var pt = gs.Ctm.Apply(Num(ops[ops.Count - 2]), Num(ops[ops.Count - 1]));
                                    if (Math.Abs(pt.X - pathLast.X) > 0.5 && Math.Abs(pt.Y - pathLast.Y) > 0.5) pathRect = false;
                                    pathLast = pt; PathPoint(pt);
                                }
                                break;
                            case "c": case "v": case "y":
                                pathRect = false;
                                if (ops.Count >= 2) { pathLast = gs.Ctm.Apply(Num(ops[ops.Count - 2]), Num(ops[ops.Count - 1])); PathPoint(pathLast); }
                                break;
                            case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*":
                                if (!pathEmpty && pathRect && gs.FillComps != 0 && pathX1 - pathX0 > 2 && pathY1 - pathY0 > 2)
                                    output.Shapes.Add(new Shape { Rect = new RectangleF(pathX0, pathY0, pathX1 - pathX0, pathY1 - pathY0), Color = gs.Fill });
                                ClearPath();
                                break;
                            case "n": case "S": case "s": ClearPath(); break;
                            // ----- text -----
                            case "BT": tm = tlm = Matrix.Identity; break;
                            case "Tf":
                                if (ops.Count >= 2)
                                {
                                    var fonts = doc.Dict(Doc.Get(res, "/Font"));
                                    gs.FontRef = Doc.Get(fonts, ops[ops.Count - 2] as string);
                                    gs.Size = Num(ops[ops.Count - 1]);
                                }
                                break;
                            case "Tc": gs.Tc = Num(Last(ops)); break;
                            case "Tw": gs.Tw = Num(Last(ops)); break;
                            case "Tz": gs.Th = Num(Last(ops)) / 100.0; break;
                            case "TL": gs.TL = Num(Last(ops)); break;
                            case "Ts": gs.Rise = Num(Last(ops)); break;
                            case "Tr": gs.Render = (int)Num(Last(ops)); break;
                            case "Td": tlm = Matrix.Of(1, 0, 0, 1, Num(ops[0]), Num(ops[1])) * tlm; tm = tlm; break;
                            case "TD": gs.TL = -Num(ops[1]); tlm = Matrix.Of(1, 0, 0, 1, Num(ops[0]), Num(ops[1])) * tlm; tm = tlm; break;
                            case "Tm": tm = tlm = M(ops); break;
                            case "T*": tlm = Matrix.Of(1, 0, 0, 1, 0, -gs.TL) * tlm; tm = tlm; break;
                            case "Tj": Show(gs, ref tm, Last(ops), actual, ref actualDepthUsed); break;
                            case "'": tlm = Matrix.Of(1, 0, 0, 1, 0, -gs.TL) * tlm; tm = tlm; Show(gs, ref tm, Last(ops), actual, ref actualDepthUsed); break;
                            case "\"": gs.Tw = Num(ops[0]); gs.Tc = Num(ops[1]); tlm = Matrix.Of(1, 0, 0, 1, 0, -gs.TL) * tlm; tm = tlm; Show(gs, ref tm, ops[2], actual, ref actualDepthUsed); break;
                            case "TJ":
                                var arrTj = Last(ops) as List<object>;
                                if (arrTj != null)
                                    foreach (var item in arrTj)
                                    {
                                        if (item is byte[]) Show(gs, ref tm, item, actual, ref actualDepthUsed);
                                        else tm = Matrix.Of(1, 0, 0, 1, -Num(item) / 1000.0 * gs.Size * gs.Th, 0) * tm;
                                    }
                                break;
                            case "BDC":
                                var props = Last(ops) as Dictionary<string, object>;
                                string at = null;
                                object actualText;
                                if (props != null && props.TryGetValue("/ActualText", out actualText)) at = PdfString(actualText);
                                actual.Push(at);
                                break;
                            case "BMC": actual.Push(null); break;
                            case "EMC": if (actual.Count > 0) actual.Pop(); if (actualDepthUsed > actual.Count) actualDepthUsed = -1; break;
                            // ----- pictures and forms -----
                            case "Do":
                            {
                                var xobjs = doc.Dict(Doc.Get(res, "/XObject"));
                                var xref = Doc.Get(xobjs, ops.Count > 0 ? ops[0] as string : null) as string;
                                var xd = doc.Dict(xref);
                                string subtype = Doc.Get(xd, "/Subtype") as string;
                                if (subtype == "/Form")
                                {
                                    var fm = doc.Resolve(Doc.Get(xd, "/Matrix")) as object[];
                                    var m = fm != null && fm.Length == 6 ? Matrix.Of(Doc.Num(fm[0]), Doc.Num(fm[1]), Doc.Num(fm[2]), Doc.Num(fm[3]), Doc.Num(fm[4]), Doc.Num(fm[5])) : Matrix.Identity;
                                    var fres = doc.Dict(Doc.Get(xd, "/Resources")) ?? res;
                                    var savedS = s; int savedPos = p;
                                    Run(doc.StreamData(xref), fres, m * gs.Ctm, depth + 1);
                                    s = savedS; p = savedPos;
                                }
                                else if (subtype == "/Image" && xref != null)
                                {
                                    // the image fills the unit square under the current matrix
                                    var corners = new[] { gs.Ctm.Apply(0, 0), gs.Ctm.Apply(1, 0), gs.Ctm.Apply(0, 1), gs.Ctm.Apply(1, 1) };
                                    float x0 = corners.Min(c => c.X), x1 = corners.Max(c => c.X), y0 = corners.Min(c => c.Y), y1 = corners.Max(c => c.Y);
                                    if (x1 - x0 >= 6 && y1 - y0 >= 6)
                                        output.Pictures.Add(new Picture { Ref = xref, Rect = new RectangleF(x0, y0, x1 - x0, y1 - y0) });
                                }
                                break;
                            }
                            case "BI": SkipInlineImage(); break;
                        }
                    }
                    catch { /* one bad operator must not lose the page */ }
                    ops.Clear();
                }
                s = saved; p = savedP;
            }

            static object Last(List<object> ops) { return ops.Count > 0 ? ops[ops.Count - 1] : null; }
            static double Num(object o) { return o is double ? (double)o : 0; }
            static GState Clone(GState g) { return (GState)g.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(g, null); }
            static Matrix M(List<object> o) { int n = o.Count; return n < 6 ? Matrix.Identity : Matrix.Of(Num(o[n - 6]), Num(o[n - 5]), Num(o[n - 4]), Num(o[n - 3]), Num(o[n - 2]), Num(o[n - 1])); }
            static int Clamp(double v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }
            static int Gray(double v) { int c = Clamp(v); return (c << 16) | (c << 8) | c; }
            static int Rgb(double r, double g, double b) { return (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b); }
            static int CmykColor(double c, double m, double y, double k) { return Rgb((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k)); }

            static string PdfString(object o)
            {
                var b = o as byte[];
                if (b == null) return null;
                if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
                return Encoding.GetEncoding(28591).GetString(b);
            }

            void Show(GState gs, ref Matrix tm, object str, Stack<string> actual, ref int actualDepthUsed)
            {
                var bytes = str as byte[];
                if (bytes == null || gs.FontRef == null) return;
                var font = doc.GetFont(gs.FontRef);
                // inside a span with /ActualText, emit that text once instead of the glyphs
                string at = actual.Count > 0 ? actual.FirstOrDefault(a => a != null) : null;
                int step = font.TwoByte ? 2 : 1;
                var list = output.Glyphs;
                for (int i = 0; i + step <= bytes.Length; i += step)
                {
                    int code = font.TwoByte ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
                    string text;
                    if (!font.Unicode.TryGetValue(code, out text))
                        text = font.Fallback != null ? font.Fallback.GetString(new[] { (byte)code }) : "";
                    double w0;
                    if (!font.Widths.TryGetValue(code, out w0)) w0 = font.DefaultWidth;
                    double adv = (w0 / 1000.0 * gs.Size + gs.Tc + (!font.TwoByte && code == 32 ? gs.Tw : 0)) * gs.Th;
                    var trm = Matrix.Of(gs.Size * gs.Th, 0, 0, gs.Size, 0, gs.Rise) * tm * gs.Ctm;
                    double scaleX = Math.Sqrt(trm.A * trm.A + trm.B * trm.B) / Math.Max(1e-9, gs.Size * gs.Th);
                    double size = Math.Sqrt(trm.C * trm.C + trm.D * trm.D);
                    Func<string, Glyph> make = t => new Glyph
                    {
                        Text = t, X = trm.E, Y = trm.F, W = adv * scaleX, Size = size, Bold = font.Bold || gs.Render == 2, Italic = font.Italic,
                        Font = font.Family, Color = gs.Fill, Invisible = gs.Render == 3 || gs.Render == 7,
                    };
                    if (at != null)
                    {
                        if (actualDepthUsed != actual.Count) { list.Add(make(at)); actualDepthUsed = actual.Count; }
                        else if (list.Count > 0) list[list.Count - 1].W = Math.Max(list[list.Count - 1].W, trm.E + adv * scaleX - list[list.Count - 1].X);
                    }
                    else if (text.Length > 0 && size > 0.5) list.Add(make(text));
                    tm = Matrix.Of(1, 0, 0, 1, adv, 0) * tm;
                }
            }

            // ---------- tokenizer ----------

            class Op { public string Name; }

            static bool IsWs(byte b) { return b == 32 || b == 10 || b == 13 || b == 9 || b == 12 || b == 0; }
            static bool IsDelim(byte b) { return b == '(' || b == ')' || b == '<' || b == '>' || b == '[' || b == ']' || b == '{' || b == '}' || b == '/' || b == '%'; }

            object Next()
            {
                while (p < s.Length)
                {
                    if (IsWs(s[p])) { p++; continue; }
                    if (s[p] == '%') { while (p < s.Length && s[p] != '\n' && s[p] != '\r') p++; continue; }
                    break;
                }
                if (p >= s.Length) return null;
                byte c = s[p];
                if (c == '(') return LiteralString();
                if (c == '<' && p + 1 < s.Length && s[p + 1] == '<') { p += 2; return DictObj(); }
                if (c == '<') return HexString();
                if (c == '[') { p++; return ArrayObj(); }
                if (c == ']' || c == '>') { p++; return Next(); }
                if (c == '/') { int st = p++; while (p < s.Length && !IsWs(s[p]) && !IsDelim(s[p])) p++; return Encoding.ASCII.GetString(s, st, p - st); }
                int start = p;
                while (p < s.Length && !IsWs(s[p]) && !IsDelim(s[p])) p++;
                if (p == start) { p++; return Next(); }
                string word = Encoding.ASCII.GetString(s, start, p - start);
                double num;
                if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out num)) return num;
                if (word == "true" || word == "false" || word == "null") return word;
                return new Op { Name = word };
            }

            List<object> ArrayObj()
            {
                var list = new List<object>();
                while (p < s.Length)
                {
                    while (p < s.Length && IsWs(s[p])) p++;
                    if (p < s.Length && s[p] == ']') { p++; break; }
                    object o = Next();
                    if (o == null || o is Op) break;
                    list.Add(o);
                }
                return list;
            }

            Dictionary<string, object> DictObj()
            {
                var d = new Dictionary<string, object>();
                while (p < s.Length)
                {
                    while (p < s.Length && IsWs(s[p])) p++;
                    if (p + 1 < s.Length && s[p] == '>' && s[p + 1] == '>') { p += 2; break; }
                    object k = Next();
                    if (!(k is string)) break;
                    object v = Next();
                    if (v is Op) break;
                    d[(string)k] = v;
                }
                return d;
            }

            byte[] LiteralString()
            {
                p++;
                var b = new List<byte>();
                int depth = 1;
                while (p < s.Length)
                {
                    byte c = s[p++];
                    if (c == '\\' && p < s.Length)
                    {
                        byte e = s[p++];
                        switch (e)
                        {
                            case (byte)'n': b.Add(10); break;
                            case (byte)'r': b.Add(13); break;
                            case (byte)'t': b.Add(9); break;
                            case (byte)'b': b.Add(8); break;
                            case (byte)'f': b.Add(12); break;
                            case 13: if (p < s.Length && s[p] == 10) p++; break;
                            case 10: break;
                            default:
                                if (e >= '0' && e <= '7')
                                {
                                    int v = e - '0';
                                    for (int k = 0; k < 2 && p < s.Length && s[p] >= '0' && s[p] <= '7'; k++) v = v * 8 + (s[p++] - '0');
                                    b.Add((byte)v);
                                }
                                else b.Add(e);
                                break;
                        }
                    }
                    else if (c == '(') { depth++; b.Add(c); }
                    else if (c == ')') { if (--depth == 0) break; b.Add(c); }
                    else b.Add(c);
                }
                return b.ToArray();
            }

            byte[] HexString()
            {
                p++;
                var hex = new StringBuilder();
                while (p < s.Length && s[p] != '>') { if (!IsWs(s[p])) hex.Append((char)s[p]); p++; }
                p++;
                if (hex.Length % 2 == 1) hex.Append('0');
                var b = new byte[hex.Length / 2];
                for (int i = 0; i < b.Length; i++) b[i] = (byte)Convert.ToInt32(hex.ToString(i * 2, 2), 16);
                return b;
            }

            void SkipInlineImage()
            {
                // skip to "ID", then to whitespace + "EI" + whitespace/end
                while (p + 1 < s.Length && !(s[p] == 'I' && s[p + 1] == 'D' && (p == 0 || IsWs(s[p - 1])))) p++;
                p += 2;
                while (p + 2 < s.Length && !(IsWs(s[p]) && s[p + 1] == 'E' && s[p + 2] == 'I' && (p + 3 >= s.Length || IsWs(s[p + 3])))) p++;
                p += 3;
            }
        }

        // ================= layout =================

        static bool InRange(char c, int lo, int hi) { return c >= lo && c <= hi; }
        static bool IsRtlChar(char c) { return InRange(c, 0x0590, 0x08FF) || InRange(c, 0xFB1D, 0xFDFF) || InRange(c, 0xFE70, 0xFEFF); }

        /// Arabic presentation forms (shaped letters) back to plain letters, e.g. "ﻻ" → "لا".
        static string Plain(string t)
        {
            if (!t.Any(c => InRange(c, 0xFB50, 0xFDFF) || InRange(c, 0xFE70, 0xFEFF))) return t;
            return t.Normalize(NormalizationForm.FormKC);
        }

        internal class Line
        {
            public List<Glyph> Glyphs = new List<Glyph>();
            public double Y, Size, Left, Right;
        }

        /// Glyphs → lines by baseline, top of the page first, each line left to right.
        static List<Line> GroupLines(List<Glyph> glyphs)
        {
            var lines = new List<Line>();
            foreach (var g in glyphs.OrderByDescending(g => g.Y).ThenBy(g => g.X))
            {
                var line = lines.LastOrDefault(l => Math.Abs(l.Y - g.Y) <= Math.Max(l.Size, g.Size) * 0.35);
                if (line == null) { line = new Line { Y = g.Y, Size = g.Size }; lines.Add(line); }
                line.Glyphs.Add(g);
                if (!g.Mark && g.Text.Trim().Length > 0) line.Size = Math.Max(line.Size, g.Size);
            }
            lines = lines.OrderByDescending(l => l.Y).ToList();
            foreach (var l in lines)
            {
                l.Glyphs = l.Glyphs.OrderBy(g => g.X).ToList();
                var real = l.Glyphs.Where(g => g.Text.Trim().Length > 0).ToList();
                if (real.Count == 0) continue;
                l.Left = real.Min(g => g.X); l.Right = real.Max(g => g.X + g.W);
            }
            return lines.Where(l => l.Glyphs.Any(g => g.Text.Trim().Length > 0)).ToList();
        }

        static Page Layout(PageContent content)
        {
            var page = new Page
            {
                X0 = content.Media.X, Y0 = content.Media.Y, Width = content.Media.Width, Height = content.Media.Height,
            };
            var glyphs = content.Glyphs;
            foreach (var g in glyphs) g.Text = Plain(g.Text.Replace((char)0xA0, ' '));
            glyphs = glyphs.Where(g => g.Text.Length > 0).ToList();
            double pageArea = page.Width * page.Height;

            // an OCR'd scan: invisible text laid over a picture of the page — keep the text, drop the page picture
            bool invisibleText = glyphs.Count(g => g.Invisible) * 2 > glyphs.Count;
            foreach (var g in glyphs) g.Invisible = false;

            // 1) group into lines by baseline (top of page first)
            var lines = GroupLines(glyphs);

            // text column and margins
            double colL = lines.Count > 0 ? lines.Min(l => l.Left) : page.X0 + 72, colR = lines.Count > 0 ? lines.Max(l => l.Right) : page.X0 + page.Width - 72;
            foreach (var pc in content.Pictures.Where(pc => pc.Rect.Width * pc.Rect.Height < pageArea * 0.6)) { colL = Math.Min(colL, pc.Rect.X); colR = Math.Max(colR, pc.Rect.Right); }
            page.Left = Math.Max(18, Math.Min(108, colL - page.X0));
            page.Right = Math.Max(18, Math.Min(108, page.X0 + page.Width - colR));
            if (lines.Count > 0) page.TopMargin = Math.Max(18, Math.Min(108, page.Y0 + page.Height - (lines[0].Y + lines[0].Size)));

            // two columns side by side (cards, newsletters): lay each column out on its own
            if (!content.NoColumns && lines.Count >= 6)
            {
                double gutter;
                if (FindGutter(lines, content.Pictures, colL, colR, pageArea, out gutter)) return LayoutColumns(content, page, lines, gutter, colL, colR);
            }

            // page colour: a filled shape covering (almost) the whole page
            var big = content.Shapes.Where(sh => sh.Rect.Width * sh.Rect.Height >= pageArea * 0.85 && !IsWhite(sh.Color)).ToList();
            if (big.Count > 0) page.Background = big[big.Count - 1].Color;
            var shades = MergeShapes(content.Shapes.Where(sh => sh.Rect.Width * sh.Rect.Height < pageArea * 0.85 && !IsWhite(sh.Color)).ToList());

            // body size: the size most characters are set in
            var sized = glyphs.Where(g => g.Text.Trim().Length > 0).GroupBy(g => Math.Round(g.Size * 2) / 2).OrderByDescending(gr => gr.Sum(x => x.Text.Length)).ToList();
            double body = sized.Count > 0 ? sized[0].Key : 11;

            // page direction: on a mostly Arabic/Hebrew page, any line with such letters reads right to left
            int pageR = 0, pageL = 0;
            foreach (var g in glyphs) foreach (char c in g.Text) if (char.IsLetter(c)) { if (IsRtlChar(c)) pageR++; else pageL++; }
            bool pageRtl = pageR > pageL;

            // a line counts as a table row only next to another row-like line (one stray wide gap is not a table)
            var cellsOf = lines.Select(SplitCells).ToList();
            for (int k = 0; k < lines.Count; k++)
            {
                var c = cellsOf[k];
                if (c.Count < 2) continue;
                // a list bullet followed by a tab-like gap is not a table column
                if (IsBulletCell(c[0]) || IsBulletCell(c[c.Count - 1])) cellsOf[k] = c.Count == 2 ? new List<List<Glyph>> { lines[k].Glyphs } : c;
            }
            var isRow = new bool[lines.Count];
            for (int k = 0; k < lines.Count; k++)
            {
                if (cellsOf[k].Count < 2) continue;
                bool up = k > 0 && cellsOf[k - 1].Count >= 2 && lines[k - 1].Y - lines[k].Y < lines[k].Size * 3;
                bool down = k + 1 < lines.Count && cellsOf[k + 1].Count >= 2 && lines[k].Y - lines[k + 1].Y < lines[k].Size * 3;
                isRow[k] = up || down || cellsOf[k].Count >= 3;   // three wide-apart columns are a table row even alone (e.g. split across pages)
            }

            // whole tables: columns from the row lines, rows that span several lines, pictures inside their cells
            var tableAt = new Dictionary<int, List<Block>>();
            var inTable = new bool[lines.Count];
            var usedPictures = new HashSet<Picture>();
            BuildTables(lines, isRow, cellsOf, shades, content.Pictures, pageRtl, pageArea, colL, colR, content.FromOcr, tableAt, inTable, usedPictures);

            // 2) each line into cells (wide gaps) and text in reading order
            var blocks = new List<Block>();
            Block current = null;
            Line prev = null;
            for (int li = 0; li < lines.Count; li++)
            {
                var l = lines[li];
                if (inTable[li])
                {
                    List<Block> rowsHere;
                    if (tableAt.TryGetValue(li, out rowsHere)) blocks.AddRange(rowsHere);
                    current = null; prev = l;
                    continue;
                }
                int r = 0, lt = 0;
                foreach (var g in l.Glyphs) foreach (char c in g.Text) if (char.IsLetter(c)) { if (IsRtlChar(c)) r++; else lt++; }
                bool rtl = r > 0 && (pageRtl || r >= lt);
                // a wrapped line of only Latin text that still belongs to a right-to-left paragraph (same right edge)
                if (!rtl && r == 0 && current != null && current.Rtl && current.Kind == Kind.Para && prev != null && prev.Y - l.Y < l.Size * 1.75
                    && Math.Abs(prev.Right - l.Right) < l.Size * 1.5 && Math.Abs(prev.Size - l.Size) < 0.6)
                    rtl = true;
                bool bold = l.Glyphs.Count(g => g.Bold) * 2 > l.Glyphs.Count;
                double top = l.Y + l.Size * 0.8, bottom = l.Y - l.Size * 0.25;
                var cells = cellsOf[li];
                if (isRow[li])
                {
                    var runs = cells.Select(c => Logical(c, rtl)).ToList();
                    var shadeOf = cells.Select(c => ShadeUnder(shades, c.Min(g => g.X), c.Max(g => g.X + g.W), top, bottom)).ToList();
                    var lefts = cells.Select(c => c.Where(g => !IsSpace(g)).Min(g => g.X)).ToList();
                    var rights = cells.Select(c => c.Where(g => !IsSpace(g)).Max(g => g.X + g.W)).ToList();
                    if (rtl) { runs.Reverse(); shadeOf.Reverse(); lefts.Reverse(); rights.Reverse(); }
                    blocks.Add(new Block
                    {
                        Kind = Kind.Row, CellRuns = runs, Cells = runs.Select(x => Text(x).Trim()).ToList(), CellShade = shadeOf, CellLeft = lefts, CellRight = rights,
                        Rtl = rtl, Bold = bold, Size = l.Size, Top = top, Bottom = bottom,
                    });
                    current = null; prev = l;
                    continue;
                }
                var lineRuns = Logical(l.Glyphs, rtl);
                TrimRuns(lineRuns);
                string text = Text(lineRuns);
                if (text.Length == 0) continue;
                Kind kind = Kind.Para;
                if (l.Size >= body * 1.6) kind = Kind.Heading1;
                else if (l.Size >= body * 1.25) kind = Kind.Heading2;
                else if (l.Size >= body * 1.08 && bold) kind = Kind.Heading3;
                string bullet = StripBullet(ref text);
                if (bullet != null) { kind = Kind.Bullet; DropFirstChar(lineRuns); }

                // continue the previous paragraph when this line just wraps it
                bool joins = current != null && prev != null && kind == current.Kind && (kind == Kind.Para || kind == Kind.Heading1 || kind == Kind.Heading2 || kind == Kind.Heading3)
                             && current.Rtl == rtl && Math.Abs(prev.Size - l.Size) < 0.6
                             && prev.Y - l.Y < l.Size * 1.75;
                if (current != null && kind == Kind.Para && current.Kind == Kind.Bullet && prev != null && prev.Y - l.Y < l.Size * 1.75 && current.Rtl == rtl
                    && Math.Abs(prev.Size - l.Size) < 0.6)
                    joins = true; // wrapped bullet text
                int shade = ShadeUnder(shades, l.Left, l.Right, top, bottom);
                if (joins)
                {
                    var last = current.Runs.Count > 0 ? current.Runs[current.Runs.Count - 1] : null;
                    current.Runs.Add(new Run { Text = " ", Font = last != null ? last.Font : "", Size = l.Size, Bold = last != null && last.Bold, Italic = last != null && last.Italic, Color = last != null ? last.Color : 0 });
                    current.Runs.AddRange(lineRuns);
                    current.Bottom = bottom;
                    current.Lines.Add(l);
                }
                else
                {
                    current = new Block { Kind = kind, Runs = lineRuns, Rtl = rtl, Bold = bold, Size = l.Size, Top = top, Bottom = bottom, Shade = shade };
                    current.Lines.Add(l);
                    blocks.Add(current);
                }
                prev = l;
            }

            // all rows of one table share its direction (a row like "Klementina Pro | 450" has no Arabic letters)
            for (int a = 0; a < blocks.Count; a++)
            {
                if (blocks[a].Kind != Kind.Row) continue;
                int b = a;
                while (b + 1 < blocks.Count && blocks[b + 1].Kind == Kind.Row) b++;
                int rtlRows = 0;
                for (int k = a; k <= b; k++) if (blocks[k].Rtl) rtlRows++;
                bool tableRtl = rtlRows * 2 >= (b - a + 1) && rtlRows > 0;
                for (int k = a; k <= b; k++)
                    if (blocks[k].Rtl != tableRtl)
                    {
                        blocks[k].Cells.Reverse(); blocks[k].CellRuns.Reverse(); blocks[k].CellShade.Reverse();
                        blocks[k].CellLeft.Reverse(); blocks[k].CellRight.Reverse();
                        blocks[k].Rtl = tableRtl;
                    }
                a = b;
            }

            // finish paragraphs: text, alignment
            foreach (var b in blocks)
            {
                if (b.Kind == Kind.Row) continue;
                MergeRuns(b.Runs);
                b.Text = Text(b.Runs);
                b.Align = Alignment(b, colL, colR);
            }

            // 3) pictures: a page-sized one behind the text is the background, the others go into the flow by position
            foreach (var pic in content.Pictures)
            {
                if (usedPictures.Contains(pic)) continue;
                var rc = pic.Rect;
                bool pageSized = rc.Width * rc.Height >= pageArea * 0.6;
                bool underText = lines.Any(l => l.Y > rc.Y && l.Y < rc.Bottom && l.Right > rc.X && l.Left < rc.Right);
                if (pageSized && invisibleText) continue;
                if (pageSized && underText)
                {
                    if (page.BackgroundImage != null) continue;
                    page.BackgroundRect = rc;
                    page.BackgroundImage = pic.File ?? "";   // "" = not decodable here: the caller crops it from a render
                    continue;
                }
                var ib = new Block
                {
                    Kind = Kind.Image, Image = pic.File, ImageW = rc.Width, ImageH = rc.Height, ImageRect = rc, Top = rc.Bottom, Bottom = rc.Y,
                    Align = Math.Abs((rc.X + rc.Right) / 2 - (colL + colR) / 2) < (colR - colL) * 0.08 ? "center" : (rc.X - colL < (colR - rc.Right) ? "left" : "right"),
                };
                int at = blocks.FindIndex(x => x.Top < ib.Top - 1);
                if (at < 0) blocks.Add(ib); else blocks.Insert(at, ib);
            }

            // spacing between blocks, from the real gaps
            for (int i = 0; i + 1 < blocks.Count; i++)
                blocks[i].SpaceAfter = Math.Max(0, Math.Min(36, blocks[i].Bottom - blocks[i + 1].Top));

            page.Blocks = blocks;
            return page;
        }

        /// Word paints a cell background as several touching strips; same-coloured touching shapes become one.
        static List<Shape> MergeShapes(List<Shape> shapes)
        {
            if (shapes.Count > 1500) return shapes;
            var list = shapes.Select(sh => new Shape { Rect = sh.Rect, Color = sh.Color }).ToList();
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        if (list[i].Color != list[j].Color) continue;
                        var a = list[i].Rect; a.Inflate(1.2f, 1.2f);
                        if (!a.IntersectsWith(list[j].Rect)) continue;
                        list[i].Rect = RectangleF.Union(list[i].Rect, list[j].Rect);
                        list.RemoveAt(j);
                        merged = true;
                        break;
                    }
            }
            return list;
        }

        /// Finds tables: a run of row lines (several wide-apart columns) plus the lines between and right next to
        /// them. Columns come from the row lines' cells and the pictures inside; lines close together form one row
        /// (a cell may hold several lines, e.g. a name above its colour); pictures go into their cells.
        static void BuildTables(List<Line> lines, bool[] isRow, List<List<List<Glyph>>> cellsOf, List<Shape> shades, List<Picture> pictures,
                                bool pageRtl, double pageArea, double colL, double colR, bool ocr,
                                Dictionary<int, List<Block>> tableAt, bool[] inTable, HashSet<Picture> used)
        {
            int n = lines.Count, k = 0;
            while (k < n)
            {
                if (!isRow[k]) { k++; continue; }
                int a = k, b = k, fullest = k;
                double spanL = lines[k].Left, spanR = lines[k].Right;
                // a line belongs between table rows if it sits within the table, or is short (a name, a colour, a price)
                Func<int, bool> inside = j => (lines[j].Left >= spanL - lines[j].Size * 2 && lines[j].Right <= spanR + lines[j].Size * 2)
                                              || lines[j].Right - lines[j].Left < (colR - colL) * 0.45;
                for (int j = k + 1; j < n; j++)
                {
                    if (!isRow[j])
                    {
                        if (!inside(j)) break;          // a full-width paragraph ends the table
                        if (lines[j].Size > lines[b].Size * 1.3 && lines[j].Size > lines[k].Size * 1.3) break;   // so does a heading
                        continue;
                    }
                    if (lines[b].Y - lines[j].Y > lines[b].Size * 10) break;
                    // a different column layout is a different table: compare with the previous row and the fullest one
                    if (!SameColumns(cellsOf[b], cellsOf[j]) || !SameColumns(cellsOf[fullest], cellsOf[j])) break;
                    b = j;
                    if (cellsOf[j].Count > cellsOf[fullest].Count) fullest = j;
                    spanL = Math.Min(spanL, lines[j].Left); spanR = Math.Max(spanR, lines[j].Right);
                }
                // OCR drops symbols such as "×" and leaves a gap: two-column "rows" there are just sentences
                if (ocr && Enumerable.Range(a, b - a + 1).Where(j => isRow[j]).All(j => cellsOf[j].Count == 2))
                {
                    for (int j = a; j <= b; j++) { isRow[j] = false; cellsOf[j] = new List<List<Glyph>> { lines[j].Glyphs }; }
                    k = b + 1;
                    continue;
                }
                // a line just above/below the first/last row line, closer than a line apart, belongs to that row
                while (a > 0 && !isRow[a - 1] && !inTable[a - 1] && lines[a - 1].Y - lines[a].Y < lines[a].Size * 1.1 && inside(a - 1)) a--;
                while (b + 1 < n && !isRow[b + 1] && lines[b].Y - lines[b + 1].Y < lines[b].Size * 1.1 && inside(b + 1)) b++;
                k = b + 1;

                var idx = Enumerable.Range(a, b - a + 1).ToList();
                double regionTop = idx.Max(j => lines[j].Y + lines[j].Size), regionBottom = idx.Min(j => lines[j].Y - lines[j].Size * 0.4);
                var pics = pictures.Where(p => p.File != null && p.Rect.Width * p.Rect.Height < pageArea * 0.3
                    && p.Rect.X + p.Rect.Width / 2 > spanL - 60 && p.Rect.X + p.Rect.Width / 2 < spanR + 60
                    && p.Rect.Y + p.Rect.Height / 2 < regionTop + 6 && p.Rect.Y + p.Rect.Height / 2 > regionBottom - 6).ToList();

                // columns: merge the horizontal extents of the row cells and pictures
                var spans = new List<double[]>();
                foreach (int j in idx.Where(j => cellsOf[j].Count >= 2))
                    foreach (var c in cellsOf[j])
                    {
                        var solid = c.Where(g => !IsSpace(g)).ToList();
                        if (solid.Count > 0) spans.Add(new[] { solid.Min(g => g.X), solid.Max(g => g.X + g.W) });
                    }
                foreach (var p in pics) spans.Add(new double[] { p.Rect.X, p.Rect.Right });
                spans = spans.OrderBy(sp => sp[0]).ToList();
                var cols = new List<double[]>();
                foreach (var sp in spans)
                {
                    if (cols.Count > 0 && sp[0] <= cols[cols.Count - 1][1] + 2) cols[cols.Count - 1][1] = Math.Max(cols[cols.Count - 1][1], sp[1]);
                    else cols.Add(new[] { sp[0], sp[1] });
                }
                if (cols.Count < 2) continue;
                Func<double, int> colOf = x =>
                {
                    for (int c = 0; c < cols.Count; c++) if (x >= cols[c][0] - 1 && x <= cols[c][1] + 1) return c;
                    int best = 0; double bd = double.MaxValue;
                    for (int c = 0; c < cols.Count; c++) { double d = Math.Min(Math.Abs(x - cols[c][0]), Math.Abs(x - cols[c][1])); if (d < bd) { bd = d; best = c; } }
                    return best;
                };

                // direction of the table
                int rl = 0, ll = 0;
                foreach (int j in idx) foreach (var g in lines[j].Glyphs) foreach (char ch in g.Text) if (char.IsLetter(ch)) { if (IsRtlChar(ch)) rl++; else ll++; }
                bool rtl = pageRtl ? rl > 0 : rl > ll;

                // rows: a gap clearly larger than the gaps inside rows starts a new row
                var gaps = new List<double>();
                for (int i = 0; i + 1 < idx.Count; i++) gaps.Add(lines[idx[i]].Y - lines[idx[i + 1]].Y);
                double thr = gaps.Count > 0 && gaps.Max() > gaps.Min() * 1.45 ? (gaps.Min() + gaps.Max()) / 2 : 0;
                var groups = new List<List<int>> { new List<int> { idx[0] } };
                for (int i = 1; i < idx.Count; i++)
                {
                    if (gaps[i - 1] > thr) groups.Add(new List<int>());
                    groups[groups.Count - 1].Add(idx[i]);
                }
                var spanOf = groups.Select(gr => new[] { gr.Max(j => lines[j].Y + lines[j].Size * 0.8), gr.Min(j => lines[j].Y - lines[j].Size * 0.25) }).ToList();
                var picsOf = groups.Select(gr => new List<Picture>()).ToList();
                foreach (var p in pics)
                {
                    double cy = p.Rect.Y + p.Rect.Height / 2;
                    int best = 0; double bd = double.MaxValue;
                    for (int gi = 0; gi < groups.Count; gi++)
                    {
                        double d = cy > spanOf[gi][0] ? cy - spanOf[gi][0] : (cy < spanOf[gi][1] ? spanOf[gi][1] - cy : 0);
                        if (d < bd) { bd = d; best = gi; }
                    }
                    picsOf[best].Add(p);
                }

                var rows = new List<Block>();
                for (int gi = 0; gi < groups.Count; gi++)
                {
                    var gr = groups[gi];
                    var cellRuns = cols.Select(c => new List<Run>()).ToList();
                    foreach (var p in picsOf[gi])
                        cellRuns[colOf(p.Rect.X + p.Rect.Width / 2)].Add(new Run { Text = "", Image = p.File, ImageW = p.Rect.Width, ImageH = p.Rect.Height });
                    foreach (int j in gr)
                        for (int c = 0; c < cols.Count; c++)
                        {
                            var gl = lines[j].Glyphs.Where(g => colOf(g.X + g.W / 2) == c).ToList();
                            if (!gl.Any(g => !IsSpace(g))) continue;
                            var lr = Logical(gl, rtl);
                            TrimRuns(lr);
                            if (lr.Count == 0) continue;
                            if (cellRuns[c].Any(x => x.Image == null)) cellRuns[c].Add(new Run { Text = "\n", Font = lr[0].Font, Size = lr[0].Size, Color = lr[0].Color });
                            cellRuns[c].AddRange(lr);
                        }
                    double top = Math.Max(spanOf[gi][0], picsOf[gi].Count > 0 ? picsOf[gi].Max(p => (double)p.Rect.Bottom) : 0);
                    double bottom = Math.Min(spanOf[gi][1], picsOf[gi].Count > 0 ? picsOf[gi].Min(p => (double)p.Rect.Y) : double.MaxValue);
                    var lefts = cols.Select(c => c[0]).ToList();
                    var rights = cols.Select(c => c[1]).ToList();
                    var shade = cols.Select(c => ShadeUnder(shades, c[0], c[1], spanOf[gi][0], spanOf[gi][1])).ToList();
                    if (rtl) { cellRuns.Reverse(); lefts.Reverse(); rights.Reverse(); shade.Reverse(); }
                    var allGlyphs = gr.SelectMany(j => lines[j].Glyphs).ToList();
                    rows.Add(new Block
                    {
                        Kind = Kind.Row, CellRuns = cellRuns, Cells = cellRuns.Select(x => Text(x).Replace("\n", " ").Trim()).ToList(),
                        CellShade = shade, CellLeft = lefts, CellRight = rights, Rtl = rtl,
                        Bold = allGlyphs.Count(g => g.Bold) * 2 > allGlyphs.Count, Size = gr.Max(j => lines[j].Size), Top = top, Bottom = bottom,
                    });
                }
                for (int j = a; j <= b; j++) inTable[j] = true;
                tableAt[a] = rows;
                used.UnionWith(pics);
            }
        }

        /// Two row lines share a column layout unless a cell of one spans two cells of the other.
        static bool SameColumns(List<List<Glyph>> x, List<List<Glyph>> y)
        {
            Func<List<Glyph>, double[]> ext = c =>
            {
                var solid = c.Where(g => !IsSpace(g)).ToList();
                return solid.Count == 0 ? null : new[] { solid.Min(g => g.X), solid.Max(g => g.X + g.W) };
            };
            var ex = x.Select(ext).Where(e => e != null).ToList();
            var ey = y.Select(ext).Where(e => e != null).ToList();
            Func<double[], List<double[]>, bool> spansTwo = (c, others) => others.Count(o => Math.Min(c[1], o[1]) - Math.Max(c[0], o[0]) > 1) >= 2;
            return !ex.Any(c => spansTwo(c, ey)) && !ey.Any(c => spansTwo(c, ex));
        }

        /// A vertical gap near the middle that no text or picture crosses, except full-width lines (title, footer).
        static bool FindGutter(List<Line> lines, List<Picture> pictures, double colL, double colR, double pageArea, out double gutter)
        {
            gutter = 0;
            double width = colR - colL;
            if (width < 200) return false;
            var narrow = lines.Where(l => l.Right - l.Left < width * 0.7).ToList();
            var pics = pictures.Where(p => p.Rect.Width * p.Rect.Height < pageArea * 0.6 && p.Rect.Width < width * 0.7).ToList();
            double from = colL + width * 0.38, to = colL + width * 0.62;
            double bestStart = 0, bestLen = 0, runStart = -1;
            for (double x = from; x <= to + 0.01; x += 1)
            {
                bool blocked = narrow.Any(l => l.Glyphs.Any(g => !IsSpace(g) && g.X < x && g.X + g.W > x))
                               || pics.Any(p => p.Rect.X < x && p.Rect.Right > x);
                if (!blocked) { if (runStart < 0) runStart = x; if (x - runStart + 1 > bestLen) { bestLen = x - runStart + 1; bestStart = runStart; } }
                else runStart = -1;
            }
            if (bestLen < 12) return false;
            double g0 = bestStart + bestLen / 2;
            // both sides must really hold content
            int left = narrow.Count(l => l.Right <= g0), right = narrow.Count(l => l.Left >= g0);
            int picsL = pics.Count(p => p.Rect.Right <= g0), picsR = pics.Count(p => p.Rect.X >= g0);
            if (left + picsL * 3 < 3 || right + picsR * 3 < 3) return false;
            // a table whose columns happen to leave a gap is not two columns: its lines reach across the gap
            // pictures side by side across the gap settle it (cards, galleries); otherwise no table may reach across it
            bool sideBySide = pics.Any(p1 => p1.Rect.Right <= g0 && pics.Any(p2 => p2.Rect.X >= g0
                                  && Math.Min(p1.Rect.Bottom, p2.Rect.Bottom) - Math.Max(p1.Rect.Y, p2.Rect.Y) > 0.3 * Math.Min(p1.Rect.Height, p2.Rect.Height)));
            if (!sideBySide)
            {
                Func<Line, bool> bothSides = l => l.Glyphs.Any(g => !IsSpace(g) && g.X + g.W < g0) && l.Glyphs.Any(g => !IsSpace(g) && g.X > g0);
                int across = lines.Count(l => bothSides(l) && (GapAt(l, g0) < Math.Max(26, l.Size * 3.5) || GapAt(l, g0) < 1.5 * OtherGaps(l, g0)));
                int tableAcross = lines.Count(l => bothSides(l) && SplitCells(l).Count >= 3);
                if (across > 0 || tableAcross > 0) return false;
            }
            gutter = g0;
            return true;
        }

        /// The widest gap in a line other than the one around x (0 if none).
        static double OtherGaps(Line l, double x)
        {
            var solid = l.Glyphs.Where(g => !IsSpace(g)).OrderBy(g => g.X).ToList();
            double best = 0;
            for (int i = 1; i < solid.Count; i++)
            {
                double end = solid[i - 1].X + solid[i - 1].W, gap = solid[i].X - end;
                if (end <= x && solid[i].X >= x) continue;
                best = Math.Max(best, gap);
            }
            return best;
        }

        /// Width of the empty space in a line around x.
        static double GapAt(Line l, double x)
        {
            var solid = l.Glyphs.Where(g => !IsSpace(g)).ToList();
            double leftEnd = solid.Where(g => g.X + g.W <= x).Select(g => g.X + g.W).DefaultIfEmpty(double.MinValue).Max();
            double rightStart = solid.Where(g => g.X >= x).Select(g => g.X).DefaultIfEmpty(double.MaxValue).Min();
            return rightStart - leftEnd;
        }

        /// Full-width lines stay in the page flow; between them, each side of the gutter becomes a column laid out
        /// on its own, and the two columns sit side by side (right column first on a right-to-left page).
        static Page LayoutColumns(PageContent content, Page page, List<Line> lines, double gutter, double colL, double colR)
        {
            double width = colR - colL;
            // a full-width line crosses the gutter; a line with text in both columns at one height does not
            Func<Line, bool> spanning = l => l.Glyphs.Any(g => !IsSpace(g) && g.X < gutter && g.X + g.W > gutter)
                                             || (l.Right - l.Left >= width * 0.7 && GapAt(l, gutter) < 12);
            int pageR = 0, pageL = 0;
            foreach (var l in lines) foreach (var g in l.Glyphs) foreach (char c in g.Text) if (char.IsLetter(c)) { if (IsRtlChar(c)) pageR++; else pageL++; }
            bool rtl = pageR > pageL;

            // sections: runs of non-spanning lines between spanning ones
            var sections = new List<List<Line>>();
            var flow = new List<Line>();
            foreach (var l in lines)
            {
                if (spanning(l)) { flow.Add(l); sections.Add(null); continue; }
                if (sections.Count == 0 || sections[sections.Count - 1] == null) sections.Add(new List<Line>());
                sections[sections.Count - 1].Add(l);
            }
            var used = new HashSet<Picture>();
            Func<PageContent> sub = () => new PageContent { Media = content.Media, FromOcr = content.FromOcr, NoColumns = true, Shapes = content.Shapes };
            var colBlocks = new List<Block>();
            var real = sections.Where(x => x != null).ToList();
            for (int si = 0; si < real.Count; si++)
            {
                var sec = real[si];
                double top = sec.Max(l => l.Y + l.Size), bottom = sec.Min(l => l.Y - l.Size * 0.4);
                // stretch the section to the pictures beside it, up to the next / previous full-width line
                double ceiling = flow.Where(l => l.Y > top).Select(l => l.Y - l.Size * 0.3).DefaultIfEmpty(page.Y0 + page.Height).Min();
                double floor = flow.Where(l => l.Y < bottom).Select(l => l.Y + l.Size).DefaultIfEmpty(page.Y0).Max();
                var pics = content.Pictures.Where(pc => !used.Contains(pc) && pc.Rect.Width < width * 0.7 && pc.Rect.Bottom <= ceiling + 2 && pc.Rect.Y >= floor - 2).ToList();
                used.UnionWith(pics);
                if (pics.Count > 0) { top = Math.Max(top, pics.Max(pc => (double)pc.Rect.Bottom)); bottom = Math.Min(bottom, pics.Min(pc => (double)pc.Rect.Y)); }
                var left = sub(); var right = sub();
                foreach (var l in sec) foreach (var g in l.Glyphs) (g.X + g.W / 2 < gutter ? left : right).Glyphs.Add(g);
                foreach (var pc in pics) (pc.Rect.X + pc.Rect.Width / 2 < gutter ? left : right).Pictures.Add(pc);
                var lb = Layout(left).Blocks; var rb = Layout(right).Blocks;
                colBlocks.Add(new Block
                {
                    Kind = Kind.Columns, Columns = rtl ? new List<List<Block>> { rb, lb } : new List<List<Block>> { lb, rb },
                    Top = top, Bottom = bottom, Rtl = rtl, ImageW = gutter,
                });
            }
            // the full-width lines (and pictures outside the columns) as a normal page, then the columns by position
            var main = sub();
            main.NoColumns = true;
            foreach (var l in flow) main.Glyphs.AddRange(l.Glyphs);
            main.Pictures.AddRange(content.Pictures.Where(pc => !used.Contains(pc)));
            var mainPage = Layout(main);
            var blocks = mainPage.Blocks;
            foreach (var cb in colBlocks)
            {
                int at = blocks.FindIndex(x => x.Top < cb.Top - 1);
                if (at < 0) blocks.Add(cb); else blocks.Insert(at, cb);
            }
            for (int i = 0; i + 1 < blocks.Count; i++) blocks[i].SpaceAfter = Math.Max(0, Math.Min(36, blocks[i].Bottom - blocks[i + 1].Top));
            mainPage.Blocks = blocks;
            mainPage.Left = page.Left; mainPage.Right = page.Right; mainPage.TopMargin = page.TopMargin;
            if (mainPage.Background < 0) mainPage.Background = page.Background;
            return mainPage;
        }

        static bool IsWhite(int c) { return ((c >> 16) & 255) > 245 && ((c >> 8) & 255) > 245 && (c & 255) > 245; }

        /// Colour of the smallest filled shape lying under the given text box, -1 if none.
        static int ShadeUnder(List<Shape> shades, double left, double right, double top, double bottom)
        {
            Shape best = null;
            foreach (var sh in shades)
            {
                var rc = sh.Rect;
                if (rc.X <= left + 2 && rc.Right >= right - 2 && rc.Y <= bottom + 2 && rc.Bottom >= top - 2)
                    if (best == null || rc.Width * rc.Height < best.Rect.Width * best.Rect.Height) best = sh;
            }
            return best != null ? best.Color : -1;
        }

        static string Alignment(Block b, double colL, double colR)
        {
            if (b.Lines.Count == 0) return "";
            double width = colR - colL, tol = Math.Max(4, b.Size * 0.8);
            var ls = b.Lines;
            // several lines that reach both edges (except the last) are justified
            if (ls.Count >= 2 && ls.Take(ls.Count - 1).All(l => Math.Abs(l.Left - colL) < tol && Math.Abs(l.Right - colR) < tol)) return "both";
            bool allLeft = ls.All(l => Math.Abs(l.Left - colL) < tol), allRight = ls.All(l => Math.Abs(l.Right - colR) < tol);
            bool centered = ls.All(l => Math.Abs((l.Left + l.Right) / 2 - (colL + colR) / 2) < tol) && !(allLeft && allRight);
            if (centered && ls.All(l => l.Right - l.Left < width * 0.9)) return "center";
            if (b.Rtl) return allLeft && !allRight ? "left" : "right";
            return allRight && !allLeft ? "right" : "left";
        }

        static string StripBullet(ref string text)
        {
            if (text.Length < 2) return null;
            char c = text[0];
            bool isBullet = c == '•' || c == '·' || c == '▪' || c == '■' || c == '●' || c == '◦' || c == '-' || c == '–' || c == '§' || InRange(c, 0xF000, 0xF0FF); // Word's Symbol-font bullets land in U+F0xx
            if (!isBullet || !char.IsWhiteSpace(text[1]) && c == '-') return null;
            text = text.Substring(1).Trim();
            return c.ToString();
        }

        static bool IsBulletCell(List<Glyph> cell)
        {
            var solid = cell.Where(g => !IsSpace(g)).ToList();
            if (solid.Count != 1 || solid[0].Text.Trim().Length != 1) return false;
            string t = solid[0].Text.Trim() + " x";
            return StripBullet(ref t) != null;
        }

        static bool IsSpace(Glyph g) { return g.Text.Length > 0 && g.Text.Trim().Length == 0; }

        /// True when a real space glyph sits in the gap between two glyphs (not one drawn on top of a neighbour).
        /// Is there a space glyph centred inside (from, to)? spaceMids: the sorted centres of a line's space glyphs
        /// (a binary search instead of scanning the whole line for every gap).
        static bool SpaceIn(double[] spaceMids, double from, double to)
        {
            int lo = 0, hi = spaceMids.Length;
            double min = from - 0.01;
            while (lo < hi) { int mid = (lo + hi) / 2; if (spaceMids[mid] > min) hi = mid; else lo = mid + 1; }
            return lo < spaceMids.Length && spaceMids[lo] < to + 0.01;
        }

        /// Splits a line at gaps much wider than its ordinary word gaps: table cells / columns. Justified text stretches
        /// every word gap about equally, so it does not split; a table row has small gaps inside cells and big ones between.
        static List<List<Glyph>> SplitCells(Line l)
        {
            var solid = l.Glyphs.Where(g => !IsSpace(g) && !g.Mark).ToList();
            var gaps = new List<double>();
            for (int i = 1; i < solid.Count; i++)
            {
                double gap = solid[i].X - (solid[i - 1].X + solid[i - 1].W);
                if (gap > solid[i].Size * 0.15) gaps.Add(gap);
            }
            var cells = new List<List<Glyph>> { new List<Glyph>() };
            if (gaps.Count == 0) { cells[0].AddRange(l.Glyphs); return cells; }
            double word = Math.Min(gaps.Min(), l.Size * 0.45);              // an ordinary space on this line
            double split = Math.Max(l.Size * 0.75, word * 1.8);
            Glyph last = null;
            foreach (var g in l.Glyphs)
            {
                if (IsSpace(g) || g.Mark) { cells[cells.Count - 1].Add(g); continue; }
                if (last != null && g.X - (last.X + last.W) > split) cells.Add(new List<Glyph>());
                cells[cells.Count - 1].Add(g);
                last = g;
            }
            return cells.Where(c => c.Any(g => !IsSpace(g))).ToList();
        }

        /// A unit of text in visual order with the glyph that gives it its look.
        class Unit { public string T; public Glyph G; }

        /// Glyphs in page order (left to right) → runs in reading order, for a line whose base direction is given.
        static List<Run> Logical(List<Glyph> visual, bool rtl, double baseline = double.NaN)
        {
            // units: glyph texts left to right; spaces come from the gaps (Word also draws stray space glyphs on top of
            // punctuation, so the glyphs themselves are not trusted); accents join the letter they sit on
            var glyphs = visual.OrderBy(x => x.X).ToList();
            // an accent (Arabic harakat, combining marks) belongs to the letter it sits on: the nearest letter by
            // centre, not simply the previous glyph — PDFs often place a mark a hair before its letter
            var bases = glyphs.Where(g => !g.Mark && !IsSpace(g) && g.Text.Any(char.IsLetter)).ToList();   // marks sit on letters, never on punctuation
            if (bases.Count == 0) bases = glyphs.Where(g => !g.Mark && !IsSpace(g)).ToList();
            var attached = new Dictionary<Glyph, string>();
            foreach (var m in glyphs.Where(g => g.Mark))
            {
                if (bases.Count == 0) break;
                double mc = m.X + Math.Max(0, m.W) / 2;
                // the letter under the mark, else the nearest by centre (first one wins a tie) — one pass, no sorting
                Glyph host = null;
                int hostInside = 2; double hostDist = double.MaxValue;
                foreach (var b in bases)
                {
                    int inside = mc >= b.X - 0.5 && mc <= b.X + b.W + 0.5 ? 0 : 1;
                    double dist = Math.Abs(b.X + b.W / 2 - mc);
                    if (inside < hostInside || (inside == hostInside && dist < hostDist)) { host = b; hostInside = inside; hostDist = dist; }
                }
                string had;
                attached[host] = (attached.TryGetValue(host, out had) ? had : "") + m.Text;
            }
            var units = new List<Unit>();
            var spaceMids = glyphs.Where(IsSpace).Select(s => s.X + s.W / 2).OrderBy(x => x).ToArray();
            Glyph last = null;
            foreach (var g in glyphs)
            {
                if (IsSpace(g) || g.Mark) continue;
                if (last != null)
                {
                    double end = last.X + last.W, gap = g.X - end, size = Math.Max(g.Size, last.Size);
                    // a real space glyph in the gap is a space however narrow; otherwise a visible gap makes one
                    if (SpaceIn(spaceMids, end, g.X) ? gap > -size * 0.05 : gap > size * 0.2) units.Add(new Unit { T = " ", G = last });
                }
                string marks;
                units.Add(new Unit { T = g.Text + (attached.TryGetValue(g, out marks) ? marks : ""), G = g });
                last = g;
            }
            List<Unit> ordered;
            if (rtl) ordered = RtlOrder(units);
            else
            {
                // left-to-right line: only the Arabic/Hebrew stretches inside it need turning around
                ordered = new List<Unit>();
                int i = 0;
                while (i < units.Count)
                {
                    if (!IsRtlUnit(units[i].T)) { ordered.Add(units[i]); i++; continue; }
                    int j = i, end = i;
                    while (j < units.Count && (IsRtlUnit(units[j].T) || IsWeak(units[j].T) || IsNumber(units[j].T)))
                    {
                        if (IsRtlUnit(units[j].T)) end = j;
                        j++;
                    }
                    ordered.AddRange(RtlOrder(units.GetRange(i, end - i + 1)));
                    i = end + 1;
                }
            }
            var runs = ordered.Select(u => new Run
            {
                Text = u.T, Font = u.G.Font, Size = Math.Round(u.G.Size * 2) / 2, Bold = u.G.Bold, Italic = u.G.Italic, Color = u.G.Color,
                Rise = double.IsNaN(baseline) || Math.Abs(u.G.Y - baseline) < u.G.Size * 0.12 ? 0 : Math.Round((u.G.Y - baseline) * 2) / 2,
            }).ToList();
            MergeRuns(runs);
            foreach (var r in runs) r.Text = Collapse(r.Text);
            FixPunctuation(runs);
            return runs;
        }

        /// Visual units of a right-to-left stretch → reading order: reverse it, then put Latin words and numbers back
        /// in their own left-to-right order. Brackets need no mirroring: PDFs map them to the logical character.
        static List<Unit> RtlOrder(List<Unit> visual)
        {
            var u = new List<Unit>(visual);
            u.Reverse();
            var result = new List<Unit>();
            int i = 0;
            while (i < u.Count)
            {
                if (!IsLtrUnit(u[i].T)) { result.Add(u[i]); i++; continue; }
                int j = i, end = i;
                while (j < u.Count && (IsLtrUnit(u[j].T) || IsWeak(u[j].T)))
                {
                    if (IsLtrUnit(u[j].T)) end = j;
                    j++;
                }
                var run = u.GetRange(i, end - i + 1);
                run.Reverse();
                result.AddRange(run);
                i = end + 1;
            }
            return result;
        }

        static bool IsRtlUnit(string u) { return u.Any(c => IsRtlChar(c) && !char.IsDigit(c)); }
        static bool IsLtrUnit(string u) { return u.Any(c => char.IsLetterOrDigit(c) && (!IsRtlChar(c) || char.IsDigit(c))) && !IsRtlUnit(u); }
        static bool IsNumber(string u) { return u.Length > 0 && u.All(char.IsDigit); }
        static bool IsWeak(string u) { return u.All(c => !char.IsLetterOrDigit(c)) && !u.Any(c => c == '(' || c == ')' || c == '[' || c == ']'); }
        static string Collapse(string t) { return System.Text.RegularExpressions.Regex.Replace(t, @"[ \t]{2,}", " "); }

        static string Text(List<Run> runs) { return string.Concat(runs.Select(r => r.Text)); }

        static bool SameLook(Run a, Run b)
        {
            return a.Font == b.Font && Math.Abs(a.Size - b.Size) < 0.6 && a.Bold == b.Bold && a.Italic == b.Italic && a.Color == b.Color && Math.Abs(a.Rise - b.Rise) < 0.3;
        }

        /// Joins neighbouring runs that look the same; a lone space takes the look of what is around it.
        static void MergeRuns(List<Run> runs)
        {
            for (int i = 1; i < runs.Count; i++)
            {
                if (runs[i].Text.Trim().Length == 0 && i + 1 < runs.Count && SameLook(runs[i - 1], runs[i + 1])) { runs[i - 1].Text += runs[i].Text; runs.RemoveAt(i); i--; continue; }
                if (SameLook(runs[i - 1], runs[i])) { runs[i - 1].Text += runs[i].Text; runs.RemoveAt(i); i--; }
            }
            for (int i = 0; i < runs.Count; i++) runs[i].Text = Collapse(runs[i].Text);
        }

        /// No space before closing punctuation (Word leaves a visual gap before a sentence-final period in RTL text).
        static void FixPunctuation(List<Run> runs)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                runs[i].Text = System.Text.RegularExpressions.Regex.Replace(runs[i].Text, @" +([.,،؛؟])(?=\s|$)", "$1");
                if (i > 0 && runs[i].Text.Length > 0 && ".,،؛؟".IndexOf(runs[i].Text[0]) >= 0 && (runs[i].Text.Length == 1 || char.IsWhiteSpace(runs[i].Text[1])))
                    runs[i - 1].Text = runs[i - 1].Text.TrimEnd(' ');
            }
        }

        static void TrimRuns(List<Run> runs)
        {
            while (runs.Count > 0 && runs[0].Text.Trim().Length == 0) runs.RemoveAt(0);
            while (runs.Count > 0 && runs[runs.Count - 1].Text.Trim().Length == 0) runs.RemoveAt(runs.Count - 1);
            if (runs.Count > 0) { runs[0].Text = runs[0].Text.TrimStart(); runs[runs.Count - 1].Text = runs[runs.Count - 1].Text.TrimEnd(); }
        }

        static void DropFirstChar(List<Run> runs)
        {
            if (runs.Count == 0) return;
            runs[0].Text = runs[0].Text.Substring(1);
            TrimRuns(runs);
        }

        // ================= output helpers =================

        /// Plain lines per page (cells joined with tabs) for TXT and spreadsheets.
        public static List<string> Lines(List<Block> page)
        {
            return Flatten(page).Where(b => b.Kind != Kind.Image)
                       .Select(b => b.Kind == Kind.Row ? string.Join("\t", b.Cells) : (b.Kind == Kind.Bullet ? "• " + b.Text : b.Text)).ToList();
        }

        /// Blocks with side-by-side columns expanded in reading order (first column, then the next).
        public static List<Block> Flatten(List<Block> blocks)
        {
            var list = new List<Block>();
            foreach (var b in blocks)
            {
                if (b.Kind == Kind.Columns) foreach (var col in b.Columns) list.AddRange(Flatten(col));
                else list.Add(b);
            }
            return list;
        }

        /// Markdown with headings, bullets and pipe tables.
        public static string Markdown(List<Page> pages)
        {
            var parts = new List<string>();
            foreach (var page in pages)
            {
                var blocks = Flatten(page.Blocks).Where(b => b.Kind != Kind.Image).ToList();
                var sb = new StringBuilder();
                for (int i = 0; i < blocks.Count; i++)
                {
                    var b = blocks[i];
                    if (b.Kind == Kind.Row)
                    {
                        int cols = 0;
                        int start = i;
                        for (int k = i; k < blocks.Count && blocks[k].Kind == Kind.Row; k++) cols = Math.Max(cols, blocks[k].Cells.Count);
                        for (; i < blocks.Count && blocks[i].Kind == Kind.Row; i++)
                        {
                            var cells = blocks[i].Cells.Concat(Enumerable.Repeat("", cols - blocks[i].Cells.Count)).Select(c => c.Replace("|", "\\|"));
                            sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");
                            if (i == start) sb.Append("|").Append(string.Concat(Enumerable.Repeat(" --- |", cols))).Append("\n");
                        }
                        i--;
                        sb.Append("\n");
                        continue;
                    }
                    string t = System.Text.RegularExpressions.Regex.Replace(b.Text, @"^([#>*\-+]|\d+\.)", @"\$1");
                    switch (b.Kind)
                    {
                        case Kind.Heading1: sb.Append("# ").Append(t).Append("\n\n"); break;
                        case Kind.Heading2: sb.Append("## ").Append(t).Append("\n\n"); break;
                        case Kind.Heading3: sb.Append("### ").Append(t).Append("\n\n"); break;
                        case Kind.Bullet:
                            sb.Append("- ").Append(t).Append("\n");
                            if (i + 1 >= blocks.Count || blocks[i + 1].Kind != Kind.Bullet) sb.Append("\n");
                            break;
                        default: sb.Append(b.Bold ? "**" + t + "**" : t).Append("\n\n"); break;
                    }
                }
                parts.Add(sb.ToString().TrimEnd());
            }
            return string.Join("\n\n---\n\n", parts) + "\n";
        }
    }
}
