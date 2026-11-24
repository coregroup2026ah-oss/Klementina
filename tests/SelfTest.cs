using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Klementina
{
    /// Runs every non-interactive operation on generated sample files and renders wheel snapshots.
    /// Usage: SelfTest.exe <empty work dir>
    static class SelfTest
    {
        static StringBuilder log = new StringBuilder();
        static readonly DateTime started = DateTime.Now;
        static int pass, fail, skip;

        [STAThread]
        static int Main(string[] args)
        {
            // in tests, UI errors must surface as exceptions, never as dialogs on the user's screen
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            if (args[0] == "--sel") // print what ExplorerSel sees for every Explorer window and the desktop
            {
                Native.EnumWindowsAll(h =>
                {
                    string cls = Native.ClassOf(h);
                    if (cls == "CabinetWClass" || cls == "Progman" || cls == "WorkerW")
                    {
                        var sel = ExplorerSel.Of(h);
                        if (cls != "WorkerW" || sel.Count > 0) Console.WriteLine(cls + " " + h + " -> " + sel.Count + ": " + string.Join(" | ", sel));
                    }
                });
                Console.WriteLine("foreground: " + Native.ClassOf(Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT)));
                return 0;
            }
            if (args[0] == "--lang-shots") // --lang-shots <image> <out dir>: settings window and tools wheel in every language
            {
                Settings.Load(); Engines.Refresh();
                Directory.CreateDirectory(args[2]);
                var w = new WheelForm();
                foreach (var l in S.Languages)
                {
                    Shot(w, args[2], "wheel-" + l[0], l[0], WheelKind.Tools, new List<string> { args[1] }, 2);
                    var sf = new SettingsForm();
                    sf.Show(); Application.DoEvents();
                    using (var b = new Bitmap(sf.Width, sf.Height)) { sf.DrawToBitmap(b, new Rectangle(0, 0, sf.Width, sf.Height)); b.Save(Path.Combine(args[2], "settings-" + l[0] + ".png"), ImageFormat.Png); }
                    sf.Close();
                    Console.WriteLine(l[0] + ": " + S.T("set_title") + " | " + S.T("compress") + " | " + Ops.Suffix("compressed"));
                }
                return 0;
            }
            if (args[0] == "--settings-shot") // render the settings window to a PNG  [scale] [language]
            {
                Settings.Load(); Engines.Refresh();
                if (args.Length > 2) SettingsForm.TestScale = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                if (args.Length > 3) Settings.Lang = args[3];
                var f = new SettingsForm();
                f.Show(); Application.DoEvents();
                using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(args[1], ImageFormat.Png); }
                f.Close();
                return 0;
            }
            if (args[0] == "--baselines") // --baselines <pdf> <out txt>: every text line of page 1 with its baseline and edges
            {
                Engines.Refresh();
                var pages = PdfText.ReadExact(args[1], Path.Combine(Path.GetTempPath(), "kl-baselines-notext.pdf"));
                var p0 = pages[0];
                var sbx = new System.Text.StringBuilder();
                bool fitx;
                foreach (var l in p0.Lines.OrderByDescending(l => l.Baseline))
                    sbx.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}	{1:F1}	{2:F1}	{3:F1}	{4}	{5:F1}	{6}",
                        p0.Y0 + p0.Height - l.Baseline, l.Left - p0.X0, l.Right - p0.X0, l.Size, string.Concat(l.Runs.Select(r => r.Text)),
                        l.Runs.Sum(r => DocxWriter.MeasurePt(r, "Arial", l.Rtl)), DocxWriter.WidthScale(l, "Arial", out fitx)));
                File.WriteAllText(args[2], sbx.ToString(), System.Text.Encoding.UTF8);
                return 0;
            }
            if (args[0] == "--convert-all") // --convert-all <file>...: every format on the Convert wheel, outputs next to each file
            {
                Settings.Load(); Settings.Lang = "ar"; Engines.Refresh();
                for (int i = 1; i < args.Length; i++)
                {
                    string f = args[i];
                    foreach (var it in Catalog.Build(WheelKind.Convert, new List<string> { f }).Where(x => x.IsFormat))
                    {
                        if (it.Need != null && !Engines.Has(it.Need)) { Console.WriteLine("skip " + Path.GetFileName(f) + " -> " + it.Label); continue; }
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        try
                        {
                            var r = it.Run(new List<string> { f });
                            Console.WriteLine((r.Errors.Count == 0 && r.Outputs.Count > 0 ? "ok   " : "FAIL ") + Path.GetFileName(f) + " -> " + it.Label + " : " +
                                string.Join(" | ", r.Outputs.Select(o => Path.GetFileName(o) + " (" + Ops.Size(Ops.SizeOf(o)) + ")")) + " " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s" +
                                (r.Errors.Count > 0 ? "  ERR: " + string.Join("; ", r.Errors) : ""));
                        }
                        catch (Exception ex) { Console.WriteLine("FAIL " + Path.GetFileName(f) + " -> " + it.Label + " EXC: " + Ops.Clean(ex)); }
                    }
                }
                return 0;
            }
            if (args[0] == "--pptx") // --pptx <pdf>: PDF → PowerPoint
            {
                Engines.Refresh();
                try { Console.WriteLine("OK " + PdfOps.ToPptx(args[1], "pptx")[0]); return 0; }
                catch (Exception ex) { Console.WriteLine(ex.ToString()); return 1; }
            }
            if (args[0] == "--ocr") // --ocr <pdf> <txt|docx>
            {
                Engines.Refresh();
                try { Console.WriteLine("OK " + PdfOps.ToText(args[1], args[2])[0]); return 0; }
                catch (Exception ex) { Console.WriteLine(ex.ToString()); return 1; }
            }
            if (args[0] == "--editor-shots") // --editor-shots <image> <out dir> [media file] : render each tool window to a PNG
            {
                Settings.Load(); Engines.Refresh();
                Directory.CreateDirectory(args[2]);
                var forms = new List<KeyValuePair<string, Func<Form>>>
                {
                    new KeyValuePair<string, Func<Form>>("photo", () => new PhotoEditor(args[1])),
                    new KeyValuePair<string, Func<Form>>("crop", () => new CropEditor(args[1])),
                    new KeyValuePair<string, Func<Form>>("annotate", () => new MarkupEditor(args[1], false)),
                    new KeyValuePair<string, Func<Form>>("redact", () => new MarkupEditor(args[1], true)),
                    new KeyValuePair<string, Func<Form>>("bg", () => new BackgroundEditor(args[1])),
                    new KeyValuePair<string, Func<Form>>("meta", () => new MetadataWindow(args.Length > 3 ? args[3] : args[1])),
                };
                foreach (var kv in forms)
                {
                    try
                    {
                        var f = kv.Value();
                        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, 0);
                        f.Show(); Application.DoEvents(); System.Threading.Thread.Sleep(300); Application.DoEvents();
                        using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(Path.Combine(args[2], kv.Key + ".png"), ImageFormat.Png); }
                        var ew = f as EditorWindow;
                        if (ew != null)
                        {
                            // exercise the save path: tweak something, then render the full-size result
                            var t = typeof(EditorWindow);
                            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                            var tb = f.GetType().GetField("exposure", flags) ?? f.GetType().GetField("padding", flags);
                            if (tb != null) { var bar = (TrackBar)tb.GetValue(f); bar.Value = Math.Min(bar.Maximum, 60); }
                            var cropField = f.GetType().GetField("crop", flags);
                            if (cropField != null) cropField.SetValue(f, new RectangleF(60, 120, 380, 380));
                            var marks = f.GetType().GetField("marks", flags);
                            if (marks != null)
                            {
                                var list = (List<Mark>)marks.GetValue(f);
                                list.Add(new Mark { Kind = kv.Key == "redact" ? "redact" : "arrow", Color = Color.Red, Width = 12, RedactMode = 1, Points = { new PointF(40, 40), new PointF(300, 260) } });
                                list.Add(new Mark { Kind = "text", Color = Color.Yellow, Width = 10, Text = "مرحبا Hello", Points = { new PointF(40, 520) } });
                            }
                            var src = (Bitmap)t.GetField("Source", flags).GetValue(f);
                            f.GetType().GetMethod("BeforeSave", flags).Invoke(f, null);
                            using (var copy = new Bitmap(src))
                            using (var res = (Bitmap)f.GetType().GetMethod("RenderFull", flags).Invoke(f, new object[] { copy }))
                                res.Save(Path.Combine(args[2], kv.Key + "_result.png"), ImageFormat.Png);
                        }
                        f.Close();
                        Console.WriteLine("ok " + kv.Key);
                    }
                    catch (Exception ex) { Console.WriteLine("FAIL " + kv.Key + ": " + ex); }
                }
                return 0;
            }
            if (args[0] == "--media-shots") // --media-shots <video> <audio> <out dir>: open each audio/video window, screenshot, run its Apply
            {
                Settings.Load(); Engines.Refresh();
                Directory.CreateDirectory(args[3]);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var cases = new[] { new { name = "trim_video", file = args[1], mode = MediaToolWindow.Mode.Trim }, new { name = "bleep_video", file = args[1], mode = MediaToolWindow.Mode.Bleep },
                                    new { name = "trim_audio", file = args[2], mode = MediaToolWindow.Mode.Trim }, new { name = "bleep_audio", file = args[2], mode = MediaToolWindow.Mode.Bleep },
                                    new { name = "channels_audio", file = args[2], mode = MediaToolWindow.Mode.Channels } };
                foreach (var c in cases)
                {
                    try
                    {
                        var f = new MediaToolWindow(c.file, c.mode);
                        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, 0);
                        f.Show();
                        for (int i = 0; i < 40; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); } // let the waveform/thumbnails load
                        var tl = (Timeline)f.GetType().GetField("timeline", flags).GetValue(f);
                        tl.Start = 0.5; tl.End = 2.2;
                        if (c.mode == MediaToolWindow.Mode.Bleep) tl.Ranges.Add(new[] { 1.0, 1.6 });
                        tl.Invalidate(); Application.DoEvents();
                        using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(Path.Combine(args[3], c.name + ".png"), ImageFormat.Png); }
                        Program.TestResults.Clear();
                        f.GetType().GetMethod("Apply", flags).Invoke(f, null);
                        var r = Program.TestResults.FirstOrDefault();
                        bool ok = r != null && r.Errors.Count == 0 && r.Outputs.Count == 1 && File.Exists(r.Outputs[0]);
                        Console.WriteLine((ok ? "ok   " : "FAIL ") + c.name + " peaks=" + (tl.Peaks != null) + " thumbs=" + tl.Thumbs.Count + " -> " +
                                          (r == null ? "no result" : string.Join(" | ", r.Outputs.Select(o => Path.GetFileName(o) + " " + MediaOps.Probe(o).Duration.ToString("0.00", Ops.Inv) + "s")) + string.Join("; ", r.Errors)));
                    }
                    catch (Exception ex) { Console.WriteLine("FAIL " + c.name + ": " + ex); }
                }
                return 0;
            }
            if (args[0] == "--organizer") // --organizer <pdf> <out png>: reverse pages, rotate the first, save
            {
                Engines.Refresh();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var f = new PdfOrganizer(args[1]);
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, 0);
                f.Show(); Application.DoEvents();
                var list = (System.Collections.IList)f.GetType().GetField("pages", flags).GetValue(f);
                var items = list.Cast<object>().Reverse().ToList();
                list.Clear(); foreach (var it in items) list.Add(it);
                var rot = items[0].GetType().GetField("Rotation");
                rot.SetValue(items[0], 90);
                ((Control)f.GetType().GetField("grid", flags).GetValue(f)).Invalidate(); Application.DoEvents();
                using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(args[2], ImageFormat.Png); }
                Program.TestResults.Clear();
                f.GetType().GetMethod("Save", flags).Invoke(f, null);
                var r = Program.TestResults.FirstOrDefault();
                Console.WriteLine(r == null ? "no result" : (r.Errors.Count == 0 ? "ok " + r.Outputs[0] + " pages=" + PdfTools.PageCount(r.Outputs[0]) : "FAIL " + string.Join("; ", r.Errors)));
                return 0;
            }
            if (args[0] == "--make-big-pdf") // --make-big-pdf <out.pdf>: photo-heavy PDF with real text on the first page (via Word if present)
            {
                var pages = new List<System.Windows.Media.Imaging.BitmapSource>();
                var rnd = new Random(3);
                for (int i = 0; i < 4; i++)
                {
                    using (var b = new Bitmap(4000, 3000))
                    {
                        using (var g = Graphics.FromImage(b))
                        {
                            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 4000, 3000), Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)), Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)), 30f))
                                g.FillRectangle(br, 0, 0, 4000, 3000);
                            for (int k = 0; k < 4000; k++) // texture so JPEG has real work to do
                                using (var pb = new SolidBrush(Color.FromArgb(rnd.Next(60, 200), rnd.Next(256), rnd.Next(256), rnd.Next(256))))
                                    g.FillEllipse(pb, rnd.Next(4000), rnd.Next(3000), rnd.Next(8, 90), rnd.Next(8, 90));
                            g.DrawString("صفحة " + (i + 1) + " Page", new Font("Segoe UI", 160, FontStyle.Bold), Brushes.White, 200, 200);
                        }
                        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); ms.Position = 0; pages.Add(System.Windows.Media.Imaging.BitmapFrame.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad)); }
                    }
                }
                Pdf.WriteImages(pages, args[1]);
                Console.WriteLine("made " + args[1] + " " + Ops.Size(new FileInfo(args[1]).Length));
                return 0;
            }
            if (args[0] == "--pdf-levels") // --pdf-levels <pdf>: compress at each level and report
            {
                Engines.Refresh();
                Console.WriteLine("original " + Ops.Size(Ops.SizeOf(args[1])) + " pages=" + PdfTools.PageCount(args[1]));
                for (int lv = 0; lv < 4; lv++)
                {
                    try
                    {
                        var t0 = DateTime.Now;
                        string o = PdfTools.CompressLevel(args[1], lv);
                        string chk; Ops.Run(Engines.Qpdf, "--check " + Ops.Q(o), out chk);
                        Console.WriteLine("level " + lv + ": " + Ops.Size(Ops.SizeOf(o)) + " pages=" + PdfTools.PageCount(o) + " check=" + (chk.Contains("No syntax or stream encoding errors") ? "ok" : "PROBLEM") +
                                          " time=" + (DateTime.Now - t0).TotalSeconds.ToString("0.0") + "s -> " + Path.GetFileName(o));
                    }
                    catch (Exception ex) { Console.WriteLine("level " + lv + " FAIL " + ex); }
                }
                try { var o2 = PdfTools.CompressToSize(args[1], Ops.SizeOf(args[1]) / 10); Console.WriteLine("to 10%: " + Ops.Size(Ops.SizeOf(o2))); } catch (Exception ex) { Console.WriteLine("to 10% FAIL " + ex.Message); }
                return 0;
            }
            if (args[0] == "--render") // --render <pdf> <out dir> [dpi]: every page to PNG
            {
                Directory.CreateDirectory(args[2]);
                foreach (var kv in PdfOps.RenderPagesToFiles(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 60)) Console.WriteLine(kv.Key);
                return 0;
            }
            if (args[0] == "--compress-dialog-shot") // --compress-dialog-shot <out png>: same controls as Ui.Compress, rendered
            {
                Settings.Load();
                var dlg = new KDialog(S.T("compress"));
                dlg.AddText(S.F("compress_now", "report.pdf", "184.0 MB"), true);
                dlg.AddChoices(S.T("compress_how"), new[] { S.T("lv_light"), S.T("lv_balanced"), S.T("lv_strong"), S.T("lv_max"), S.T("by_percent"), S.T("custom_size") }, 1);
                dlg.AddChoices(S.T("compress_pct"), new[] { "75%", "50%", "25%", "10%" }, 1);
                dlg.AddInput(S.T("target_mb"), "92", 160);
                dlg.AddText(S.T("compress_levels_note"));
                dlg.AddText(S.T("pdf_raster_note"));
                dlg.AddButton(S.T("cancel"), false, () => { });
                dlg.AddButton(S.T("compress"), true, () => { });
                dlg.StartPosition = FormStartPosition.Manual; dlg.Location = new Point(-4000, 0);
                dlg.Show(); Application.DoEvents();
                using (var b = new Bitmap(dlg.Width, dlg.Height)) { dlg.DrawToBitmap(b, new Rectangle(0, 0, dlg.Width, dlg.Height)); b.Save(args[1], ImageFormat.Png); }
                return 0;
            }
            if (args[0] == "--split-tests") // --split-tests <pdf>
            {
                Engines.Refresh();
                string src = args[1];
                int n = PdfTools.PageCount(src);
                Console.WriteLine("pages " + n);
                Func<string, string> summary = folder => string.Join(" | ", Directory.GetFiles(folder).OrderBy(x => x).Select(x => Path.GetFileName(x) + "=" + PdfTools.PageCount(x) + "p"));
                Console.WriteLine("halves: " + summary(PdfTools.SplitRanges(src, PdfTools.EqualParts(n, 2))));
                Console.WriteLine("3 equal: " + summary(PdfTools.SplitRanges(src, PdfTools.EqualParts(n, 3))));
                Console.WriteLine("every 100: " + summary(PdfTools.SplitEvery(src, 100)));
                var custom = "1-50, ١٠١-٢٠٠، 300-365".Split(new[] { ',', '،' }).Select(r => PdfTools.NormalizeRanges(r, n)).ToList();
                Console.WriteLine("custom: " + summary(PdfTools.SplitRanges(src, custom)));
                var t0 = DateTime.Now;
                var org = new PdfOrganizer(src);
                Console.WriteLine("organizer constructed in " + (DateTime.Now - t0).TotalSeconds.ToString("0.00") + "s");
                org.StartPosition = FormStartPosition.Manual; org.Location = new Point(-4000, 0);
                org.Show();
                for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
                using (var b = new Bitmap(org.Width, org.Height)) { org.DrawToBitmap(b, new Rectangle(0, 0, org.Width, org.Height)); b.Save(Path.Combine(Path.GetDirectoryName(src), "organizer365.png"), ImageFormat.Png); }
                org.Close();
                return 0;
            }
            if (args[0] == "--terminal") // --terminal <file>: run the Terminal action
            {
                Settings.Load();
                Ui.TerminalDryRun = true;
                Ui.Terminal(new List<string> { args[1] });
                string check = Ui.LastTerminalScript + "; Write-Output ('CWD=' + (Get-Location).Path); Write-Output ('F=' + $f); Write-Output ('EXISTS=' + (Test-Path -LiteralPath $f))";
                string psOut;
                Ops.Run("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(check)), out psOut);
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "klem_terminal_check.txt"), psOut, Encoding.UTF8);
                Console.WriteLine("written");
                return 0;
            }
            if (args[0] == "--probe") // --probe <word|excel|ppt> <file> <ext> : one Office call with the full stack trace
            {
                Engines.Refresh();
                try
                {
                    var r = args[1] == "excel" ? OfficeOps.Excel(args[2], args[3]) : args[1] == "ppt" ? OfficeOps.PowerPoint(args[2], args[3]) : OfficeOps.Word(args[2], args[3]);
                    Console.WriteLine("OK " + r[0]);
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine(ex.ToString()); return 1; }
            }
            string dir = args[0];
            Directory.CreateDirectory(dir);
            Native.MakeDpiAware();
            Settings.Lang = "en";
            Engines.Refresh();
            Log("ffmpeg=" + Engines.Ffmpeg + " word=" + Engines.Word + " excel=" + Engines.Excel + " ppt=" + Engines.PowerPoint + " edge=" + Engines.Edge);

            var samples = MakeSamples(dir);
            foreach (var f in samples)
            {
                foreach (WheelKind kind in new[] { WheelKind.Convert, WheelKind.Tools })
                {
                    var items = Catalog.Build(kind, new List<string> { f });
                    Log("== " + Path.GetFileName(f) + " / " + kind + ": " + string.Join(", ", items.Select(i => i.Label)));
                    foreach (var it in items)
                    {
                        if (it.OnUi || it.IsMore || it.Label == S.T("read_qr")) continue; // QR is tested on a real code below
                        if (it.Need != null && !Engines.Has(it.Need)) { skip++; continue; }
                        try
                        {
                            var r = it.Run(new List<string> { f });
                            bool ok = r.Errors.Count == 0 && (r.Outputs.Count > 0 || r.ClipboardText != null || r.Message != null) && r.Outputs.All(o => File.Exists(o) || Directory.Exists(o)) &&
                                      r.Outputs.All(o => Ops.SizeOf(o) > 0);
                            if (ok) pass++; else fail++;
                            Log((ok ? "  ok   " : "  FAIL ") + it.Label + " -> " + string.Join(" | ", r.Outputs.Select(o => Path.GetFileName(o) + " (" + Ops.Size(Ops.SizeOf(o)) + ")")) +
                                (r.Errors.Count > 0 ? "  ERR: " + string.Join("; ", r.Errors) : "") + (r.Message != null ? "  MSG: " + r.Message.Replace("\n", " / ") : ""));
                        }
                        catch (Exception ex) { fail++; Log("  FAIL " + it.Label + " EXC: " + Ops.Clean(ex)); }
                    }
                }
            }

            // multi-file tools
            var imgs = samples.Where(s => Catalog.CatOf(s) == Cat.Image).ToList();
            Try("combine pdf", () => ImageOps.CombinePdf(imgs));
            Try("zip all", () => ArchiveOps.ZipAll(samples.Take(3).ToList()));
            Try("hash", () => MiscOps.Hash(samples.Take(2).ToList()));
            // phase 1 tools
            var pdfs = samples.Where(s => Catalog.Ext(s) == "pdf").ToList();
            Try("pdf merge", () => PdfTools.Merge(new List<string> { pdfs[0], pdfs[0] }));
            Try("pdf split", () => Ops.Each(pdfs.Take(1).ToList(), f => PdfTools.Split(f)));
            Try("pdf rotate", () => Ops.Each(pdfs.Take(1).ToList(), f => PdfTools.Rotate(f)));
            Try("pdf extract 2", () => Ops.Each(pdfs.Take(1).ToList(), f => PdfTools.Extract(f, PdfTools.NormalizeRanges("٢", 2))));
            Try("pdf strip info", () => Ops.Each(pdfs.Take(1).ToList(), f => PdfTools.StripInfo(f)));
            Try("pdf to 25%", () => Ops.Each(pdfs.Take(1).ToList(), f => { var o = PdfTools.CompressToSize(f, Ops.SizeOf(f) / 4); Log("     " + Ops.Size(Ops.SizeOf(f)) + " -> " + Ops.Size(Ops.SizeOf(o))); return o; }));
            Try("image to 30 KB", () => Ops.Each(imgs.Take(1).ToList(), f => { var o = ImageOps.ToSize(f, 30 * 1024); if (Ops.SizeOf(o) > 30 * 1024) throw new Exception("too big " + Ops.SizeOf(o)); return o; }));
            Try("collage", () => ImageOps.Collage(imgs.Concat(imgs).ToList()));
            string qr = Path.Combine(dir, "qr.png");
            var writer = new ZXing.BarcodeWriter { Format = ZXing.BarcodeFormat.QR_CODE, Options = new ZXing.QrCode.QrCodeEncodingOptions { Width = 300, Height = 300, Margin = 2, CharacterSet = "UTF-8" } };
            using (var qb = writer.Write("https://example.com/كلمنتينة")) qb.Save(qr, ImageFormat.Png);
            Try("read qr", () => { var r = ImageOps.ReadCodes(new List<string> { qr }); if (r.ClipboardText != "https://example.com/كلمنتينة") throw new Exception("got " + r.ClipboardText); return r; });
            // compression levels (the Compress window's choices)
            for (int lv = 0; lv < 4; lv++)
            {
                int level = lv;
                Try("pdf level " + lv, () => Ops.Compress(pdfs.Take(1).ToList(), f => PdfTools.CompressLevel(f, level)));
                Try("image level " + lv, () => Ops.Compress(imgs.Skip(1).Take(1).ToList(), f => ImageOps.CompressLevel(f, level)));
            }

            // phase 4 conversions
            string svg = Path.Combine(dir, "logo.svg");
            File.WriteAllText(svg,
                "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 120' width='200' height='120'>" +
                "<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#ffb44a'/><stop offset='1' stop-color='#f0601a'/></linearGradient></defs>" +
                "<rect x='4' y='4' width='192' height='112' rx='16' fill='url(#g)'/>" +
                "<circle cx='60' cy='60' r='34' fill='#fff' fill-opacity='.85' stroke='#7a2e0a' stroke-width='4'/>" +
                "<path d='M120 30l40 30-40 30z' fill='#2e8b57'/><path d='M100 100a10 10 0 1110 10' stroke='#000' fill='none'/>" +
                "<g transform='translate(130 105) scale(.8)'><text x='0' y='0' font-size='18' fill='#fff' text-anchor='middle'>كلمنتينة</text></g></svg>");
            Try("svg -> png", () => Ops.Each(new List<string> { svg }, f => ImageOps.ConvertTo(f, "png")));
            if (Engines.Ffmpeg != null) Try("png -> avif", () => Ops.Each(imgs.Take(1).ToList(), f => ImageOps.ConvertTo(f, "avif")));
            string mdSrc = samples.First(s => s.EndsWith("notes.md"));
            Try("md -> epub", () => Ops.Each(new List<string> { mdSrc }, f => Formats.DocTo(f, "epub")));
            string epub = Path.Combine(dir, "notes.epub");
            Try("epub -> md", () => { var r = Ops.Each(new List<string> { epub }, f => Formats.EpubTo(f, "md")); string back = File.ReadAllText(r.Outputs[0]); if (!back.Contains("عنوان")) throw new Exception("round trip lost text: " + back); return r; });
            Try("epub -> txt", () => Ops.Each(new List<string> { epub }, f => Formats.EpubTo(f, "txt")));
            Try("epub -> html", () => Ops.Each(new List<string> { epub }, f => Formats.EpubTo(f, "html")));
            if (Engines.Word)
            {
                Try("epub -> docx", () => Ops.Each(new List<string> { epub }, f => Formats.EpubTo(f, "docx")));
                Try("epub -> pdf", () => Ops.Each(new List<string> { epub }, f => Formats.EpubTo(f, "pdf")));
                var docx = samples.FirstOrDefault(s => s.EndsWith("letter.docx"));
                if (docx != null)
                {
                    Try("docx -> md", () => Ops.Each(new List<string> { docx }, f => Formats.DocTo(f, "md")));
                    Try("docx -> epub", () => Ops.Each(new List<string> { docx }, f => Formats.DocTo(f, "epub")));
                }
            }
            string letterPdf = Directory.GetFiles(dir, "letter.pdf").FirstOrDefault() ?? pdfs[0];
            Try("pdf -> md", () => Ops.Each(new List<string> { letterPdf }, f => Formats.PdfTo(f, "md")));
            Try("pdf -> epub", () => Ops.Each(new List<string> { letterPdf }, f => Formats.PdfTo(f, "epub")));
            if (Engines.Excel) Try("pdf -> xlsx", () => Ops.Each(new List<string> { letterPdf }, f => Formats.PdfTo(f, "xlsx")));
            if (Engines.PowerPoint) Try("pdf -> pptx", () => Ops.Each(new List<string> { letterPdf }, f => Formats.PdfTo(f, "pptx")));

            var media = samples.Where(s => s.EndsWith("clip.mp4") || s.EndsWith("tone.wav")).ToList();
            if (media.Count > 0)
            {
                Try("crop video", () => Ops.Each(media.Take(1).ToList(), f => MediaOps.CropVideo(f, new Rectangle(10, 10, 301, 201))));
                Try("video level 2", () => Ops.Each(media.Take(1).ToList(), f => MediaOps.CompressVideoLevel(f, 2)));
                Try("audio level 2", () => Ops.Each(media.Skip(1).ToList(), f => MediaOps.CompressAudioLevel(f, 2)));
                Try("speed 2x", () => Ops.Each(media, f => MediaOps.Speed(f, 2)));
                Try("speed 0.5x", () => Ops.Each(media, f => MediaOps.Speed(f, 0.5)));
                Try("split 3", () => Ops.Each(media, f => MediaOps.Split(f, 3)));
                Try("join video", () => MediaOps.Join(new List<string> { media[0], media[0] }));
                Try("join audio", () => MediaOps.Join(new List<string> { media[1], media[1] }));
                Try("video to 40 KB", () => Ops.Each(media.Take(1).ToList(), f => MediaOps.VideoToSize(f, 40 * 1024)));
                Try("audio to 20 KB", () => Ops.Each(media.Skip(1).ToList(), f => MediaOps.AudioToSize(f, 20 * 1024)));
                Try("strip media", () => Ops.Each(media, f => MediaOps.StripMetadata(f)));
            }
            if (media.Count > 0) Try("trim 1s-2s", () => Ops.Each(media, f => MediaOps.Trim(f, MediaOps.ParseTime("0:01"), MediaOps.ParseTime("٢"))));

            // wheel snapshots
            var shots = Path.Combine(dir, "_wheels");
            Directory.CreateDirectory(shots);
            var wheel = new WheelForm();
            string png = samples.First(s => s.EndsWith("sample.png"));
            Shot(wheel, shots, "img-convert-en", "en", WheelKind.Convert, new List<string> { png }, 0);
            Shot(wheel, shots, "img-tools-ar", "ar", WheelKind.Tools, new List<string> { png }, 2);
            Shot(wheel, shots, "img-actions-ar", "ar", WheelKind.Actions, new List<string> { png }, -1);
            Shot(wheel, shots, "video-convert-ar", "ar", WheelKind.Convert, new List<string> { Path.Combine(dir, "clip.mp4") }, 1);
            Shot(wheel, shots, "multi-tools-en", "en", WheelKind.Tools, imgs, -2);
            Shot(wheel, shots, "pdf-convert-ar", "ar", WheelKind.Convert, new List<string> { samples.First(s => s.EndsWith(".pdf")) }, 3);
            Shot(wheel, shots, "empty", "ar", WheelKind.Convert, new List<string>(), -1);
            GlyphSheet(Path.Combine(shots, "glyphs.png"));

            Log(string.Format("\nPASS {0}  FAIL {1}  SKIP {2}", pass, fail, skip));
            File.WriteAllText(Path.Combine(dir, "_log.txt"), log.ToString(), Encoding.UTF8);
            Console.WriteLine(log.ToString());
            return fail == 0 ? 0 : 1;
        }

        static void Try(string name, Func<OpResult> f)
        {
            try
            {
                var r = f();
                bool ok = r.Errors.Count == 0;
                if (ok) pass++; else fail++;
                Log((ok ? "  ok   " : "  FAIL ") + name + " -> " + string.Join(" | ", r.Outputs.Select(Path.GetFileName)) + (r.Message != null ? "  MSG: " + r.Message.Replace("\n", " / ") : "") +
                    (r.Errors.Count > 0 ? " ERR " + string.Join("; ", r.Errors) : ""));
            }
            catch (Exception ex) { fail++; Log("  FAIL " + name + " EXC: " + Ops.Clean(ex)); }
        }

        static void Shot(WheelForm w, string dir, string name, string lang, WheelKind kind, List<string> files, int hover)
        {
            Settings.Lang = lang;
            w.Present(kind, files, new Point(400, 400), false);
            w.HoverForTest(hover);
            using (var bmp = w.Snapshot())
            using (var bg = new Bitmap(bmp.Width, bmp.Height))
            {
                using (var g = Graphics.FromImage(bg))
                {
                    using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, bg.Width, bg.Height), Color.FromArgb(240, 140, 80), Color.FromArgb(150, 130, 200), 45f))
                        g.FillRectangle(br, 0, 0, bg.Width, bg.Height);
                    g.DrawImage(bmp, 0, 0);
                }
                bg.Save(Path.Combine(dir, name + ".png"), ImageFormat.Png);
            }
            w.Dismiss();
        }

        static void GlyphSheet(string path)
        {
            var fields = typeof(Glyphs).GetFields();
            using (var bmp = new Bitmap(900, 40 * ((fields.Length + 5) / 6)))
            using (var g = Graphics.FromImage(bmp))
            using (var gf = new Font("Segoe Fluent Icons", 20, GraphicsUnit.Pixel))
            using (var tf = new Font("Segoe UI", 11, GraphicsUnit.Pixel))
            {
                g.Clear(Color.White);
                for (int i = 0; i < fields.Length; i++)
                {
                    int x = (i % 6) * 150, y = (i / 6) * 40;
                    g.DrawString((string)fields[i].GetValue(null), gf, Brushes.Black, x + 4, y + 6);
                    g.DrawString(fields[i].Name, tf, Brushes.DimGray, x + 34, y + 12);
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Log(string s) { log.AppendLine(s); }

        static List<string> MakeSamples(string dir)
        {
            var list = new List<string>();
            // transparent PNG
            string png = Path.Combine(dir, "sample.png");
            using (var b = new Bitmap(640, 400, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(Brushes.Orange, 60, 40, 320, 320);
                    g.FillRectangle(Brushes.SeaGreen, 380, 120, 200, 160);
                    g.DrawString("كلمنتينة Test", new Font("Segoe UI", 28), Brushes.Black, 120, 170);
                }
                b.Save(png, ImageFormat.Png);
            }
            list.Add(png);
            string jpg = Path.Combine(dir, "photo.jpg");
            using (var b = new Bitmap(1200, 800))
            {
                using (var g = Graphics.FromImage(b))
                using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 1200, 800), Color.SkyBlue, Color.DarkOrange, 30f))
                    g.FillRectangle(br, 0, 0, 1200, 800);
                b.Save(jpg, ImageFormat.Jpeg);
            }
            list.Add(jpg);

            // the PDF made from the PNG, using our own writer
            string pdf = Path.Combine(dir, "doc.pdf");
            Pdf.WriteImages(new List<System.Windows.Media.Imaging.BitmapSource> { ImageOps.Load(png), ImageOps.Load(jpg) }, pdf);
            list.Add(pdf);

            string md = Path.Combine(dir, "notes.md");
            File.WriteAllText(md, "# عنوان Title\n\nفقرة **عريضة** و *مائلة* مع `code` و [رابط](https://example.com).\n\n- one\n- two\n\n1. first\n2. second\n\n> quote\n\n```\nx = 1\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n", new UTF8Encoding(false));
            list.Add(md);

            string csv = Path.Combine(dir, "data.csv");
            File.WriteAllText(csv, "name,city,note\nأحمد,عمّان,\"hello, world\"\nSara,Dubai,\"say \"\"hi\"\"\"\n", new UTF8Encoding(false));
            list.Add(csv);

            string srt = Path.Combine(dir, "subs.srt");
            File.WriteAllText(srt, "1\n00:00:01,000 --> 00:00:03,500\nمرحبا\n\n2\n00:00:04,000 --> 00:00:06,000\nSecond line\nwith two lines\n", new UTF8Encoding(false));
            list.Add(srt);

            string folder = Path.Combine(dir, "folder");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(folder, "sub", "b.txt"), "world");
            list.Add(folder);

            string zip = Path.Combine(dir, "pack.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(folder, zip);
            list.Add(zip);

            if (Engines.Word)
            {
                string docx = Path.Combine(dir, "letter.docx");
                dynamic w = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application"));
                dynamic d = w.Documents.Add();
                d.Content.Text = "رسالة تجريبية — Test letter";
                d.SaveAs2(docx, 16); d.Close(0); w.Quit(0);
                list.Add(docx);
            }
            if (Engines.Excel)
            {
                string xlsx = Path.Combine(dir, "sheet.xlsx");
                dynamic x = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application"));
                x.DisplayAlerts = false;
                dynamic wb = x.Workbooks.Add();
                wb.Worksheets[1].Range["A1"].Value = "الاسم"; wb.Worksheets[1].Range["B1"].Value = 42;
                wb.SaveAs(xlsx, 51); wb.Close(false); x.Quit();
                list.Add(xlsx);
            }
            if (Engines.PowerPoint)
            {
                string pptx = Path.Combine(dir, "deck.pptx");
                dynamic p = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application"));
                dynamic pres = p.Presentations.Add(0);
                dynamic s1 = pres.Slides.Add(1, 1);
                s1.Shapes[1].TextFrame.TextRange.Text = "كلمنتينة";
                pres.Slides.Add(2, 1);
                pres.SaveAs(pptx, 24); pres.Close();
                if (p.Presentations.Count == 0) p.Quit();
                list.Add(pptx);
            }
            // let go of every Office object made above and make sure those hidden instances are gone,
            // otherwise the conversions below may attach to a half-closed one
            GC.Collect();
            foreach (var name in new[] { "WINWORD", "EXCEL", "POWERPNT" })
                foreach (var pr in System.Diagnostics.Process.GetProcessesByName(name))
                    try { if (pr.StartTime > started && string.IsNullOrEmpty(pr.MainWindowTitle) && !pr.WaitForExit(8000)) pr.Kill(); } catch { }
            if (Engines.Ffmpeg != null)
            {
                string clip = Path.Combine(dir, "clip.mp4"), tone = Path.Combine(dir, "tone.wav");
                Ops.Ffmpeg("-f lavfi -i testsrc=duration=3:size=640x360:rate=25 -f lavfi -i sine=frequency=440:duration=3 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest " + Ops.Q(clip), clip);
                Ops.Ffmpeg("-f lavfi -i sine=frequency=660:duration=3 " + Ops.Q(tone), tone);
                list.Add(clip); list.Add(tone);
            }
            else File.WriteAllBytes(Path.Combine(dir, "clip.mp4"), new byte[16]); // placeholder only for the wheel snapshot
            return list;
        }
    }
}
