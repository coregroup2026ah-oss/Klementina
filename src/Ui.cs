using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VBFS = Microsoft.VisualBasic.FileIO;

namespace Klementina
{
    /// Actions that need the UI thread (clipboard, dialogs, windows).
    static class Ui
    {
        static OpResult Quiet() { return new OpResult { Quiet = true }; }

        public static OpResult CopyFiles(List<string> files)
        {
            var sc = new StringCollection();
            sc.AddRange(files.ToArray());
            Clipboard.SetFileDropList(sc);
            return new OpResult { Message = S.T("copied") };
        }

        public static OpResult CopyPath(List<string> files)
        {
            Clipboard.SetText(string.Join("\r\n", files));
            return new OpResult { Message = S.T("copied") + "\n" + files[0] + (files.Count > 1 ? " …" : "") };
        }

        public static OpResult MoveTo(List<string> files) { return Transfer(files, true); }
        public static OpResult CopyTo(List<string> files) { return Transfer(files, false); }

        static OpResult Transfer(List<string> files, bool move)
        {
            string dest;
            using (var owner = Owner()) dest = Native.PickFolder(owner.Handle, S.T("pick_dest"));
            if (string.IsNullOrEmpty(dest)) return Quiet();
            Program.RunWorker(() =>
            {
                var r = new OpResult();
                foreach (var f in files)
                {
                    try
                    {
                        string target = Path.Combine(dest, Path.GetFileName(f.TrimEnd('\\')));
                        if (Directory.Exists(f))
                        {
                            if (move) VBFS.FileSystem.MoveDirectory(f, target, VBFS.UIOption.AllDialogs, VBFS.UICancelOption.DoNothing);
                            else VBFS.FileSystem.CopyDirectory(f, target, VBFS.UIOption.AllDialogs, VBFS.UICancelOption.DoNothing);
                        }
                        else
                        {
                            if (move) VBFS.FileSystem.MoveFile(f, target, VBFS.UIOption.AllDialogs, VBFS.UICancelOption.DoNothing);
                            else VBFS.FileSystem.CopyFile(f, target, VBFS.UIOption.AllDialogs, VBFS.UICancelOption.DoNothing);
                        }
                        if (File.Exists(target) || Directory.Exists(target)) r.Outputs.Add(target);
                    }
                    catch (Exception ex) { r.Errors.Add(Path.GetFileName(f) + ": " + Ops.Clean(ex)); }
                }
                return r;
            }, false);
            return Quiet();
        }

        public static string LastTerminalScript; // for self-tests
        public static bool TerminalDryRun;

