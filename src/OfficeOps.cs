using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Klementina
{
    /// Word / Excel / PowerPoint through COM automation (runs on an STA worker thread).
    static class OfficeOps
    {
        static readonly object officeLock = new object();

        /// Starts the Office app and arms a watchdog that kills the instance we started if it hangs
        /// (e.g. on a modal prompt that can't be seen because the app is hidden).
        static dynamic Create(string progId, string processName, out Timer watchdog)
        {
            Type t = Type.GetTypeFromProgID(progId);
            if (t == null) throw new Exception(S.T("need_office"));
            var before = new HashSet<int>(Process.GetProcessesByName(processName).Select(p => p.Id));
            dynamic app = null;
            for (int attempt = 1; app == null; attempt++)
            {
                try { app = Activator.CreateInstance(t); }
                catch (COMException) // the previous instance may still be shutting down ("RPC server is unavailable")
                {
                    if (attempt >= 4) throw;
                    Thread.Sleep(1500);
                }
            }
            var mine = Process.GetProcessesByName(processName).Where(p => !before.Contains(p.Id)).ToList();
            watchdog = new Timer(_ =>
            {
                foreach (var p in mine) { try { if (!p.HasExited) p.Kill(); } catch { } }
            }, null, 180000, Timeout.Infinite);
            lock (owned) owned[watchdog] = mine;
            return app;
        }

        static readonly Dictionary<Timer, List<Process>> owned = new Dictionary<Timer, List<Process>>();

        /// After Quit: drop lingering COM references and make sure the instance we started really exits
        /// (Excel in particular stays alive while any hidden reference remains).
        static void Shutdown(Timer watchdog)
        {
            watchdog.Dispose();
            List<Process> mine;
            lock (owned) { owned.TryGetValue(watchdog, out mine); owned.Remove(watchdog); }
            GC.Collect(); // no WaitForPendingFinalizers: it can deadlock on an STA thread without a message loop
            if (mine == null) return;
            foreach (var p in mine)
            {
                try { if (!p.WaitForExit(5000)) p.Kill(); } catch { }
            }
        }

        static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
        }

        public static List<string> Word(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            WordSave(src, dest, ext, Catalog.Ext(src) == "txt");
            return new List<string> { dest };
        }

        public static void WordSave(string src, string dest, string ext, bool utf8Text)
        {
            lock (officeLock)
            {
                Timer dog; dynamic app = Create("Word.Application", "WINWORD", out dog);
                dynamic doc = null;
                Action restorePdfWarning = null;
                try
                {
                    app.Visible = false;
                    app.DisplayAlerts = 0;
                    if (Catalog.Ext(src) == "pdf") restorePdfWarning = SuppressPdfWarning((string)app.Version);
                    if (utf8Text)
                        doc = app.Documents.Open(FileName: src, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Encoding: 65001, Visible: false);
                    else
                        doc = app.Documents.Open(FileName: src, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);

                    switch (ext)
                    {
                        case "pdf": doc.ExportAsFixedFormat(dest, 17); break;
                        case "docx": doc.SaveAs2(FileName: dest, FileFormat: 16); break;
                        case "doc": doc.SaveAs2(FileName: dest, FileFormat: 0); break;
                        case "rtf": doc.SaveAs2(FileName: dest, FileFormat: 6); break;
                        case "odt": doc.SaveAs2(FileName: dest, FileFormat: 23); break;
                        case "html": doc.SaveAs2(FileName: dest, FileFormat: 10, Encoding: 65001); break;
                        case "txt": doc.SaveAs2(FileName: dest, FileFormat: 2, Encoding: 65001); break;
                        default: throw new NotSupportedException(ext);
                    }
                }
                finally
                {
                    if (doc != null) { try { doc.Close(0); } catch { } Release((object)doc); }
                    try { app.Quit(0); } catch { }
                    Release((object)app);
                    Shutdown(dog);
                    if (restorePdfWarning != null) restorePdfWarning();
                }
            }
            if (!File.Exists(dest)) throw new Exception("Word did not create " + Path.GetFileName(dest));
        }

        /// Word shows a modal "will now convert your PDF" prompt even when hidden; turn it off for this
        /// conversion only and put the user's setting back afterwards.
        static Action SuppressPdfWarning(string version)
        {
            string keyPath = @"Software\Microsoft\Office\" + version + @"\Word\Options";
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    object old = k.GetValue("DisableConvertPdfWarning");
                    k.SetValue("DisableConvertPdfWarning", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    return () =>
                    {
                        try
                        {
                            using (var k2 = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(keyPath))
                            {
                                if (old == null) k2.DeleteValue("DisableConvertPdfWarning", false);
                                else k2.SetValue("DisableConvertPdfWarning", old, Microsoft.Win32.RegistryValueKind.DWord);
                            }
                        }
                        catch { }
                    };
                }
            }
            catch { return null; }
        }

        public static List<string> Excel(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            string srcExt = Catalog.Ext(src);
            string tsvTmp = null;
            string viaHtml = null; // PDF fallback when Excel can't export (it needs a printer installed)
            lock (officeLock)
            {
                Timer dog; dynamic app = Create("Excel.Application", "EXCEL", out dog);
                dynamic wb = null;
                try
                {
                    app.Visible = false;
                    app.DisplayAlerts = false;
                    if (srcExt == "csv" || srcExt == "tsv")
                    {
                        bool tab = srcExt == "tsv";
                        app.Workbooks.OpenText(Filename: src, Origin: 65001, DataType: 1, TextQualifier: 1, Tab: tab, Comma: !tab, Local: false);
                        wb = app.ActiveWorkbook;
                    }
                    else wb = app.Workbooks.Open(Filename: src, UpdateLinks: 0, ReadOnly: true);

                    switch (ext)
                    {
                        case "pdf":
                            try { wb.ExportAsFixedFormat(0, dest); }
                            catch
                            {
                                viaHtml = Path.Combine(Ops.TempDir(), Ops.BaseName(src) + ".htm");
                                wb.SaveAs(Filename: viaHtml, FileFormat: 44);
                            }
                            break;
                        case "xlsx": wb.SaveAs(Filename: dest, FileFormat: 51); break;
                        case "ods": wb.SaveAs(Filename: dest, FileFormat: 60); break;
                        case "html": wb.SaveAs(Filename: dest, FileFormat: 44); break;
                        case "csv": wb.SaveAs(Filename: dest, FileFormat: 62); break;
                        case "tsv":
                            tsvTmp = Path.Combine(Ops.TempDir(), "sheet.txt");
                            wb.SaveAs(Filename: tsvTmp, FileFormat: 42); // UTF-16 tab-separated; re-encoded once Excel lets go
                            break;
                        default: throw new NotSupportedException(ext);
                    }
                }
                finally
                {
                    if (wb != null) { try { wb.Close(false); } catch { } Release((object)wb); }
                    try { app.Quit(); } catch { }
                    Release((object)app);
                    Shutdown(dog);
                }
            }
            if (tsvTmp != null)
            {
                try { File.WriteAllText(dest, File.ReadAllText(tsvTmp, Encoding.Unicode), new UTF8Encoding(false)); }
                finally { Ops.TryDelete(Path.GetDirectoryName(tsvTmp)); }
            }
            if (viaHtml != null)
            {
                try { WordSave(viaHtml, dest, "pdf", false); }
                finally { Ops.TryDelete(Path.GetDirectoryName(viaHtml)); }
            }
            if (!File.Exists(dest)) throw new Exception("Excel did not create " + Path.GetFileName(dest));
            return new List<string> { dest };
        }

        /// PDF → PowerPoint: each page becomes a full-slide picture, slides sized like the pages.
        public static void PdfToPptx(string src, string dest)
        {
            string tmp = Ops.TempDir();
            try
            {
                var pages = PdfOps.RenderPagesToFiles(src, tmp, 150);
                if (pages.Count == 0) throw new Exception("PDF has no pages");
                float w = Math.Max(72, Math.Min(4032, pages[0].Value.Width)), h = Math.Max(72, Math.Min(4032, pages[0].Value.Height));
                lock (officeLock)
                {
                    Timer dog; dynamic app = Create("PowerPoint.Application", "POWERPNT", out dog);
                    dynamic pres = null;
                    try
                    {
                        pres = app.Presentations.Add(0); // no window
                        pres.PageSetup.SlideWidth = w;
                        pres.PageSetup.SlideHeight = h;
                        for (int i = 0; i < pages.Count; i++)
                        {
                            dynamic slide = pres.Slides.Add(i + 1, 12); // ppLayoutBlank
                            slide.Shapes.AddPicture(pages[i].Key, 0, -1, 0, 0, w, h);
                            Release((object)slide);
                        }
                        pres.SaveAs(dest, 24);
                    }
                    finally
                    {
                        if (pres != null) { try { pres.Close(); } catch { } Release((object)pres); }
                        try { if (app.Presentations.Count == 0) app.Quit(); } catch { }
                        Release((object)app);
                        Shutdown(dog);
                    }
                }
            }
            finally { Ops.TryDelete(tmp); }
            if (!File.Exists(dest)) throw new Exception("PowerPoint did not create " + Path.GetFileName(dest));
        }

        public static List<string> PowerPoint(string src, string ext)
        {
            bool images = ext == "png" || ext == "jpg";
            string dest = images ? Ops.Unique(Ops.DirOf(src), Ops.BaseName(src) + " (" + Ops.Suffix("slides") + ")", null) : Ops.Out(src, ext);
            lock (officeLock)
            {
                Timer dog; dynamic app = Create("PowerPoint.Application", "POWERPNT", out dog);
                dynamic pres = null;
                try
                {
                    pres = app.Presentations.Open(src, -1, 0, 0); // ReadOnly, Untitled=false, WithWindow=false
                    switch (ext)
                    {
                        case "pdf": pres.SaveAs(dest, 32); break;
                        case "pptx": pres.SaveAs(dest, 24); break;
                        case "png": pres.SaveAs(dest, 18); break;
                        case "jpg": pres.SaveAs(dest, 17); break;
                        default: throw new NotSupportedException(ext);
                    }
                }
                finally
                {
                    if (pres != null) { try { pres.Close(); } catch { } Release((object)pres); }
                    // PowerPoint is single-instance: only quit if the user has nothing else open.
                    try { if (app.Presentations.Count == 0) app.Quit(); } catch { }
                    Release((object)app);
                    Shutdown(dog);
                }
            }
            if (!File.Exists(dest) && !Directory.Exists(dest)) throw new Exception("PowerPoint did not create " + Path.GetFileName(dest));
            return new List<string> { dest };
        }
    }

    /// PDF pages → images using the PDF renderer built into Windows.
    static class PdfOps
    {
        static R Wait<R>(IAsyncOperation<R> op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(5);
            if (op.Status != AsyncStatus.Completed) throw op.ErrorCode ?? new Exception("PDF: " + op.Status);
            return op.GetResults();
        }

        static void Wait(IAsyncAction op)
        {
            while (op.Status == AsyncStatus.Started) Thread.Sleep(5);
            if (op.Status != AsyncStatus.Completed) throw op.ErrorCode ?? new Exception("PDF: " + op.Status);
        }

        /// Renders pages one by one, handing each finished PNG to onPage(index, file); stops early when cancelled() is true.
        public static void RenderPagesProgressively(string src, string dir, int dpi, Action<int, string> onPage, Func<bool> cancelled)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
            PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
            for (uint i = 0; i < doc.PageCount && !cancelled(); i++)
            {
                using (PdfPage page = doc.GetPage(i))
                {
                    var opt = new PdfPageRenderOptions();
                    opt.DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96.0));
                    var ms = new InMemoryRandomAccessStream();
                    Wait(page.RenderToStreamAsync(ms, opt));
                    var reader = new DataReader(ms.GetInputStreamAt(0));
                    uint len = Wait(reader.LoadAsync((uint)ms.Size));
                    var png = new byte[len];
                    reader.ReadBytes(png);
                    reader.Dispose(); ms.Dispose();
                    string f = Path.Combine(dir, "thumb" + (i + 1).ToString("D5") + ".png");
                    File.WriteAllBytes(f, png);
                    onPage((int)i, f);
                }
            }
        }

        /// Writes every page as a PNG at the given resolution; returns the files and each page's size in points.
        public static List<KeyValuePair<string, System.Drawing.SizeF>> RenderPagesToFiles(string src, string dir, int dpi)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
            PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
            var res = new List<KeyValuePair<string, System.Drawing.SizeF>>();
            for (uint i = 0; i < doc.PageCount; i++)
            {
                using (PdfPage page = doc.GetPage(i))
                {
                    var opt = new PdfPageRenderOptions();
                    opt.DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96.0));
                    var ms = new InMemoryRandomAccessStream();
                    Wait(page.RenderToStreamAsync(ms, opt));
                    var reader = new DataReader(ms.GetInputStreamAt(0));
                    uint len = Wait(reader.LoadAsync((uint)ms.Size));
                    var png = new byte[len];
                    reader.ReadBytes(png);
                    reader.Dispose(); ms.Dispose();
                    string f = Path.Combine(dir, "page" + (i + 1).ToString("D4") + ".png");
                    File.WriteAllBytes(f, png);
                    res.Add(new KeyValuePair<string, System.Drawing.SizeF>(f, new System.Drawing.SizeF((float)(page.Size.Width * 0.75), (float)(page.Size.Height * 0.75))));
                }
            }
            return res;
        }

        /// Renders one page as PNG bytes at ~300 DPI.
        static InMemoryRandomAccessStream RenderPage(PdfDocument doc, uint i)
        {
            using (PdfPage page = doc.GetPage(i))
            {
                var opt = new PdfPageRenderOptions();
                double w = page.Size.Width * 300 / 96.0, h = page.Size.Height * 300 / 96.0;
                double fit = Math.Min(1.0, (OcrEngine.MaxImageDimension - 1) / Math.Max(w, h));
                opt.DestinationWidth = (uint)Math.Round(w * fit);
                var ms = new InMemoryRandomAccessStream();
                Wait(page.RenderToStreamAsync(ms, opt));
                ms.Seek(0);
                return ms;
            }
        }

        static int CountLetters(string s, bool arabic)
        {
            int n = 0;
            foreach (char c in s)
                if (arabic ? (c >= '؀' && c <= 'ۿ') : ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))) n++;
            return n;
        }

        /// Reads the text of each page with the OCR built into Windows (Arabic and English), picking per page
        /// whichever language recognises more of it. Returns one list of lines per page.
        public static List<List<string>> OcrPages(string src, List<List<string>> tableRows = null)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
            PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
            var engines = new List<OcrEngine>();
            foreach (var tag in new[] { "ar", "en-US", "en-GB" })
            {
                try
                {
                    var lang = new Windows.Globalization.Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang)) { engines.Add(OcrEngine.TryCreateFromLanguage(lang)); if (tag.StartsWith("en")) break; }
                }
                catch { }
            }
            if (engines.Count == 0) { var e = OcrEngine.TryCreateFromUserProfileLanguages(); if (e != null) engines.Add(e); }
            if (engines.Count == 0) throw new Exception("Windows OCR is not available");

            var pages = new List<List<string>>();
            for (uint i = 0; i < doc.PageCount; i++)
            {
                using (var ms = RenderPage(doc, i))
                {
                    var dec = Wait(BitmapDecoder.CreateAsync(ms));
                    using (var bmp = Wait(dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)))
                    {
                        OcrResult arRes = null, enRes = null;
                        foreach (var engine in engines)
                        {
                            var r = Wait(engine.RecognizeAsync(bmp));
                            if (engine.RecognizerLanguage.LanguageTag.StartsWith("ar")) arRes = r; else if (enRes == null) enRes = r;
                        }
                        var lines = new List<string>();
                        if (arRes != null && CountLetters(arRes.Text ?? "", true) > 0)
                        {
                            foreach (var line in arRes.Lines)
                            {
                                if (CountLetters(line.Text, true) > 0) lines.Add(ArabicLine(line));
                                else lines.Add(SameLine(enRes, line) ?? line.Text); // Latin line: the English reader is more accurate
                                if (tableRows != null) tableRows.Add(Cells(line.Words));
                            }
                        }
                        else if (enRes != null)
                        {
                            lines.AddRange(enRes.Lines.Select(l => l.Text));
                            if (tableRows != null) foreach (var l in enRes.Lines) tableRows.Add(Cells(l.Words));
                        }
                        else if (arRes != null) lines.AddRange(arRes.Lines.Select(l => l.Text));
                        pages.Add(lines);
                        if (tableRows != null) tableRows.Add(new List<string>()); // blank row between pages
                    }
                }
            }
            return pages;
        }

        /// Windows OCR lists words left to right as they appear on the page. For an Arabic line, reverse them into
        /// reading order, but keep runs of Latin words/numbers (e.g. "Microsoft Word 365") in their own order.
        static string ArabicLine(OcrLine line)
        {
            var visual = line.Words.OrderBy(w => w.BoundingRect.X).Select(w => w.Text).ToList();
            visual.Reverse();
            var result = new List<string>();
            int i = 0;
            while (i < visual.Count)
            {
                if (CountLetters(visual[i], true) > 0) { result.Add(visual[i]); i++; continue; }
                int j = i;
                while (j < visual.Count && CountLetters(visual[j], true) == 0) j++;
                var run = visual.GetRange(i, j - i);
                run.Reverse();
                result.AddRange(run);
                i = j;
            }
            return string.Join(" ", result);
        }

        /// Splits a line into table cells wherever the gap between words is wider than about one line height.
        static List<string> Cells(IReadOnlyList<OcrWord> words)
        {
            var sorted = words.OrderBy(w => w.BoundingRect.X).ToList();
            var cells = new List<List<OcrWord>>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var w = sorted[i];
                if (i == 0) { cells.Add(new List<OcrWord> { w }); continue; }
                var prev = sorted[i - 1];
                double gap = w.BoundingRect.X - (prev.BoundingRect.X + prev.BoundingRect.Width);
                if (gap > Math.Max(w.BoundingRect.Height, prev.BoundingRect.Height) * 1.4) cells.Add(new List<OcrWord> { w });
                else cells[cells.Count - 1].Add(w);
            }
            return cells.Select(c =>
            {
                var texts = c.Select(w => w.Text).ToList();
                if (CountLetters(string.Join("", texts), true) > 0) texts.Reverse(); // Arabic reads right to left
                return string.Join(" ", texts);
            }).ToList();
        }

        /// The English reader's line at the same height as the given line, if any.
        static string SameLine(OcrResult other, OcrLine line)
        {
            if (other == null || line.Words.Count == 0) return null;
            double top = line.Words.Min(w => w.BoundingRect.Y), bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            // every English word whose centre sits inside this line's band, left to right
            var words = other.Lines.SelectMany(l => l.Words)
                .Where(w => { double c = w.BoundingRect.Y + w.BoundingRect.Height / 2; return c >= top && c <= bottom; })
                .OrderBy(w => w.BoundingRect.X).Select(w => w.Text).ToList();
            return words.Count >= Math.Max(1, line.Words.Count / 2) ? string.Join(" ", words) : null;
        }

        public static List<string> ToText(string src, string ext)
        {
            var pages = OcrPages(src);
            if (pages.All(p => p.Count == 0)) throw new Exception(S.Ar ? "لم يُعثر على نص في الملف" : "No text found in this PDF");
            string dest = Ops.Out(src, ext);
            if (ext == "txt")
            {
                File.WriteAllText(dest, string.Join("\r\n\r\n", pages.Select(p => string.Join("\r\n", p))), new UTF8Encoding(false));
                return new List<string> { dest };
            }
            // DOCX: build a simple HTML page per PDF page, then let Word save it as .docx
            var html = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><style>body{font-family:'Segoe UI',Tahoma,sans-serif;font-size:12pt}p{margin:0 0 6pt}</style></head><body>");
            for (int i = 0; i < pages.Count; i++)
            {
                if (i > 0) html.Append("<br clear=all style='page-break-before:always'>");
                foreach (var line in pages[i])
                {
                    bool rtl = CountLetters(line, true) > CountLetters(line, false);
                    html.Append(rtl ? "<p dir=\"rtl\" align=\"right\">" : "<p>").Append(System.Net.WebUtility.HtmlEncode(line)).Append("</p>");
                }
            }
            html.Append("</body></html>");
            string tmpDir = Ops.TempDir();
            try
            {
                string tmp = Path.Combine(tmpDir, Ops.BaseName(src) + ".html");
                File.WriteAllText(tmp, html.ToString(), new UTF8Encoding(false));
                OfficeOps.WordSave(tmp, dest, "docx", false);
            }
            finally { Ops.TryDelete(tmpDir); }
            return new List<string> { dest };
        }

        public static List<string> ToImages(string src, string ext)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
            PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
            uint n = doc.PageCount;
            var outputs = new List<string>();
            string folder = null;
            if (n > 1)
            {
                folder = Ops.Unique(Ops.DirOf(src), Ops.BaseName(src) + " (" + Ops.Suffix("pages") + ")", null);
                Directory.CreateDirectory(folder);
            }
            int digits = n.ToString().Length;
            for (uint i = 0; i < n; i++)
            {
                byte[] png;
                using (PdfPage page = doc.GetPage(i))
                {
                    var opt = new PdfPageRenderOptions();
                    opt.DestinationWidth = (uint)Math.Round(page.Size.Width * 300 / 96.0); // ~300 DPI
                    var ms = new InMemoryRandomAccessStream();
                    Wait(page.RenderToStreamAsync(ms, opt));
                    var reader = new DataReader(ms.GetInputStreamAt(0));
                    uint len = Wait(reader.LoadAsync((uint)ms.Size));
                    png = new byte[len];
                    reader.ReadBytes(png);
                    reader.Dispose();
                    ms.Dispose();
                }
                string dest = folder == null ? Ops.Out(src, ext)
                    : Path.Combine(folder, (S.Ar ? "صفحة-" : "page-") + (i + 1).ToString().PadLeft(digits, '0') + "." + ext);
                if (ext == "png") File.WriteAllBytes(dest, png);
                else
                {
                    string tmp = Path.Combine(Ops.TempDir(), "p.png");
                    File.WriteAllBytes(tmp, png);
                    File.WriteAllBytes(dest, ImageOps.Encode(ImageOps.Load(tmp), "jpg", Settings.JpegQuality));
                    Ops.TryDelete(Path.GetDirectoryName(tmp));
                }
                if (folder == null) outputs.Add(dest);
            }
            if (folder != null) outputs.Add(folder);
            return outputs;
        }
    }
}
