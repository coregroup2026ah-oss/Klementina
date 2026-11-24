using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Klementina
{
    /// Writes a simple .xlsx straight from rows of text, without Excel: numbers become real numbers, the first row
    /// is a bold header (when asked), columns are sized to their contents, and a mostly-Arabic sheet reads right to
    /// left. Used for CSV/TSV → XLSX and PDF → XLSX, where opening the text in Excel garbled Arabic.
    static class XlsxWriter
    {
        public class Sheet
        {
            public string Name;
            public List<List<string>> Rows = new List<List<string>>();
            public bool Header = true;
        }

        public static void Write(string dest, List<Sheet> sheets)
        {
            if (sheets.Count == 0) sheets.Add(new Sheet { Name = "Sheet1" });
            if (File.Exists(dest)) File.Delete(dest);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
            {
                var wbSheets = new StringBuilder();
                var wbRels = new StringBuilder();
                var ct = new StringBuilder();
                for (int i = 0; i < sheets.Count; i++)
                {
                    string name = SafeName(sheets[i].Name, i, names);
                    wbSheets.Append("<sheet name=\"" + X(name) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>");
                    wbRels.Append("<Relationship Id=\"rId" + (i + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + (i + 1) + ".xml\"/>");
                    ct.Append("<Override PartName=\"/xl/worksheets/sheet" + (i + 1) + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                    Add(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", SheetXml(sheets[i], i == 0));
                }
                int n = sheets.Count;
                wbRels.Append("<Relationship Id=\"rId" + (n + 1) + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" + ct + "</Types>");
                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<sheets>" + wbSheets + "</sheets></workbook>");
                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + wbRels + "</Relationships>");
                Add(zip, "xl/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Arial\"/></font><font><b/><sz val=\"11\"/><name val=\"Arial\"/></font></fonts>" +
                    "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                    "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFBEFE4\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                    "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
                    "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FFE0C8B4\"/></bottom><diagonal/></border></borders>" +
                    "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
                    "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +
                    "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf></cellXfs>" +
                    "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            }
        }

        static string SheetXml(Sheet sh, bool selected)
        {
            var rows = sh.Rows;
            int cols = rows.Count == 0 ? 1 : Math.Max(1, rows.Max(r => r.Count));
            string all = string.Concat(rows.SelectMany(r => r));
            int ar = all.Count(c => (c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFEFF));
            int lat = all.Count(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
            bool rtl = ar > lat;

            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"" + (rtl ? " rightToLeft=\"1\"" : "") + (selected ? " tabSelected=\"1\"" : "") + ">");
            if (sh.Header && rows.Count > 1) sb.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.Append("</sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"15\"/><cols>");
            for (int c = 0; c < cols; c++)
            {
                int len = rows.Select(r => c < r.Count && r[c] != null ? r[c].Split('\n').Max(x => x.Length) : 0).DefaultIfEmpty(0).Max();
                double w = Math.Max(8, Math.Min(60, len * 1.15 + 2));
                sb.Append("<col min=\"" + (c + 1) + "\" max=\"" + (c + 1) + "\" width=\"" + w.ToString("0.#", CultureInfo.InvariantCulture) + "\" customWidth=\"1\"/>");
            }
            sb.Append("</cols><sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("<row r=\"" + (r + 1) + "\">");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    string v = rows[r][c] ?? "";
                    if (v.Length == 0) continue;
                    string refc = Col(c) + (r + 1);
                    int style = sh.Header && r == 0 ? 1 : v.Contains('\n') ? 2 : 0;
                    double num;
                    if (!(sh.Header && r == 0) && IsNumber(v, out num))
                        sb.Append("<c r=\"" + refc + "\"" + (style != 0 ? " s=\"" + style + "\"" : "") + "><v>" + num.ToString("R", CultureInfo.InvariantCulture) + "</v></c>");
                    else
                        sb.Append("<c r=\"" + refc + "\" t=\"inlineStr\"" + (style != 0 ? " s=\"" + style + "\"" : "") + "><is><t xml:space=\"preserve\">" + X(v) + "</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        /// Plain numbers only ("95", "-3.5", "1,250.75"): codes like "0599…" or "1101-2" stay text.
        static bool IsNumber(string s, out double v)
        {
            v = 0;
            string t = s.Trim();
            if (t.Length == 0 || t.Length > 15) return false;
            if (t.Length > 1 && t[0] == '0' && char.IsDigit(t[1])) return false;   // leading zero: phone, code
            if (!t.All(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-')) return false;
            if (t.IndexOf('-') > 0) return false;
            return double.TryParse(t.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        static string Col(int c)
        {
            string s = "";
            for (c++; c > 0; c = (c - 1) / 26) s = (char)('A' + (c - 1) % 26) + s;
            return s;
        }

        static string SafeName(string name, int i, HashSet<string> used)
        {
            string n = string.IsNullOrWhiteSpace(name) ? "Sheet" + (i + 1) : name;
            foreach (char bad in new[] { '\\', '/', '?', '*', '[', ']', ':' }) n = n.Replace(bad, ' ');
            n = n.Trim();
            if (n.Length > 31) n = n.Substring(0, 31);
            string b = n; int k = 2;
            while (!used.Add(n)) { string suffix = " (" + k++ + ")"; n = (b.Length + suffix.Length > 31 ? b.Substring(0, 31 - suffix.Length) : b) + suffix; }
            return n;
        }

        static void Add(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
        }

        static string X(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '&') sb.Append("&amp;");
                else if (c == '<') sb.Append("&lt;");
                else if (c == '>') sb.Append("&gt;");
                else if (c == '"') sb.Append("&quot;");
                else if (c < 0x20 && c != '\t' && c != '\n') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
