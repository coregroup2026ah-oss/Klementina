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
                        case "txt":
                            // Word's plain text runs table cells together ("NameMarkAhmad95"): make every table
                            // tab-separated lines first (only in this read-only copy)
                            for (int ti = (int)doc.Tables.Count; ti >= 1; ti--)
                                try { doc.Tables.Item(ti).ConvertToText(1); } catch { }
                            doc.SaveAs2(FileName: dest, FileFormat: 2, Encoding: 65001); break;
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

        /// Every sheet's used cells as an HTML table (the sheet name as a heading when there are several).
        static string SheetsHtml(dynamic wb, string title)
        {
            var sb = new StringBuilder();
            int count = (int)wb.Worksheets.Count;
            for (int si = 1; si <= count; si++)
            {
                dynamic ws = wb.Worksheets.Item(si);
                object v = ws.UsedRange.Value2;
                if (count > 1) sb.Append("<h2>" + System.Net.WebUtility.HtmlEncode((string)ws.Name) + "</h2>");
                sb.Append("<table>");
                var arr = v as object[,];
                if (arr == null) sb.Append("<tr><td>" + System.Net.WebUtility.HtmlEncode(Convert.ToString(v)) + "</td></tr>");
                else
                    for (int r = arr.GetLowerBound(0); r <= arr.GetUpperBound(0); r++)
                    {
                        sb.Append("<tr>");
                        for (int c = arr.GetLowerBound(1); c <= arr.GetUpperBound(1); c++)
                        {
                            string tag = r == arr.GetLowerBound(0) ? "th" : "td";
                            sb.Append("<" + tag + ">" + System.Net.WebUtility.HtmlEncode(Convert.ToString(arr[r, c], System.Globalization.CultureInfo.CurrentCulture)) + "</" + tag + ">");
                        }
                        sb.Append("</tr>");
                    }
                sb.Append("</table>");
                Release((object)ws);
            }
            return TextOps.Page(title, sb.ToString());
        }

        public static List<string> Excel(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            string srcExt = Catalog.Ext(src);
            string tsvTmp = null;
            string viaHtml = null; // PDF fallback when Excel can't export (it needs a printer installed)
            string csvTmpDir = null;
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
                        // Excel's text import garbles UTF-8 Arabic and ignores the separator; open a real workbook
                        csvTmpDir = Ops.TempDir();
                        string tmpX = Path.Combine(csvTmpDir, Ops.BaseName(src) + ".xlsx");
                        File.Move(TextOps.CsvToXlsx(src, "xlsx")[0], tmpX);
                        wb = app.Workbooks.Open(Filename: tmpX, UpdateLinks: 0, ReadOnly: true);
                    }
                    else wb = app.Workbooks.Open(Filename: src, UpdateLinks: 0, ReadOnly: true);

                    switch (ext)
                    {
                        case "pdf":
                            try { wb.ExportAsFixedFormat(0, dest); }
                            catch
                            {
                                // Excel can't export without a printer: lay the sheets out as tables ourselves
                                // (Excel's own web page export is a frameset that prints as an empty page)
                                viaHtml = Path.Combine(Ops.TempDir(), Ops.BaseName(src) + ".html");
                                File.WriteAllText(viaHtml, SheetsHtml(wb, Ops.BaseName(src)), new UTF8Encoding(false));
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
            if (csvTmpDir != null) Ops.TryDelete(csvTmpDir);
            if (tsvTmp != null)
            {
                try { File.WriteAllText(dest, File.ReadAllText(tsvTmp, Encoding.Unicode), new UTF8Encoding(false)); }
                finally { Ops.TryDelete(Path.GetDirectoryName(tsvTmp)); }
            }
            if (viaHtml != null)
            {
                try
                {
                    TextOps.HtmlFileToPdf(viaHtml, dest);
                    if (!File.Exists(dest)) WordSave(viaHtml, dest, "pdf", false);
                }
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
        /// Every page's text as blocks: the PDF's own text layer where it has one (exact, keeps headings, lists and
        /// tables), and the Windows OCR reader only for pages without text, such as scans.
        /// With imageDir set, the pictures are saved there too; pictures qpdf can't hand over in a usable format
        /// (JPEG 2000, fax, masks…) are cut out of a rendering of the page instead.
        public static List<PdfText.Page> ReadPages(string src, string imageDir = null)
        {
            // pictures are needed even for plain-text outputs when a page is one picture (it gets OCR'd)
            string work = imageDir ?? Ops.TempDir();
            try { return ReadPagesIn(src, work, imageDir != null); }
            finally { if (imageDir == null) Ops.TryDelete(work); }
        }

        static List<PdfText.Page> ReadPagesIn(string src, string imageDir, bool keepPictures)
        {
            List<PdfText.Page> pages = null;
            try { pages = PdfText.Read(src, imageDir); } catch { pages = null; }
            HashSet<int> missing = null;
            if (pages != null)
            {
                // a page that is one picture (scan, or a design exported as an image): OCR the picture itself, with
                // word positions, colours, coloured areas and photos — not a rendering, which some broken files lack
                for (int i = 0; i < pages.Count; i++)
                    if (pages[i] != null && pages[i].NeedsOcr)
                    {
                        try
                        {
                            var words = OcrImageWords(pages[i].ScanImage);
                            pages[i] = words.Count == 0 ? null : OcrLayout.Build(pages[i], words, pages[i].ScanImage, imageDir);
                        }
                        catch { pages[i] = null; }
                    }
                missing = new HashSet<int>(Enumerable.Range(0, pages.Count).Where(i => pages[i] == null));
                if (keepPictures) CropMissingPictures(src, pages, imageDir);
                if (missing.Count == 0) return pages;
            }
            var ocr = OcrPages(src, null, missing);
            if (pages == null) pages = ocr.Select(p => (PdfText.Page)null).ToList();
            for (int i = 0; i < pages.Count && i < ocr.Count; i++)
                if (pages[i] == null)
                {
                    var page = new PdfText.Page { Ocr = true };
                    page.Blocks = ocr[i].Where(l => l.Trim().Length > 0).Select(l => new PdfText.Block
                    {
                        Kind = PdfText.Kind.Para, Text = l, Rtl = CountLetters(l, true) > CountLetters(l, false),
                        Runs = new List<PdfText.Run> { new PdfText.Run { Text = l, Size = 11 } }, SpaceAfter = 4,
                    }).ToList();
                    pages[i] = page;
                }
            return pages;
        }

        static void CropMissingPictures(string src, List<PdfText.Page> pages, string imageDir)
        {
            var todo = new List<Tuple<int, PdfText.Block>>();
            for (int i = 0; i < pages.Count; i++)
                if (pages[i] != null)
                    foreach (var b in pages[i].Blocks.Where(b => b.Kind == PdfText.Kind.Image && b.Image == null)) todo.Add(Tuple.Create(i, b));
            bool backgrounds = pages.Any(p => p != null && p.BackgroundImage == "");
            if (todo.Count == 0 && !backgrounds) return;
            try
            {
                StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
                PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
                int n = 0;
                Func<int, System.Drawing.RectangleF, string> crop = (pi, rc) =>
                {
                    var page = pages[pi];
                    using (PdfPage pg = doc.GetPage((uint)pi))
                    {
                        double k = 96.0 / 72.0;
                        var opt = new PdfPageRenderOptions
                        {
                            SourceRect = new Rect((rc.X - page.X0) * k, (page.Y0 + page.Height - rc.Bottom) * k, rc.Width * k, rc.Height * k),
                            DestinationWidth = (uint)Math.Max(16, Math.Min(4000, rc.Width * 200 / 72)),
                        };
                        var ms = new InMemoryRandomAccessStream();
                        Wait(pg.RenderToStreamAsync(ms, opt));
                        var reader = new DataReader(ms.GetInputStreamAt(0));
                        uint len = Wait(reader.LoadAsync((uint)ms.Size));
                        var png = new byte[len];
                        reader.ReadBytes(png);
                        reader.Dispose(); ms.Dispose();
                        string f = Path.Combine(imageDir, "crop" + (++n) + ".png");
                        File.WriteAllBytes(f, png);
                        return f;
                    }
                };
                foreach (var t in todo) { try { t.Item2.Image = crop(t.Item1, t.Item2.ImageRect); } catch { } }
                for (int i = 0; i < pages.Count; i++)
                    if (pages[i] != null && pages[i].BackgroundImage == "")
                        try { pages[i].BackgroundImage = crop(i, pages[i].BackgroundRect); } catch { pages[i].BackgroundImage = null; }
            }
            catch { /* pictures are a bonus: the text still converts */ }
        }

        static List<OcrEngine> OcrEngines()
        {
            var engines = new List<OcrEngine>();
            foreach (var tag in new[] { "ar", "en-US", "en-GB" })
            {
                try
                {
                    var lang = new Windows.Globalization.Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang)) { engines.Add(OcrEngine.TryCreateFromLanguage(lang)); if (tag.StartsWith("en", StringComparison.Ordinal)) break; }
                }
                catch { }
            }
            if (engines.Count == 0) { var e = OcrEngine.TryCreateFromUserProfileLanguages(); if (e != null) engines.Add(e); }
            if (engines.Count == 0) throw new Exception("Windows OCR is not available");
            return engines;
        }

        /// Words of a picture file with their boxes (pixels of that picture). Every word of a line gets the line's
        /// height, so words sit on one baseline; Latin-only lines use the English reader, which knows them better.
        public static List<OcrLayout.Word> OcrImageWords(string imageFile)
        {
            var engines = OcrEngines();
            StorageFile f = Wait(StorageFile.GetFileFromPathAsync(imageFile));
            using (var stream = Wait(f.OpenAsync(FileAccessMode.Read)))
            {
                var dec = Wait(BitmapDecoder.CreateAsync(stream));
                double k = Math.Min(1.0, (OcrEngine.MaxImageDimension - 1) / (double)Math.Max(dec.PixelWidth, dec.PixelHeight));
                var tf = new BitmapTransform
                {
                    ScaledWidth = (uint)Math.Max(1, dec.PixelWidth * k), ScaledHeight = (uint)Math.Max(1, dec.PixelHeight * k),
                    InterpolationMode = BitmapInterpolationMode.Fant,
                };
                using (var bmp = Wait(dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, tf, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)))
                {
                    OcrResult arRes = null, enRes = null;
                    foreach (var engine in engines)
                    {
                        var r = Wait(engine.RecognizeAsync(bmp));
                        if (engine.RecognizerLanguage.LanguageTag.StartsWith("ar", StringComparison.Ordinal)) arRes = r; else if (enRes == null) enRes = r;
                    }
                    var main = arRes ?? enRes;
                    var words = new List<OcrLayout.Word>();
                    if (main == null) return words;
                    foreach (var line in main.Lines)
                    {
                        List<OcrWord> ws = line.Words.ToList();
                        if (arRes != null && enRes != null && CountLetters(line.Text, true) == 0)
                        {
                            var eng = SameLineWords(enRes, line);
                            if (eng != null) ws = eng;
                        }
                        if (ws.Count == 0) continue;
                        double top = ws.Min(w => w.BoundingRect.Y), bottom = ws.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
                        foreach (var w in ws)
                            words.Add(new OcrLayout.Word
                            {
                                Text = FixOcrWord(LatinFromEnglish(w, enRes)),
                                Box = new System.Drawing.RectangleF((float)(w.BoundingRect.X / k), (float)(top / k), (float)(w.BoundingRect.Width / k), (float)((bottom - top) / k)),
                            });
                    }
                    return words;
                }
            }
        }

        /// A Latin word inside an Arabic line: the English reader's reading of the same spot is more reliable
        /// (the Arabic reader confuses l/I, O/0 in Latin text).
        static string LatinFromEnglish(OcrWord w, OcrResult en)
        {
            if (en == null || CountLetters(w.Text, true) > 0) return w.Text;
            var r = w.BoundingRect;
            OcrWord best = null; double bestOverlap = 0;
            foreach (var e in en.Lines.SelectMany(l => l.Words))
            {
                var b = e.BoundingRect;
                double ix = Math.Max(0, Math.Min(r.X + r.Width, b.X + b.Width) - Math.Max(r.X, b.X));
                double iy = Math.Max(0, Math.Min(r.Y + r.Height, b.Y + b.Height) - Math.Max(r.Y, b.Y));
                double overlap = ix * iy / Math.Max(1, Math.Max(r.Width * r.Height, b.Width * b.Height));
                if (overlap > bestOverlap) { bestOverlap = overlap; best = e; }
            }
            return best != null && bestOverlap > 0.45 && CountLetters(best.Text, true) == 0 ? best.Text : w.Text;
        }

        static readonly System.Text.RegularExpressions.Regex ShekelPrefix =
            new System.Text.RegularExpressions.Regex(@"^(QJ|Q\.J|IJ|lJ|LJ|n\.J|nJ|lu|Iu|L1|U)(?=[\dOoIl][\dOoIl,.]+$)");

        /// Common OCR slips: the shekel sign read as "QJ"/"IJ"/"lu", and letters inside numbers ("1O" → "10", "l44" → "144").
        static string FixOcrWord(string t)
        {
            if (string.IsNullOrEmpty(t)) return t;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, "^[Il1][Oo0]x?$")) return "10";   // "× 10" read as "1Ox" / "IO"
            t = ShekelPrefix.Replace(t, "₪");
            if (t.Any(char.IsDigit))
            {
                string before;
                do
                {
                    before = t;
                    t = System.Text.RegularExpressions.Regex.Replace(t, @"(?<=[\d₪,.])[Oo]|[Oo](?=\d)", "0");
                    t = System.Text.RegularExpressions.Regex.Replace(t, @"(?<=[\d₪,.])[lI]|[lI](?=[\d,])", "1");
                } while (t != before);
            }
            return t;
        }

        /// The English reader's words in the same band as a line of the Arabic reader, if it read the line too.
        static List<OcrWord> SameLineWords(OcrResult other, OcrLine line)
        {
            if (line.Words.Count == 0) return null;
            double top = line.Words.Min(w => w.BoundingRect.Y), bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            double left = line.Words.Min(w => w.BoundingRect.X), right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            var words = other.Lines.SelectMany(l => l.Words).Where(w =>
            {
                double cy = w.BoundingRect.Y + w.BoundingRect.Height / 2, cx = w.BoundingRect.X + w.BoundingRect.Width / 2;
                return cy >= top && cy <= bottom && cx >= left - 4 && cx <= right + 4;
            }).ToList();
            return words.Count >= Math.Max(1, line.Words.Count / 2) ? words : null;
        }

        public static List<List<string>> OcrPages(string src, List<List<string>> tableRows = null, HashSet<int> only = null)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
            PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
            var engines = new List<OcrEngine>();
            foreach (var tag in new[] { "ar", "en-US", "en-GB" })
            {
                try
                {
                    var lang = new Windows.Globalization.Language(tag);
                    if (OcrEngine.IsLanguageSupported(lang)) { engines.Add(OcrEngine.TryCreateFromLanguage(lang)); if (tag.StartsWith("en", StringComparison.Ordinal)) break; }
                }
                catch { }
            }
            if (engines.Count == 0) { var e = OcrEngine.TryCreateFromUserProfileLanguages(); if (e != null) engines.Add(e); }
            if (engines.Count == 0) throw new Exception("Windows OCR is not available");

            var pages = new List<List<string>>();
            for (uint i = 0; i < doc.PageCount; i++)
            {
                if (only != null && !only.Contains((int)i)) { pages.Add(new List<string>()); continue; }
                using (var ms = RenderPage(doc, i))
                {
                    var dec = Wait(BitmapDecoder.CreateAsync(ms));
                    using (var bmp = Wait(dec.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)))
                    {
                        OcrResult arRes = null, enRes = null;
                        foreach (var engine in engines)
                        {
                            var r = Wait(engine.RecognizeAsync(bmp));
                            if (engine.RecognizerLanguage.LanguageTag.StartsWith("ar", StringComparison.Ordinal)) arRes = r; else if (enRes == null) enRes = r;
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

        /// PDF → TXT, or PDF → DOCX as an exact copy of every page (falling back to reflowed paragraphs, tables and
        /// pictures when the exact copy can't be made).
        public static List<string> ToText(string src, string ext)
        {
            string tmpDir = Ops.TempDir();
            try
            {
                var pages = ReadPages(src, ext == "docx" ? Path.Combine(tmpDir, "media") : null);
                if (pages.All(p => p.Blocks.Count == 0 && string.IsNullOrEmpty(p.BackgroundImage))) throw new Exception(S.T("no_text"));
                string dest = Ops.Out(src, ext);
                if (ext == "txt")
                    File.WriteAllText(dest, string.Join("\r\n\r\n", pages.Select(p => string.Join("\r\n", PdfText.Lines(p.Blocks)))), new UTF8Encoding(false));
                else
                {
                    // exact copy: the page without its text as the background, every text line on top in its place
                    List<PdfText.ExactPage> exact = null;
                    List<string> backgrounds = null;
                    try
                    {
                        string noText = Path.Combine(tmpDir, "notext.pdf");
                        exact = PdfText.ReadExact(src, noText);
                        backgrounds = RenderBackgrounds(noText, exact, Path.Combine(tmpDir, "pages"));
                    }
                    catch { exact = null; backgrounds = null; }
                    DocxWriter.Write(pages, dest, exact, backgrounds);   // Word is not needed
                }
                return new List<string> { dest };
            }
            finally { Ops.TryDelete(tmpDir); }
        }

        /// Renders the text-free copy of every page that has text, at print quality (200 DPI). Line art stays PNG;
        /// a page full of photos (a large PNG) becomes JPEG so the Word file stays a sensible size.
        static List<string> RenderBackgrounds(string noTextPdf, List<PdfText.ExactPage> exact, string dir)
        {
            Directory.CreateDirectory(dir);
            var files = RenderPagesToFiles(noTextPdf, dir, 200).Select(kv => kv.Key).ToList();
            var result = new List<string>();
            for (int i = 0; i < files.Count; i++)
            {
                if (i >= exact.Count || !exact[i].HasText) { result.Add(null); continue; }
                result.Add(Compact(files[i]));
            }
            return result;
        }

        /// A page picture over 900 KB (photos) becomes a JPEG; line art stays a sharp PNG.
        static string Compact(string f)
        {
            if (new FileInfo(f).Length <= 900 * 1024) return f;
            string jpg = Path.ChangeExtension(f, ".jpg");
            using (var img = System.Drawing.Image.FromFile(f))
            {
                var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
                using (var ep = new System.Drawing.Imaging.EncoderParameters(1))
                {
                    ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                    img.Save(jpg, codec, ep);
                }
            }
            return jpg;
        }

        /// PDF → PowerPoint without PowerPoint: a slide per page with the text-free page behind and every text line
        /// as an editable text box in its place (pages without text become a picture slide).
        public static List<string> ToPptx(string src, string ext)
        {
            string tmpDir = Ops.TempDir();
            try
            {
                string dest = Ops.Out(src, "pptx");
                string noText = Path.Combine(tmpDir, "notext.pdf");
                var exact = PdfText.ReadExact(src, noText);
                var pictures = RenderBackgrounds(noText, exact, Path.Combine(tmpDir, "pages"));
                if (pictures.Count < exact.Count || pictures.Any(x => x == null))
                {
                    string wholeDir = Path.Combine(tmpDir, "whole");
                    Directory.CreateDirectory(wholeDir);
                    var whole = RenderPagesToFiles(src, wholeDir, 200).Select(kv => kv.Key).ToList();
                    for (int i = 0; i < exact.Count; i++)
                    {
                        while (pictures.Count <= i) pictures.Add(null);
                        if (pictures[i] == null && i < whole.Count) pictures[i] = Compact(whole[i]);
                    }
                }
                PptxWriter.Write(exact, pictures, dest);
                return new List<string> { dest };
            }
            finally { Ops.TryDelete(tmpDir); }
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
                    : Path.Combine(folder, Ops.Suffix("page") + "-" + (i + 1).ToString().PadLeft(digits, '0') + "." + ext);
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