        public static OpResult Terminal(List<string> files)
        {
            // PowerShell opens in the file's folder with the file(s) ready in $f; the script is passed encoded so
            // Arabic names, spaces and quotes in paths can't break it
            string f = files[0];
            string dir = Directory.Exists(f) && files.Count == 1 ? f : Ops.DirOf(f);
            Func<string, string> lit = s => "'" + s.Replace("'", "''") + "'";
            var script = new System.Text.StringBuilder();
            script.Append("Set-Location -LiteralPath ").Append(lit(dir)).Append("; ");
            if (files.Count == 1) script.Append("$f = ").Append(lit(f)).Append("; ");
            else script.Append("$f = @(").Append(string.Join(",", files.Select(lit))).Append("); ");
            script.Append("Write-Host ''; Write-Host ' Klementina ' -BackgroundColor DarkYellow -ForegroundColor Black -NoNewline; ");
            script.Append("Write-Host (' ' + ").Append(lit(files.Count == 1 ? Path.GetFileName(f.TrimEnd('\\')) : S.F("files_n", files.Count))).Append(" + '   ->   $f') -ForegroundColor Yellow; Write-Host ''");
            LastTerminalScript = script.ToString();
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script.ToString()));
            if (!TerminalDryRun)
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoExit -NoLogo -EncodedCommand " + encoded) { WorkingDirectory = dir, UseShellExecute = true });
            return Quiet();
        }

        public static OpResult Float(List<string> files)
        {
            new FloatForm(files).Show();
            return Quiet();
        }

        public static OpResult Reveal(List<string> files)
        {
            RevealPath(files[0]);
            return Quiet();
        }

        public static void RevealPath(string path)
        {
            try { Process.Start("explorer.exe", "/select," + Ops.Q(path)); } catch { }
        }

        /// Opens a tool window; errors loading the file surface as the usual failure notice.
        public static OpResult Open(Form window)
        {
            window.Show();
            return Quiet();
        }

        public static OpResult OpenWith(List<string> files) { Native.ShellVerb(files[0], "openas"); return Quiet(); }
        public static OpResult Properties(List<string> files) { Native.ShellVerb(files[0], "properties"); return Quiet(); }

        public static OpResult EditFile(List<string> files)
        {
            try { Native.ShellVerb(files[0], "edit"); }
            catch { Process.Start("mspaint.exe", Ops.Q(files[0])); }
            return Quiet();
        }

        public static OpResult Trim(List<string> files)
        {
            string src = files[0];
            using (var dlg = new TrimForm(Path.GetFileName(src)))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return Quiet();
                string start = dlg.Start, end = dlg.End;
                Program.RunWorker(() => Ops.Each(new List<string> { src }, f => MediaOps.Trim(f, start, end)), true);
            }
            return Quiet();
        }

        // ---------------- tools that ask something first ----------------

        static double ParseNumber(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s ?? "")
            {
                if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else if (ch == '٫' || ch == ',') sb.Append('.');
                else if (char.IsDigit(ch) || ch == '.') sb.Append(ch);
            }
            double v;
            return double.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, Ops.Inv, out v) ? v : -1;
        }

        /// "Compress": one window with every option — quality levels, a percentage of the original, or an exact size.
        public static OpResult Compress(List<string> files)
        {
            long total = files.Sum(f => Ops.SizeOf(f));
            var dlg = new KDialog(S.T("compress"));
            dlg.AddText(S.F("compress_now", files.Count == 1 ? Path.GetFileName(files[0]) : S.F("files_n", files.Count), Ops.Size(total)), true);
            var mode = dlg.AddChoices(S.T("compress_how"), new[] { S.T("lv_light"), S.T("lv_balanced"), S.T("lv_strong"), S.T("lv_max"), S.T("by_percent"), S.T("custom_size") }, 1);
            var pct = dlg.AddChoices(S.T("compress_pct"), new[] { "75%", "50%", "25%", "10%" }, 1);
            var mb = dlg.AddInput(S.T("target_mb"), (Math.Max(0.1, total / 1048576.0 / 2 / files.Count)).ToString("0.#", Ops.Inv), 160);
            dlg.AddText(S.T("compress_levels_note"));
            if (files.Any(f => Catalog.Ext(f) == "pdf")) dlg.AddText(S.T("pdf_raster_note"));
            dlg.Check = () => mode() != 5 || ParseNumber(mb.Text) > 0;
            if (!dlg.Ask(S.T("compress"))) return Quiet();
            int m = mode();
            double frac = new[] { 0.75, 0.5, 0.25, 0.10 }[Math.Max(0, pct())];
            double targetMb = ParseNumber(mb.Text);
            Program.RunWorker(() =>
            {
                if (m < 4)
                {
                    // a quality level: keep a result only when it is actually smaller
                    return Ops.Compress(files, f =>
                    {
                        switch (Catalog.CatOf(f))
                        {
                            case Cat.Image: return ImageOps.CompressLevel(f, m);
                            case Cat.Video: return MediaOps.CompressVideoLevel(f, m);
                            case Cat.Audio: return MediaOps.CompressAudioLevel(f, m);
                            case Cat.Pdf: return PdfTools.CompressLevel(f, m);
                        }
                        throw new NotSupportedException(Path.GetFileName(f));
                    });
                }
                var r = Ops.Each(files, f =>
                {
                    long target = m == 4 ? (long)(Ops.SizeOf(f) * frac) : (long)(targetMb * 1048576);
                    switch (Catalog.CatOf(f))
                    {
                        case Cat.Image: return ImageOps.ToSize(f, target);
                        case Cat.Video: return MediaOps.VideoToSize(f, target);
                        case Cat.Audio: return MediaOps.AudioToSize(f, target);
                        case Cat.Pdf: return PdfTools.CompressToSize(f, target);
                    }
                    throw new NotSupportedException(Path.GetFileName(f));
                });
                r.Message = Ops.Savings(files, r.Outputs);
                return r;
            }, true);
            return Quiet();
        }

        public static OpResult Speed(List<string> files)
        {
            var dlg = new KDialog(S.T("speed"));
            var speeds = new[] { 0.25, 0.5, 0.75, 1.25, 1.5, 2, 3, 4 };
            var pick = dlg.AddChoices(S.T("speed_pick"), speeds.Select(v => v.ToString("0.##", Ops.Inv) + "×").ToArray(), 5);
            if (!dlg.Ask(S.T("ok"))) return Quiet();
            double factor = speeds[pick()];
            Program.RunWorker(() => Ops.Each(files, f => MediaOps.Speed(f, factor)), true);
            return Quiet();
        }

        public static OpResult SplitMedia(List<string> files)
        {
            var dlg = new KDialog(S.T("split"));
            var pick = dlg.AddChoices(S.T("split_parts"), new[] { "2", "3", "4", "5", "6", "8", "10" }, 0);
            dlg.AddText(S.T("split_note"));
            if (!dlg.Ask(S.T("split"))) return Quiet();
            int parts = new[] { 2, 3, 4, 5, 6, 8, 10 }[pick()];
            Program.RunWorker(() => Ops.Each(files, f => MediaOps.Split(f, parts)), true);
            return Quiet();
        }

        /// "Split PDF": halves, N equal parts, every N pages, custom ranges, or one file per page.
        public static OpResult SplitPdf(List<string> files)
        {
            string src = files[0];
            int n = PdfTools.PageCount(src);
            var dlg = new KDialog(S.T("split"));
            dlg.AddText(S.F("pdf_has_pages", Path.GetFileName(src), n), true);
            var mode = dlg.AddChoices(S.T("split_how"), new[] { S.T("split_halves"), S.T("split_equal"), S.T("split_every"), S.T("split_ranges"), S.T("split_each") }, 0);
            var parts = dlg.AddInput(S.T("split_parts_n"), "3", 120);
            var every = dlg.AddInput(S.T("split_every_n"), Math.Max(1, n / 4).ToString(), 120);
            var ranges = dlg.AddInput(S.T("split_ranges_hint"), n > 1 ? "1-" + (n / 2) + ", " + (n / 2 + 1) + "-" + n : "1", 300);
            List<string> list = null;
            dlg.Check = () =>
            {
                switch (mode())
                {
                    case 0: list = PdfTools.EqualParts(n, 2); return n >= 2;
                    case 1: { int k = (int)ParseNumber(parts.Text); if (k < 2 || k > n) return false; list = PdfTools.EqualParts(n, k); return true; }
                    case 2: { int k = (int)ParseNumber(every.Text); if (k < 1 || k >= n) return false; list = null; return true; }
                    case 3:
                    {
                        list = new List<string>();
                        foreach (var part in ranges.Text.Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string r = PdfTools.NormalizeRanges(part, n);
                            if (r == null) return false;
                            list.Add(r);
                        }
                        return list.Count > 0;
                    }
                }
                list = null;
                return true;
            };
            if (!dlg.Ask(S.T("split"))) return Quiet();
            int m = mode();
            int everyN = (int)ParseNumber(every.Text);
            var chosen = list;
            Program.RunWorker(() => Ops.Each(new List<string> { src }, f =>
                m == 4 ? PdfTools.Split(f) : m == 2 ? PdfTools.SplitEvery(f, everyN) : PdfTools.SplitRanges(f, chosen)), true);
            return Quiet();
        }

        public static OpResult ExtractPages(List<string> files)
        {
            string src = files[0];
            int n = PdfTools.PageCount(src);
            var dlg = new KDialog(S.T("extract_pages"));
            dlg.AddText(S.F("pdf_has_pages", Path.GetFileName(src), n));
            var box = dlg.AddInput(S.T("pages_hint"), n > 1 ? "1-" + Math.Min(n, 3) : "1", 240);
            string ranges = null;
            dlg.Check = () => (ranges = PdfTools.NormalizeRanges(box.Text, n)) != null;
            if (!dlg.Ask(S.T("ok"))) return Quiet();
            Program.RunWorker(() => Ops.Each(files, f => PdfTools.Extract(f, ranges)), true);
            return Quiet();
        }

        /// An invisible top-most window we can force to the foreground and use as a dialog owner.
        public static Form Owner()
        {
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Size = new Size(1, 1), Location = Cursor.Position, TopMost = true, Opacity = 0,
            };
            f.Show();
            ForceForeground(f.Handle);
            return f;
        }

        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);

        public static void ForceForeground(IntPtr h)
        {
            uint fg = GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
            bool attached = fg != me && AttachThreadInput(me, fg, true);
            SetForegroundWindow(h);
            BringWindowToTop(h);
            if (attached) AttachThreadInput(me, fg, false);
        }

        public static void ApplyDirection(Form f)
        {
            f.RightToLeft = S.Rtl ? RightToLeft.Yes : RightToLeft.No;
            f.RightToLeftLayout = S.Rtl;
        }
    }

    /// Reads the files selected in File Explorer or on the desktop.
    static class ExplorerSel
    {
        static bool IsExplorerClass(string cls)
        {
            return cls == "CabinetWClass" || cls == "ExploreWClass" || cls == "Progman" || cls == "WorkerW";
        }

        /// Selection in the front-most File Explorer window (windows are enumerated top to bottom in z-order).
        public static List<string> TopmostExplorer()
        {
            var res = new List<string>();
            Native.EnumWindowsAll(h =>
            {
                if (res.Count == 0 && Native.IsWindowVisible(h) && Native.ClassOf(h) == "CabinetWClass")
                {
                    var sel = Of(h);
                    if (sel.Count > 0) res.AddRange(sel);
                    else res.Add(null); // stop at the first Explorer window even if nothing is selected
                }
            });
            res.RemoveAll(p => p == null);
            return res;
        }

        public static List<string> AtPoint(Point screenPt)
        {
            IntPtr h = Native.WindowFromPoint(new Native.POINT(screenPt.X, screenPt.Y));
            return Of(Native.GetAncestor(h, Native.GA_ROOT));
        }

        public static List<string> Of(IntPtr root)
        {
            var res = new List<string>();
            string cls = Native.ClassOf(root);
            if (!IsExplorerClass(cls)) return res;
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic doc = null;
                if (cls == "Progman" || cls == "WorkerW")
                {
                    dynamic wins = shell.Windows();
                    object loc = 0, empty = null;
                    int hw = 0;
                    dynamic desk = wins.FindWindowSW(ref loc, ref empty, 8, ref hw, 1);
                    doc = desk.Document;
                }
                else
                {
                    dynamic fallback = null;
                    foreach (dynamic w in shell.Windows())
                    {
                        try
                        {
                            if (new IntPtr((long)w.HWND) != root) continue;
                            if (fallback == null) fallback = w;
                            IntPtr tab = Native.TabWindowOf((object)w);
                            if (tab != IntPtr.Zero && Native.IsWindowVisible(tab)) { doc = w.Document; break; }
                        }
                        catch { }
                    }
                    if (doc == null && fallback != null) doc = fallback.Document;
                }
                if (doc != null)
                    foreach (dynamic item in doc.SelectedItems())
                    {
                        string p = item.Path;
                        if (!string.IsNullOrEmpty(p) && (File.Exists(p) || Directory.Exists(p))) res.Add(p);
                    }
            }
            catch { }
            return res;
        }
    }

    /// The app's tangerine artwork, drawn in code.
    static class Art
    {
        public static Bitmap Fruit(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float s = size;
                var body = new RectangleF(s * 0.08f, s * 0.20f, s * 0.84f, s * 0.76f);
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(body);
                    using (var pg = new PathGradientBrush(path))
                    {
                        pg.CenterPoint = new PointF(s * 0.38f, s * 0.42f);
                        pg.CenterColor = Color.FromArgb(255, 190, 92);
                        pg.SurroundColors = new[] { Color.FromArgb(240, 98, 18) };
                        g.FillPath(pg, path);
                    }
                }
                // highlight
                using (var hb = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                    g.FillEllipse(hb, s * 0.24f, s * 0.32f, s * 0.20f, s * 0.13f);
                // leaf
                using (var leaf = new GraphicsPath())
                {
                    leaf.AddBezier(s * 0.50f, s * 0.24f, s * 0.58f, s * 0.02f, s * 0.86f, s * 0.04f, s * 0.92f, s * 0.10f);
                    leaf.AddBezier(s * 0.92f, s * 0.10f, s * 0.84f, s * 0.30f, s * 0.62f, s * 0.32f, s * 0.50f, s * 0.24f);
                    using (var lb = new LinearGradientBrush(new RectangleF(s * 0.5f, 0, s * 0.45f, s * 0.35f), Color.FromArgb(118, 196, 74), Color.FromArgb(46, 140, 52), 45f))
                        g.FillPath(lb, leaf);
                }
                // stem
                using (var p = new Pen(Color.FromArgb(92, 64, 30), Math.Max(1f, s * 0.05f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, s * 0.48f, s * 0.26f, s * 0.46f, s * 0.14f);
            }
            return bmp;
        }

        public static Icon AppIcon(int size)
        {
            using (var b = Fruit(size)) return Icon.FromHandle(b.GetHicon());
        }

        // ---------------- installer artwork ----------------

        static readonly Color Ember = Color.FromArgb(255, 112, 32), Peel = Color.FromArgb(255, 166, 77);

        /// Writes the installer images (background, side panel, small corner image) at several DPI sizes.
        public static void WriteInstallerArt(string dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var s in new[] { new Size(596, 432), new Size(994, 720), new Size(1272, 922), new Size(1630, 1148) })
                using (var b = Background(s.Width, s.Height)) b.Save(Path.Combine(dir, "back_" + s.Width + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            foreach (var s in new[] { new Size(202, 386), new Size(336, 643), new Size(430, 824), new Size(534, 1022) })
                using (var b = Panel(s.Width, s.Height)) b.Save(Path.Combine(dir, "panel_" + s.Width + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            foreach (int s in new[] { 58, 97, 124, 159 })
                using (var b = Fruit(s)) b.Save(Path.Combine(dir, "small_" + s + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }

        static Graphics Hq(Bitmap b)
        {
            var g = Graphics.FromImage(b);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            return g;
        }

        static void Glow(Graphics g, PointF c, float r, Color color, int alpha)
        {
            using (var p = new GraphicsPath())
            {
                p.AddEllipse(c.X - r, c.Y - r, 2 * r, 2 * r);
                using (var br = new PathGradientBrush(p) { CenterColor = Color.FromArgb(alpha, color), SurroundColors = new[] { Color.FromArgb(0, color) } })
                    g.FillPath(br, p);
            }
        }

        /// The radial-menu motif: eight rounded segments around a hub.
        static void WheelMotif(Graphics g, PointF c, float r, Color fill, Color line)
        {
            float hub = r * 0.36f, pen = r * 0.07f, gap = r * 0.05f + pen;
            float ro = r - pen / 2, ri = hub + pen / 2 + r * 0.05f;
            for (int i = 0; i < 8; i++)
            {
                double center = -90 + i * 45.0;
                double dO = Math.Asin(gap / 2 / ro) * 180 / Math.PI, dI = Math.Asin(gap / 2 / ri) * 180 / Math.PI;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(c.X - ro, c.Y - ro, 2 * ro, 2 * ro, (float)(center - 22.5 + dO), (float)(45 - 2 * dO));
                    path.AddArc(c.X - ri, c.Y - ri, 2 * ri, 2 * ri, (float)(center + 22.5 - dI), (float)-(45 - 2 * dI));
                    path.CloseFigure();
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var p = new Pen(fill, pen) { LineJoin = LineJoin.Round }) g.DrawPath(p, path);
                }
            }
            using (var p = new Pen(line, Math.Max(1f, r * 0.012f))) g.DrawEllipse(p, c.X - r - pen, c.Y - r - pen, 2 * (r + pen), 2 * (r + pen));
        }

        static void DrawFruit(Graphics g, RectangleF where, float shadow)
        {
            using (var f = Fruit((int)Math.Ceiling(where.Width)))
            {
                if (shadow > 0)
                    Glow(g, new PointF(where.X + where.Width / 2, where.Y + where.Height * 0.62f + shadow), where.Width * 0.5f, Color.Black, 120);
                g.DrawImage(f, where);
            }
        }

        static Bitmap Background(int w, int h)
        {
            var b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Hq(b))
            {
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(24, 14, 10), Color.FromArgb(52, 22, 10), 35f))
                    g.FillRectangle(br, 0, 0, w, h);
                Glow(g, new PointF(w * 0.92f, h * 1.02f), w * 0.62f, Ember, 120);
                Glow(g, new PointF(w * 0.05f, h * -0.1f), w * 0.35f, Peel, 28);
                var c = new PointF(w * 0.90f, h * 0.93f);
                float r = h * 0.50f;
                WheelMotif(g, c, r, Color.FromArgb(14, 255, 214, 170), Color.FromArgb(22, 255, 200, 150));
                // a few warm specks
                var rnd = new Random(7);
                for (int i = 0; i < 40; i++)
                {
                    float x = (float)rnd.NextDouble() * w, y = (float)rnd.NextDouble() * h, s = (float)(rnd.NextDouble() * 2.2 + 0.6) * h / 432f;
                    using (var sb = new SolidBrush(Color.FromArgb(rnd.Next(18, 60), 255, 190, 120))) g.FillEllipse(sb, x, y, s, s);
                }
            }
            return b;
        }

        static Bitmap Panel(int w, int h)
        {
            var b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            float u = w / 202f;
            using (var g = Hq(b))
            {
                using (var br = new LinearGradientBrush(new Rectangle(0, 0, w, h), Color.FromArgb(255, 160, 60), Color.FromArgb(150, 52, 10), 90f))
                {
                    var blend = new ColorBlend { Positions = new[] { 0f, 0.55f, 1f }, Colors = new[] { Color.FromArgb(255, 172, 72), Color.FromArgb(240, 98, 22), Color.FromArgb(120, 40, 8) } };
                    br.InterpolationColors = blend;
                    g.FillRectangle(br, 0, 0, w, h);
                }
                Glow(g, new PointF(w * 0.5f, h * 0.30f), w * 0.85f, Color.FromArgb(255, 236, 200), 120);
                var c = new PointF(w * 0.5f, h * 0.34f);
                WheelMotif(g, c, w * 0.44f, Color.FromArgb(34, 255, 255, 255), Color.FromArgb(60, 255, 255, 255));
                float fs = w * 0.52f;
                DrawFruit(g, new RectangleF(c.X - fs / 2, c.Y - fs / 2, fs, fs), 6 * u);

                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                {
                    using (var f = new Font("Segoe UI", 30 * u, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        using (var sh = new SolidBrush(Color.FromArgb(70, 80, 20, 0)))
                            g.DrawString("كلمنتينة", f, sh, new RectangleF(0, h * 0.62f + 2 * u, w, 44 * u), sf);
                        g.DrawString("كلمنتينة", f, Brushes.White, new RectangleF(0, h * 0.62f, w, 44 * u), sf);
                    }
                    using (var f = new Font("Segoe UI Semibold", 15 * u, GraphicsUnit.Pixel))
                    using (var tb = new SolidBrush(Color.FromArgb(235, 255, 246, 236)))
                        g.DrawString("K L E M E N T I N A", f, tb, new RectangleF(0, h * 0.62f + 40 * u, w, 22 * u), sf);
                    using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), 1.2f * u))
                        g.DrawLine(p, w * 0.36f, h * 0.62f + 68 * u, w * 0.64f, h * 0.62f + 68 * u);
                    using (var f = new Font("Segoe UI", 11.5f * u, GraphicsUnit.Pixel))
                    using (var tb = new SolidBrush(Color.FromArgb(225, 255, 244, 232)))
                    {
                        g.DrawString("حوّل ملفاتك بلمسة وأنت تسحبها", f, tb, new RectangleF(0, h * 0.62f + 78 * u, w, 20 * u), sf);
                        g.DrawString("Convert files where you drag them", f, tb, new RectangleF(0, h * 0.62f + 96 * u, w, 20 * u), sf);
                    }
                    // the maker, quietly at the foot of the panel
                    using (var f = new Font("Segoe UI", 9.5f * u, GraphicsUnit.Pixel))
                    using (var tb = new SolidBrush(Color.FromArgb(175, 255, 236, 218)))
                    {
                        g.DrawString("بواسطة بيدر للتكنولوجيا", f, tb, new RectangleF(0, h - 40 * u, w, 16 * u), sf);
                        g.DrawString("by Bayder Technology", f, tb, new RectangleF(0, h - 24 * u, w, 16 * u), sf);
                    }
                }
            }
            return b;
        }

        /// Multi-size .ico, used by the build to give the exe its icon.
        public static void WriteIco(string path)
        {
            int[] sizes = { 256, 64, 48, 32, 24, 16 };
            var pngs = sizes.Select(sz =>
            {
                using (var b = Fruit(sz)) using (var ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); return ms.ToArray(); }
            }).ToList();
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(off); off += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }
    }

    class TrimForm : Form
    {
        readonly TextBox start = new TextBox(), end = new TextBox();
        public string Start, End;

        public TrimForm(string fileName)
        {
            Text = S.T("trim_title") + " — " + fileName;
            Font = new Font("Segoe UI", 10f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(380, 170);
            Ui.ApplyDirection(this);

            var l1 = new Label { Text = S.T("trim_start"), Location = new Point(16, 14), AutoSize = true };
            start.SetBounds(16, 38, 348, 26); start.Text = "0";
            var l2 = new Label { Text = S.T("trim_end"), Location = new Point(16, 72), AutoSize = true };
            end.SetBounds(16, 96, 348, 26);
            var ok = new Button { Text = S.T("ok"), DialogResult = DialogResult.OK, Location = new Point(264, 132), Size = new Size(100, 30) };
            ok.Click += (s, e) =>
            {
                try { Start = MediaOps.ParseTime(start.Text); End = MediaOps.ParseTime(end.Text); if (Start == "") Start = "0"; }
                catch { DialogResult = DialogResult.None; start.Focus(); System.Media.SystemSounds.Beep.Play(); }
            };
            Controls.AddRange(new Control[] { l1, start, l2, end, ok });
            AcceptButton = ok;
            Shown += (s, e) => { Ui.ForceForeground(Handle); start.SelectAll(); start.Focus(); };
        }
    }

    /// "Float": keeps files in a small always-on-top shelf you can drag them back out of.
    class FloatForm : Form
    {
        readonly List<string> files;
        readonly Bitmap thumb;
        Point dragStart;
        bool pressedOnImage;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public FloatForm(List<string> files)
        {
            this.files = files;
            float sc = Native.ScaleAt(Cursor.Position);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Settings.DarkMode ? Color.FromArgb(36, 33, 31) : Color.FromArgb(255, 250, 245);
            thumb = Native.Thumbnail(files[0], 256, false) ?? Native.Thumbnail(files[0], 256, true);
            int w = (int)(200 * sc), h = (int)(200 * sc);
            if (thumb != null && thumb.Width > 0)
            {
                float ar = thumb.Width / (float)thumb.Height;
                if (ar > 1) h = (int)(w / ar + 34 * sc); else { h = (int)(200 * sc + 34 * sc); w = Math.Max((int)(140 * sc), (int)(200 * sc * ar)); }
            }
            Size = new Size(w, h);
            Location = new Point(Cursor.Position.X - w / 2, Cursor.Position.Y - h / 2);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { dragStart = e.Location; pressedOnImage = e.Y < Height - 34 * sc; } };
            MouseMove += OnMove;
            MouseDoubleClick += (s, e) => { try { Process.Start(new ProcessStartInfo(files[0]) { UseShellExecute = true }); } catch { } };
            MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && new Rectangle(Width - (int)(26 * sc), 0, (int)(26 * sc), (int)(26 * sc)).Contains(e.Location)) Close(); };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;
            try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        void OnMove(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - dragStart.X) < 5 && Math.Abs(e.Y - dragStart.Y) < 5) return;
            if (pressedOnImage)
            {
                var data = new DataObject(DataFormats.FileDrop, files.ToArray());
                DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                files.RemoveAll(f => !File.Exists(f) && !Directory.Exists(f));
                if (files.Count == 0) Close();
            }
            else
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // move the window by its caption strip
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float sc = DeviceDpi / 96f;
            int cap = (int)(34 * sc);
            var area = new Rectangle(6, 6, Width - 12, Height - cap - 6);
            if (thumb != null)
            {
                float ar = thumb.Width / (float)thumb.Height;
                float w = area.Width, h = w / ar;
                if (h > area.Height) { h = area.Height; w = h * ar; }
                g.DrawImage(thumb, area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            }
            Color fc = Settings.DarkMode ? Color.FromArgb(240, 235, 228) : Color.FromArgb(58, 36, 22);
            string name = files.Count == 1 ? Path.GetFileName(files[0].TrimEnd('\\')) : S.F("files_n", files.Count);
            using (var f = new Font("Segoe UI Semibold", 9f))
            using (var b = new SolidBrush(fc))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(name, f, b, new RectangleF(8, Height - cap, Width - 16, cap), sf);
            using (var f = new Font("Segoe UI", 10f))
            using (var b = new SolidBrush(Color.FromArgb(150, fc)))
                g.DrawString("✕", f, b, Width - 22 * sc, 3 * sc);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (thumb != null) thumb.Dispose();
            base.OnFormClosed(e);
        }
    }
}
