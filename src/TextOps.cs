using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Klementina
{
    static class TextOps
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        const string Css =
            "body{font-family:'Segoe UI',Tahoma,sans-serif;max-width:820px;margin:40px auto;padding:0 20px;line-height:1.7;color:#222}" +
            "p,li,h1,h2,h3,h4,h5,h6,blockquote{unicode-bidi:plaintext;text-align:start}" +
            "pre{background:#f6f3f0;padding:12px 14px;border-radius:8px;overflow:auto;direction:ltr}" +
            "code{font-family:Consolas,monospace;background:#f6f3f0;padding:1px 4px;border-radius:4px}pre code{padding:0;background:none}" +
            "blockquote{border-inline-start:4px solid #ff8a2a;margin:0;padding:4px 14px;color:#555}" +
            "table{border-collapse:collapse}td,th{border:1px solid #ddd;padding:4px 10px;unicode-bidi:plaintext}th{background:#fbefe4}" +
            "img{max-width:100%}hr{border:0;border-top:1px solid #ddd}a{color:#d9600f}";

        static string Page(string title, string body)
        {
            return "<!doctype html>\n<html dir=\"auto\"><head><meta charset=\"utf-8\"><title>" + WebUtility.HtmlEncode(title) +
                   "</title><style>" + Css + "</style></head><body>\n" + body + "\n</body></html>\n";
        }

        static string RenderHtml(string src)
        {
            string text = File.ReadAllText(src);
            string ext = Catalog.Ext(src);
            if (ext == "html" || ext == "htm") return text;
            string body = ext == "md" || ext == "markdown" ? Markdown(text)
                : string.Join("\n", text.Replace("\r\n", "\n").Split('\n').Select(l =>
                    "<p" + Dir(l) + " style=\"margin:0;white-space:pre-wrap\">" + (l.Length == 0 ? "&nbsp;" : WebUtility.HtmlEncode(l)) + "</p>"));
            return Page(Ops.BaseName(src), body);
        }

        public static List<string> ToHtml(string src, string ext)
        {
            string dest = Ops.Out(src, "html");
            File.WriteAllText(dest, RenderHtml(src), Utf8);
            return new List<string> { dest };
        }

        public static List<string> ToPdf(string src, string ext)
        {
            string dest = Ops.Out(src, "pdf");
            string tmpDir = Ops.TempDir();
            try
            {
                string html = Path.Combine(tmpDir, "page.html");
                File.WriteAllText(html, RenderHtml(src), Utf8);
                if (Engines.Edge != null)
                {
                    string log;
                    Ops.Run(Engines.Edge, "--headless --disable-gpu --no-pdf-header-footer --user-data-dir=" + Ops.Q(Path.Combine(tmpDir, "profile")) +
                        " --print-to-pdf=" + Ops.Q(dest) + " " + Ops.Q(new Uri(html).AbsoluteUri), out log);
                }
                if (!File.Exists(dest) && Engines.Word) OfficeOps.WordSave(html, dest, "pdf", false);
                if (!File.Exists(dest)) throw new Exception("PDF needs Microsoft Edge or Word");
            }
            finally { Ops.TryDelete(tmpDir); }
            return new List<string> { dest };
        }

        /// Prints a local HTML file to PDF with headless Edge/Chrome (no-op if neither is installed).
        public static void HtmlFileToPdf(string html, string dest)
        {
            if (Engines.Edge == null) return;
            string profile = Ops.TempDir();
            try
            {
                string log;
                Ops.Run(Engines.Edge, "--headless --disable-gpu --no-pdf-header-footer --user-data-dir=" + Ops.Q(profile) +
                    " --print-to-pdf=" + Ops.Q(dest) + " " + Ops.Q(new Uri(html).AbsoluteUri), out log);
            }
            finally { Ops.TryDelete(profile); }
        }

        public static List<string> ViaWord(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            string e = Catalog.Ext(src);
            if (e == "md" || e == "markdown")
            {
                string tmpDir = Ops.TempDir();
                try
                {
                    string html = Path.Combine(tmpDir, Ops.BaseName(src) + ".html");
                    File.WriteAllText(html, RenderHtml(src), Utf8);
                    OfficeOps.WordSave(html, dest, ext, false);
                }
                finally { Ops.TryDelete(tmpDir); }
            }
            else OfficeOps.WordSave(src, dest, ext, e == "txt");
            return new List<string> { dest };
        }

        // ---------- Markdown (common subset) ----------

        public static string Markdown(string md)
        {
            var lines = md.Replace("\r\n", "\n").Split('\n');
            var o = new StringBuilder();
            var para = new List<string>();
            string listType = null;
            bool inCode = false;

            Action flushPara = () =>
            {
                if (para.Count > 0) { string t = string.Join(" ", para); o.Append("<p" + Dir(t) + ">").Append(Inline(t)).Append("</p>\n"); para.Clear(); }
            };
            Action closeList = () => { if (listType != null) { o.Append("</" + listType + ">\n"); listType = null; } };

            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.TrimStart().StartsWith("```"))
                {
                    flushPara(); closeList();
                    o.Append(inCode ? "</code></pre>\n" : "<pre><code>");
                    inCode = !inCode;
                    continue;
                }
                if (inCode) { o.Append(WebUtility.HtmlEncode(raw)).Append('\n'); continue; }

                if (line.Trim().Length == 0) { flushPara(); closeList(); continue; }

                var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                if (h.Success)
                {
                    flushPara(); closeList();
                    int lvl = h.Groups[1].Length;
                    string ht = h.Groups[2].Value.TrimEnd('#', ' ');
                    o.Append("<h" + lvl + Dir(ht) + ">").Append(Inline(ht)).Append("</h" + lvl + ">\n");
                    continue;
                }
                if (Regex.IsMatch(line, @"^\s*([-*_])(\s*\1){2,}\s*$")) { flushPara(); closeList(); o.Append("<hr>\n"); continue; }
                if (line.StartsWith(">"))
                {
                    flushPara(); closeList();
                    string qt = line.TrimStart('>', ' ');
                    o.Append("<blockquote" + Dir(qt) + "><p>").Append(Inline(qt)).Append("</p></blockquote>\n");
                    continue;
                }
                var ul = Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
                var ol = Regex.Match(line, @"^\s*\d+[.)]\s+(.*)$");
                if (ul.Success || ol.Success)
                {
                    flushPara();
                    string type = ul.Success ? "ul" : "ol";
                    if (listType != type) { closeList(); o.Append("<" + type + ">\n"); listType = type; }
                    string lt = (ul.Success ? ul : ol).Groups[1].Value;
                    o.Append("<li" + Dir(lt) + ">").Append(Inline(lt)).Append("</li>\n");
                    continue;
                }
                if (line.Contains("|") && line.Trim().StartsWith("|"))
                {
                    flushPara(); closeList();
                    if (Regex.IsMatch(line, @"^\s*\|?[\s:\-|]+\|?\s*$")) continue; // separator row
                    var cells = line.Trim().Trim('|').Split('|');
                    o.Append("<table><tr>");
                    foreach (var c in cells) o.Append("<td>").Append(Inline(c.Trim())).Append("</td>");
                    o.Append("</tr></table>\n");
                    continue;
                }
                closeList();
                para.Add(line.Trim());
            }
            flushPara(); closeList();
            if (inCode) o.Append("</code></pre>\n");
            // merge consecutive single-row tables
            return o.ToString().Replace("</tr></table>\n<table><tr>", "</tr>\n<tr>");
        }

        /// dir="rtl" when the first letter is Arabic/Hebrew (Word ignores CSS unicode-bidi).
        static string Dir(string text)
        {
            foreach (char c in text)
            {
                if ((c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= 'ﻼ')) return " dir=\"rtl\"";
                if (char.IsLetter(c)) return "";
            }
            return "";
        }

        static string Inline(string s)
        {
            s = WebUtility.HtmlEncode(s);
            s = Regex.Replace(s, @"`([^`]+)`", "<code>$1</code>");
            s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)\s]+)\)", "<img alt=\"$1\" src=\"$2\">");
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)\)", "<a href=\"$2\">$1</a>");
            s = Regex.Replace(s, @"\*\*(.+?)\*\*|__(.+?)__", "<strong>$1$2</strong>");
            s = Regex.Replace(s, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])|(?<!\w)_(?!\s)(.+?)(?<!\s)_(?!\w)", "<em>$1$2</em>");
            s = Regex.Replace(s, @"~~(.+?)~~", "<del>$1</del>");
            return s;
        }

        // ---------- CSV / TSV ----------

        public static List<List<string>> ParseDelimited(string text, char sep)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"' && cell.Length == 0) quoted = true;
                else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            return rows;
        }

        static char SepOf(string path) { return Catalog.Ext(path) == "tsv" ? '\t' : DetectCsvSep(path); }

        static char DetectCsvSep(string path)
        {
            string first = File.ReadLines(path).FirstOrDefault() ?? "";
            return first.Count(ch => ch == ';') > first.Count(ch => ch == ',') ? ';' : ',';
        }

        public static List<string> CsvTsv(string src, string ext)
        {
            var rows = ParseDelimited(File.ReadAllText(src), SepOf(src));
            string dest = Ops.Out(src, ext);
            var sb = new StringBuilder();
            foreach (var r in rows)
            {
                if (ext == "tsv")
                    sb.Append(string.Join("\t", r.Select(c => c.Replace('\t', ' ').Replace("\r", " ").Replace("\n", " "))));
                else
                    sb.Append(string.Join(",", r.Select(c => c.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + c.Replace("\"", "\"\"") + "\"" : c)));
                sb.Append("\r\n");
            }
            File.WriteAllText(dest, sb.ToString(), new UTF8Encoding(ext == "csv")); // BOM helps Excel read UTF-8 CSV
            return new List<string> { dest };
        }

        public static List<string> CsvToHtml(string src, string ext)
        {
            var rows = ParseDelimited(File.ReadAllText(src), SepOf(src));
            var sb = new StringBuilder("<table>\n");
            for (int i = 0; i < rows.Count; i++)
            {
                string tag = i == 0 ? "th" : "td";
                sb.Append("<tr>");
                foreach (var c in rows[i]) sb.Append("<" + tag + ">").Append(WebUtility.HtmlEncode(c)).Append("</" + tag + ">");
                sb.Append("</tr>\n");
            }
            sb.Append("</table>");
            string dest = Ops.Out(src, "html");
            File.WriteAllText(dest, Page(Ops.BaseName(src), sb.ToString()), Utf8);
            return new List<string> { dest };
        }

        // ---------- Subtitles ----------

        class Cue { public string Start, End; public List<string> Lines = new List<string>(); }

        static List<Cue> ParseCues(string text)
        {
            var cues = new List<Cue>();
            var blocks = Regex.Split(text.Replace("\r\n", "\n").Trim(), @"\n\s*\n");
            foreach (var b in blocks)
            {
                var lines = b.Split('\n');
                int t = Array.FindIndex(lines, l => l.Contains("-->"));
                if (t < 0) continue;
                var m = Regex.Match(lines[t], @"([\d:.,]+)\s*-->\s*([\d:.,]+)");
                if (!m.Success) continue;
                var cue = new Cue { Start = NormTime(m.Groups[1].Value), End = NormTime(m.Groups[2].Value) };
                for (int i = t + 1; i < lines.Length; i++) if (lines[i].Trim().Length > 0) cue.Lines.Add(lines[i].TrimEnd());
                cues.Add(cue);
            }
            return cues;
        }

        /// → "hh:mm:ss.mmm"
        static string NormTime(string t)
        {
            t = t.Replace(',', '.');
            var parts = t.Split(':');
            string h = parts.Length == 3 ? parts[0] : "0", m = parts[parts.Length - 2], s = parts[parts.Length - 1];
            if (!s.Contains(".")) s += ".000";
            var sp = s.Split('.');
            return int.Parse(h).ToString("00") + ":" + int.Parse(m).ToString("00") + ":" + int.Parse(sp[0]).ToString("00") + "." + sp[1].PadRight(3, '0').Substring(0, 3);
        }

        public static List<string> Subtitles(string src, string ext)
        {
            var cues = ParseCues(File.ReadAllText(src));
            var sb = new StringBuilder();
            if (ext == "vtt") sb.Append("WEBVTT\n\n");
            for (int i = 0; i < cues.Count; i++)
            {
                var c = cues[i];
                if (ext == "txt") { sb.Append(string.Join("\n", c.Lines)).Append("\n"); continue; }
                if (ext == "srt") sb.Append(i + 1).Append('\n').Append(c.Start.Replace('.', ',')).Append(" --> ").Append(c.End.Replace('.', ','));
                else sb.Append(c.Start).Append(" --> ").Append(c.End);
                sb.Append('\n').Append(string.Join("\n", c.Lines)).Append("\n\n");
            }
            string dest = Ops.Out(src, ext);
            File.WriteAllText(dest, sb.ToString().Replace("\n", "\r\n"), Utf8);
            return new List<string> { dest };
        }
    }

    static class ArchiveOps
    {
        static void Tar(string args, string workDir = null)
        {
            string log;
            if (Ops.Run("tar.exe", args, out log, workDir) != 0)
            {
                var lines = log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                throw new Exception(lines.Length > 0 ? lines[lines.Length - 1] : "tar failed");
            }
        }

        public static string Extract(string src)
        {
            string dest = Ops.Unique(Ops.DirOf(src), Ops.BaseName(src), null);
            Directory.CreateDirectory(dest);
            try
            {
                if (Catalog.Ext(src) == "zip") ZipFile.ExtractToDirectory(src, dest);
                else Tar("-xf " + Ops.Q(src) + " -C " + Ops.Q(dest));
            }
            catch { Ops.TryDelete(dest); throw; }
            return dest;
        }

        static string Pack(string folder, string dest, string ext)
        {
            if (ext == "zip") ZipFile.CreateFromDirectory(folder, dest, CompressionLevel.Optimal, false);
            else Tar((ext == "gz" ? "-czf " : "-cf ") + Ops.Q(dest) + " .", folder);
            return dest;
        }

        static string DestFor(string src, string ext)
        {
            return ext == "gz" ? Ops.Unique(Ops.DirOf(src), Ops.BaseName(src), "tar.gz") : Ops.Out(src, ext);
        }

        public static List<string> Repack(string src, string ext)
        {
            string tmp = Ops.TempDir();
            try
            {
                if (Catalog.Ext(src) == "zip") ZipFile.ExtractToDirectory(src, tmp);
                else Tar("-xf " + Ops.Q(src) + " -C " + Ops.Q(tmp));
                return new List<string> { Pack(tmp, DestFor(src, ext), ext) };
            }
            finally { Ops.TryDelete(tmp); }
        }

        public static List<string> PackFolder(string src, string ext)
        {
            string dest = DestFor(src, ext);
            if (ext == "zip") ZipFile.CreateFromDirectory(src, dest, CompressionLevel.Optimal, true);
            else Tar((ext == "gz" ? "-czf " : "-cf ") + Ops.Q(dest) + " " + Ops.Q(Path.GetFileName(src.TrimEnd('\\'))), Ops.DirOf(src));
            return new List<string> { dest };
        }

        public static OpResult ZipAll(List<string> files)
        {
            var r = new OpResult();
            string dest = files.Count == 1 ? DestFor(files[0], "zip")
                : Ops.Unique(Ops.DirOf(files[0]), S.Ar ? "ملفات" : "Archive", "zip");
            using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
            {
                foreach (var f in files)
                {
                    if (File.Exists(f)) zip.CreateEntryFromFile(f, Path.GetFileName(f), CompressionLevel.Optimal);
                    else if (Directory.Exists(f))
                    {
                        string root = Ops.DirOf(f);
                        foreach (var file in Directory.EnumerateFiles(f, "*", SearchOption.AllDirectories))
                            zip.CreateEntryFromFile(file, file.Substring(root.Length).TrimStart('\\').Replace('\\', '/'), CompressionLevel.Optimal);
                    }
                }
            }
            r.Outputs.Add(dest);
            r.Message = Path.GetFileName(dest) + "  ·  " + Ops.Savings(files, r.Outputs);
            return r;
        }
    }

    static class MiscOps
    {
        public static OpResult Hash(List<string> files)
        {
            var r = new OpResult();
            var sb = new StringBuilder();
            using (var sha = SHA256.Create())
            {
                foreach (var f in files)
                {
                    using (var fs = File.OpenRead(f))
                    {
                        string h = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                        if (files.Count == 1) sb.Append(h);
                        else sb.Append(h).Append("  ").Append(Path.GetFileName(f)).Append("\r\n");
                    }
                }
            }
            r.ClipboardText = sb.ToString();
            r.Message = S.T("hash_copied") + (files.Count == 1 ? "\n" + r.ClipboardText.Substring(0, 16) + "…" : "");
            return r;
        }
    }
}
