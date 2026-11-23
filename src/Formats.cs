using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Klementina
{
    /// EPUB and Markdown conversions, plus PDF → PPTX/XLSX/EPUB/MD and documents → EPUB/MD.
    static class Formats
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        const RegexOptions RX = RegexOptions.IgnoreCase | RegexOptions.Singleline;

        // ---------------- HTML → Markdown ----------------

        public static string HtmlToMarkdown(string html)
        {
            string s = html;
            s = Regex.Replace(s, @"<(head|script|style|title)[^>]*>.*?</\1>", "", RX);
            s = Regex.Replace(s, @"<!--.*?-->", "", RX);
            s = Regex.Replace(s, @"\r?\n", " ");
            s = Regex.Replace(s, @"<h([1-6])[^>]*>(.*?)</h\1>", m => "\n\n" + new string('#', int.Parse(m.Groups[1].Value)) + " " + m.Groups[2].Value.Trim() + "\n\n", RX);
            s = Regex.Replace(s, @"<br\s*/?>", "  \n", RX);
            s = Regex.Replace(s, @"<li[^>]*>(.*?)</li>", m => "\n- " + m.Groups[1].Value.Trim(), RX);
            s = Regex.Replace(s, @"<(strong|b)(\s[^>]*)?>(.*?)</\1>", m => m.Groups[3].Value.Trim().Length == 0 ? m.Groups[3].Value : "**" + m.Groups[3].Value.Trim() + "**", RX);
            s = Regex.Replace(s, @"<(em|i)(\s[^>]*)?>(.*?)</\1>", m => m.Groups[3].Value.Trim().Length == 0 ? m.Groups[3].Value : "*" + m.Groups[3].Value.Trim() + "*", RX);
            s = Regex.Replace(s, @"<a\s[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", m => m.Groups[1].Value.StartsWith("#") ? m.Groups[2].Value : "[" + m.Groups[2].Value + "](" + m.Groups[1].Value + ")", RX);
            s = Regex.Replace(s, @"</(p|div|ul|ol|table|blockquote|section|article)>", "\n\n", RX);
            s = Regex.Replace(s, @"</tr>", "\n", RX);
            s = Regex.Replace(s, @"</t[dh]>", " | ", RX);
            s = Regex.Replace(s, @"<[^>]+>", "");
            s = WebUtility.HtmlDecode(s).Replace(' ', ' ');
            var lines = s.Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim());
            s = string.Join("\n", lines);
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim() + "\n";
        }

        static string MarkdownToPlain(string md)
        {
            string s = Regex.Replace(md, @"^#{1,6}\s+", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"\*\*(.+?)\*\*", "$1");
            s = Regex.Replace(s, @"\*(.+?)\*", "$1");
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");
            return s.Replace("  \n", "\n");
        }

        static bool MostlyArabic(string s)
        {
            int ar = s.Count(c => c >= '؀' && c <= 'ۿ'), lat = s.Count(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
            return ar > lat;
        }

        // ---------------- EPUB writer ----------------

        public static void WriteEpub(string dest, string title, string markdown)
        {
            bool rtl = MostlyArabic(markdown);
            string lang = rtl ? "ar" : "en";
            string body = TextOps.Markdown(markdown);
            // XHTML needs empty elements closed
            body = body.Replace("<hr>", "<hr/>").Replace("<br>", "<br/>");
            body = Regex.Replace(body, @"<img([^>]*?)\s*/?>", "<img$1/>");
            string t = WebUtility.HtmlEncode(title);
            string dir = rtl ? "rtl" : "ltr";
            string chapter = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"" + lang + "\" lang=\"" + lang + "\" dir=\"" + dir + "\">\n" +
                             "<head><meta charset=\"utf-8\"/><title>" + t + "</title><link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/></head>\n<body>\n" + body + "\n</body>\n</html>\n";
            string nav = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"" + lang + "\" dir=\"" + dir + "\">\n<head><meta charset=\"utf-8\"/><title>" + t + "</title></head>\n" +
                         "<body><nav epub:type=\"toc\" id=\"toc\"><ol><li><a href=\"chapter.xhtml\">" + t + "</a></li></ol></nav></body>\n</html>\n";
            string id = "urn:uuid:" + Guid.NewGuid();
            string ncx = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\"><head><meta name=\"dtb:uid\" content=\"" + id + "\"/></head>" +
                         "<docTitle><text>" + t + "</text></docTitle><navMap><navPoint id=\"p1\" playOrder=\"1\"><navLabel><text>" + t + "</text></navLabel><content src=\"chapter.xhtml\"/></navPoint></navMap></ncx>\n";
            string opf = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\" xml:lang=\"" + lang + "\">\n" +
                         " <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n  <dc:identifier id=\"bookid\">" + id + "</dc:identifier>\n  <dc:title>" + t + "</dc:title>\n  <dc:language>" + lang + "</dc:language>\n" +
                         "  <meta property=\"dcterms:modified\">" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "</meta>\n </metadata>\n" +
                         " <manifest>\n  <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n  <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n" +
                         "  <item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n  <item id=\"ch1\" href=\"chapter.xhtml\" media-type=\"application/xhtml+xml\"/>\n </manifest>\n" +
                         " <spine toc=\"ncx\" page-progression-direction=\"" + dir + "\"><itemref idref=\"ch1\"/></spine>\n</package>\n";
            string css = "body{font-family:serif;line-height:1.6;margin:0 4%}h1,h2,h3{line-height:1.3}pre{white-space:pre-wrap;direction:ltr}blockquote{margin:0 1em;color:#555}table{border-collapse:collapse}td{border:1px solid #bbb;padding:2px 6px}";
            string container = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>\n";

            if (File.Exists(dest)) File.Delete(dest);
            using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
            {
                Action<string, string, CompressionLevel> add = (name, text, level) =>
                {
                    var e = zip.CreateEntry(name, level);
                    using (var w = new StreamWriter(e.Open(), Utf8)) w.Write(text);
                };
                add("mimetype", "application/epub+zip", CompressionLevel.NoCompression); // must be first and uncompressed
                add("META-INF/container.xml", container, CompressionLevel.Optimal);
                add("OEBPS/content.opf", opf, CompressionLevel.Optimal);
                add("OEBPS/nav.xhtml", nav, CompressionLevel.Optimal);
                add("OEBPS/toc.ncx", ncx, CompressionLevel.Optimal);
                add("OEBPS/style.css", css, CompressionLevel.Optimal);
                add("OEBPS/chapter.xhtml", chapter, CompressionLevel.Optimal);
            }
        }

        // ---------------- EPUB reader ----------------

        /// Unpacks the book into workDir and writes one combined HTML page (spine order, images as absolute file links).
        public static string EpubToHtml(string epub, string workDir, out string title)
        {
            ZipFile.ExtractToDirectory(epub, workDir);
            var container = new XmlDocument();
            container.Load(Path.Combine(workDir, "META-INF", "container.xml"));
            var ns = new XmlNamespaceManager(container.NameTable);
            ns.AddNamespace("c", "urn:oasis:names:tc:opendocument:xmlns:container");
            string opfRel = container.SelectSingleNode("//c:rootfile", ns).Attributes["full-path"].Value;
            string opfPath = Path.Combine(workDir, opfRel.Replace('/', '\\'));
            string opfDir = Path.GetDirectoryName(opfPath);
            var opf = new XmlDocument();
            opf.Load(opfPath);
            var ons = new XmlNamespaceManager(opf.NameTable);
            ons.AddNamespace("o", "http://www.idpf.org/2007/opf");
            ons.AddNamespace("dc", "http://purl.org/dc/elements/1.1/");
            var t = opf.SelectSingleNode("//dc:title", ons);
            title = t != null ? t.InnerText : Ops.BaseName(epub);
            var manifest = new Dictionary<string, string>();
            foreach (XmlNode item in opf.SelectNodes("//o:manifest/o:item", ons)) manifest[item.Attributes["id"].Value] = Uri.UnescapeDataString(item.Attributes["href"].Value);

            var sb = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(title) + "</title></head><body>\n");
            bool first = true;
            foreach (XmlNode itemref in opf.SelectNodes("//o:spine/o:itemref", ons))
            {
                string href;
                if (!manifest.TryGetValue(itemref.Attributes["idref"].Value, out href)) continue;
                string file = Path.GetFullPath(Path.Combine(opfDir, href.Replace('/', '\\')));
                if (!File.Exists(file)) continue;
                string xhtml = File.ReadAllText(file);
                var body = Regex.Match(xhtml, @"<body[^>]*>(.*)</body>", RX);
                string inner = body.Success ? body.Groups[1].Value : xhtml;
                string itemDir = Path.GetDirectoryName(file);
                // make image/link paths absolute so they still work from the combined page
                inner = Regex.Replace(inner, @"(src|xlink:href)=""([^""#:]+)""", m =>
                {
                    try { return "src=\"" + new Uri(Path.GetFullPath(Path.Combine(itemDir, Uri.UnescapeDataString(m.Groups[2].Value).Replace('/', '\\')))).AbsoluteUri + "\""; }
                    catch { return m.Value; }
                }, RX);
                inner = Regex.Replace(inner, @"<svg\b.*?</svg>", m => Regex.Replace(m.Value, @".*?<image[^>]*src=""([^""]+)"".*", "<img src=\"$1\">", RX), RX);
                if (!first) sb.Append("<br clear=all style='page-break-before:always'>\n");
                sb.Append(inner).Append('\n');
                first = false;
            }
            sb.Append("</body></html>");
            string html = Path.Combine(opfDir, "klementina-book.html");
            File.WriteAllText(html, sb.ToString(), Utf8);
            return html;
        }

        static string InlineImages(string html)
        {
            return Regex.Replace(html, @"src=""(file:///[^""]+)""", m =>
            {
                try
                {
                    string p = new Uri(m.Groups[1].Value).LocalPath;
                    string ext = Path.GetExtension(p).TrimStart('.').ToLowerInvariant();
                    string mime = ext == "png" ? "image/png" : ext == "gif" ? "image/gif" : ext == "svg" ? "image/svg+xml" : ext == "webp" ? "image/webp" : "image/jpeg";
                    return "src=\"data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(p)) + "\"";
                }
                catch { return m.Value; }
            });
        }

        public static List<string> EpubTo(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            string tmp = Ops.TempDir();
            try
            {
                string title;
                string html = EpubToHtml(src, tmp, out title);
                switch (ext)
                {
                    case "md": File.WriteAllText(dest, HtmlToMarkdown(File.ReadAllText(html)), Utf8); break;
                    case "txt": File.WriteAllText(dest, MarkdownToPlain(HtmlToMarkdown(File.ReadAllText(html))), Utf8); break;
                    case "html": File.WriteAllText(dest, InlineImages(File.ReadAllText(html)), Utf8); break;
                    case "pdf":
                        if (Engines.Edge != null) TextOps.HtmlFileToPdf(html, dest);
                        if (!File.Exists(dest)) OfficeOps.WordSave(html, dest, "pdf", false);
                        break;
                    case "docx": OfficeOps.WordSave(html, dest, "docx", false); break;
                    default: throw new NotSupportedException(ext);
                }
            }
            finally { Ops.TryDelete(tmp); }
            return new List<string> { dest };
        }

        // ---------------- documents / text → EPUB, MD ----------------

        static string DocumentMarkdown(string src)
        {
            string e = Catalog.Ext(src);
            if (e == "md" || e == "markdown") return File.ReadAllText(src);
            if (e == "txt") return string.Join("\n\n", File.ReadAllText(src).Replace("\r\n", "\n").Split(new[] { "\n\n" }, StringSplitOptions.None).Select(p => p.Replace("\n", "  \n")));
            if (e == "html" || e == "htm") return HtmlToMarkdown(File.ReadAllText(src));
            // Word documents: let Word write clean HTML, then convert that
            string tmp = Ops.TempDir();
            try
            {
                string html = Path.Combine(tmp, "doc.html");
                OfficeOps.WordSave(src, html, "html", false);
                return HtmlToMarkdown(File.ReadAllText(html, Encoding.UTF8));
            }
            finally { Ops.TryDelete(tmp); }
        }

        public static List<string> DocTo(string src, string ext)
        {
            string md = DocumentMarkdown(src);
            string dest = Ops.Out(src, ext);
            if (ext == "md") File.WriteAllText(dest, md, Utf8);
            else if (ext == "epub") WriteEpub(dest, Ops.BaseName(src), md);
            else throw new NotSupportedException(ext);
            return new List<string> { dest };
        }

        // ---------------- PDF → MD / EPUB / XLSX / PPTX ----------------

        static string PdfMarkdown(string src)
        {
            var pages = PdfOps.OcrPages(src);
            if (pages.All(p => p.Count == 0)) throw new Exception(S.Ar ? "لم يُعثر على نص في الملف" : "No text found in this PDF");
            return string.Join("\n\n---\n\n", pages.Select(p => string.Join("  \n", p.Select(l => Regex.Replace(l, @"^([#>*\-+]|\d+\.)", @"\$1")))));
        }

        public static List<string> PdfTo(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            switch (ext)
            {
                case "md": File.WriteAllText(dest, PdfMarkdown(src), Utf8); break;
                case "epub": WriteEpub(dest, Ops.BaseName(src), PdfMarkdown(src)); break;
                case "xlsx":
                {
                    var rows = new List<List<string>>();
                    PdfOps.OcrPages(src, rows);
                    string tmp = Ops.TempDir();
                    try
                    {
                        string tsv = Path.Combine(tmp, Ops.BaseName(src) + ".tsv");
                        File.WriteAllLines(tsv, rows.Select(r => string.Join("\t", r.Select(c => c.Replace('\t', ' ')))), Utf8);
                        string made = OfficeOps.Excel(tsv, "xlsx")[0];
                        File.Move(made, dest);
                    }
                    finally { Ops.TryDelete(tmp); }
                    break;
                }
                case "pptx": OfficeOps.PdfToPptx(src, dest); break;
                default: throw new NotSupportedException(ext);
            }
            return new List<string> { dest };
        }
    }
}
