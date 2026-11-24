using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Klementina
{
    /// The radial menu. A per-pixel-alpha layered window that never takes focus and is an OLE drop target.
    class WheelForm : Form
    {
        public WheelKind Kind;
        public List<string> Files = new List<string>();
        public bool KeyboardMode;
        public event Action<OpItem, List<string>> Execute;

        List<OpItem> all = new List<OpItem>();
        List<OpItem> shown = new List<OpItem>();
        int page, hover = -1;
        const int MaxSegments = 8;
        const int Hub = -2, Outside = -1;

        float sc = 1;
        int R, rHub, pad, chipsH, W, H;
        PointF C;
        bool dark;
        Bitmap thumb;
        static string glyphFont;

        readonly Timer pageTimer = new Timer { Interval = 650 };

        public WheelForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AllowDrop = true;
            TopMost = true;
            pageTimer.Tick += (s, e) => { pageTimer.Stop(); if (HoveredIsMore()) { NextPage(1); Render(); } };
            var h = Handle; // create now so the window can be shown instantly mid-drag
            if (glyphFont == null)
            {
                using (var fonts = new InstalledFontCollection())
                    glyphFont = fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        // ---------------- public API ----------------

        public void Present(WheelKind kind, List<string> files, Point cursor, bool keyboard)
        {
            Kind = kind;
            KeyboardMode = keyboard;
            dark = Settings.DarkMode;
            SetFiles(files ?? new List<string>());
            hover = keyboard && shown.Count > 0 ? 0 : Outside;

            sc = Native.ScaleAt(cursor);
            R = (int)(150 * sc); rHub = (int)(50 * sc); pad = (int)(22 * sc); chipsH = (int)(46 * sc);
            W = 2 * (R + pad); H = 2 * (R + pad) + chipsH;
            C = new PointF(W / 2f, pad + R);

            var wa = Screen.FromPoint(cursor).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(cursor.X - (int)C.X, wa.Right - W));
            int y = Math.Max(wa.Top, Math.Min(cursor.Y - (int)C.Y, wa.Bottom - H));
            Bounds = new Rectangle(x, y, W, H);
            Render();
            if (!Visible) Show();
        }

        public void SwitchKind(WheelKind k)
        {
            if (k == Kind) return;
            Kind = k;
            all = Catalog.Build(Kind, Files);
            page = 0;
            Paginate();
            hover = KeyboardMode && shown.Count > 0 ? 0 : Outside;
            Render();
        }

        public void Dismiss()
        {
            pageTimer.Stop();
            if (Visible) Hide();
            KeyboardMode = false;
            hover = Outside;
        }

        public void Nav(Keys k)
        {
            if (!Visible || !KeyboardMode) return;
            int n = shown.Count;
            switch (k)
            {
                case Keys.Right: case Keys.Down: if (n > 0) hover = hover < 0 ? 0 : (hover + 1) % n; break;
                case Keys.Left: case Keys.Up: if (n > 0) hover = hover < 0 ? n - 1 : (hover - 1 + n) % n; break;
                case Keys.Tab: SwitchKind((WheelKind)(((int)Kind + 1) % 3)); return;
                case Keys.PageDown: NextPage(1); break;
                case Keys.PageUp: NextPage(-1); break;
                case Keys.Escape: Dismiss(); return;
                case Keys.Return: case Keys.Space: Activate(hover); return;
            }
            Render();
        }

        // ---------------- items & paging ----------------

        void SetFiles(List<string> files)
        {
            Files = files;
            all = Catalog.Build(Kind, Files);
            page = 0;
            Paginate();
            LoadThumb();
        }

        int Pages { get { return all.Count <= MaxSegments ? 1 : (int)Math.Ceiling(all.Count / (double)(MaxSegments - 1)); } }

        void Paginate()
        {
            if (all.Count <= MaxSegments) { shown = all.ToList(); return; }
            int per = MaxSegments - 1;
            shown = all.Skip(page * per).Take(per).ToList();
            shown.Add(new OpItem { IsMore = true, Label = S.T("more"), Glyph = "" });
        }

        void NextPage(int dir)
        {
            if (Pages <= 1) return;
            page = (page + dir + Pages) % Pages;
            Paginate();
            if (hover >= shown.Count) hover = shown.Count - 1;
        }

        bool HoveredIsMore() { return hover >= 0 && hover < shown.Count && shown[hover].IsMore; }

        void Activate(int idx)
        {
            if (idx < 0 || idx >= shown.Count) { if (idx == Hub) Dismiss(); return; }
            var item = shown[idx];
            if (item.IsMore) { NextPage(1); Render(); return; }
            var files = Files.ToList();
            Dismiss();
            if (Execute != null) BeginInvoke(new Action(() => Execute(item, files)));
        }

        void LoadThumb()
        {
            if (thumb != null) { thumb.Dispose(); thumb = null; }
            if (Files.Count == 0) return;
            thumb = Native.Thumbnail(Files[0], 128, false) ?? Native.Thumbnail(Files[0], 128, true);
        }

        // ---------------- hit testing ----------------

        int HitTest(Point client)
        {
            float dx = client.X - C.X, dy = client.Y - C.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= rHub + 6 * sc) return Hub;
            if (d > R + 6 * sc) return Outside;
            int n = shown.Count;
            if (n == 0) return Outside;
            double span = 360.0 / n;
            double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
            double a = ((ang + 90 + span / 2) % 360 + 360) % 360;
            return Math.Min(n - 1, (int)(a / span));
        }

        void SetHover(int idx)
        {
            if (idx == hover) return;
            hover = idx;
            pageTimer.Stop();
            if (HoveredIsMore()) pageTimer.Start();
            Render();
        }

        // ---------------- drag & drop ----------------

        static List<string> FilesFrom(IDataObject data)
        {
            try
            {
                if (data != null && data.GetDataPresent(DataFormats.FileDrop))
                {
                    var arr = data.GetData(DataFormats.FileDrop) as string[];
                    if (arr != null && arr.Length > 0) return arr.ToList();
                }
            }
            catch { }
            return null;
        }

        DragDropEffects EffectFor(DragEventArgs e)
        {
            if (Files.Count == 0 || hover < 0 || hover >= shown.Count || shown[hover].IsMore) return DragDropEffects.None;
            // Never report Move: the source would delete the original.
            if ((e.AllowedEffect & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((e.AllowedEffect & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            return DragDropEffects.None;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            var f = FilesFrom(e.Data);
            if (f != null && !f.SequenceEqual(Files, StringComparer.OrdinalIgnoreCase))
            {
                SetFiles(f);
                hover = Outside;
            }
            SetHover(HitTest(PointToClient(new Point(e.X, e.Y))));
            Render();
            e.Effect = EffectFor(e);
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            SetHover(HitTest(PointToClient(new Point(e.X, e.Y))));
            e.Effect = EffectFor(e);
        }

        protected override void OnDragLeave(EventArgs e) { SetHover(Outside); }

        protected override void OnDragDrop(DragEventArgs e)
        {
            var f = FilesFrom(e.Data);
            if (f != null) Files = f;
            int idx = HitTest(PointToClient(new Point(e.X, e.Y)));
            e.Effect = EffectFor(e);
            Activate(idx);
            Dismiss();
        }

        // ---------------- mouse (keyboard mode) ----------------

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (KeyboardMode) SetHover(HitTest(e.Location));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!KeyboardMode || e.Button != MouseButtons.Left) return;
            int idx = HitTest(e.Location);
            if (idx == Outside) Dismiss();
            else Activate(idx);
        }

        // ---------------- painting ----------------

        Color DiscColor { get { return dark ? Color.FromArgb(232, 38, 34, 32) : Color.FromArgb(226, 255, 249, 243); } }
        Color TextColor { get { return dark ? Color.FromArgb(242, 236, 228) : Color.FromArgb(58, 36, 22); } }
        static readonly Color Tangerine1 = Color.FromArgb(255, 166, 77), Tangerine2 = Color.FromArgb(255, 112, 32);

        public void Render()
        {
            if (W <= 0) return;
            using (var bmp = Snapshot()) Apply(bmp);
        }

        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                Draw(g);
            }
            return bmp;
        }

        /// Test hook: hover a segment without a mouse.
        public void HoverForTest(int idx) { hover = idx; }

        void Draw(Graphics g)
        {
            // soft shadow
            float sr = R + pad * 0.85f;
            using (var sp = new GraphicsPath())
            {
                sp.AddEllipse(C.X - sr, C.Y - sr + 5 * sc, 2 * sr, 2 * sr);
                using (var pg = new PathGradientBrush(sp))
                {
                    pg.CenterColor = Color.FromArgb(dark ? 140 : 80, 40, 20, 0);
                    pg.SurroundColors = new[] { Color.FromArgb(0, 40, 20, 0) };
                    pg.FocusScales = new PointF(0.80f, 0.80f);
                    g.FillPath(pg, sp);
                }
            }

            // disc
            var disc = new RectangleF(C.X - R, C.Y - R, 2 * R, 2 * R);
            using (var b = new SolidBrush(DiscColor)) g.FillEllipse(b, disc);
            using (var p = new Pen(dark ? Color.FromArgb(50, 255, 255, 255) : Color.FromArgb(200, 255, 255, 255), 1.5f * sc)) g.DrawEllipse(p, disc);

            int n = shown.Count;
            if (n == 0) DrawEmpty(g);
            for (int i = 0; i < n; i++) DrawSegment(g, i, n);
            DrawHub(g);
            DrawChips(g);
        }

        void DrawEmpty(Graphics g)
        {
            string msg = Files.Count == 0 ? S.T("drop_here") : S.T("no_options");
            using (var font = new Font("Segoe UI Semibold", 13 * sc, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.FromArgb(160, TextColor)))
            using (var sf = Centered())
                g.DrawString(msg, font, b, new RectangleF(C.X - R, C.Y - R * 0.78f, 2 * R, R * 0.4f), sf);
        }

        static StringFormat Centered()
        {
            return new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        }

        void DrawSegment(Graphics g, int i, int n)
        {
            var item = shown[i];
            bool hot = i == hover;
            bool missing = item.Need != null && !Engines.Has(item.Need);
            float penW = 12 * sc;
            float ro = R - 9 * sc - penW / 2, ri = rHub + 9 * sc + penW / 2;
            double span = 360.0 / n, center = -90 + i * span;
            float gap = 6 * sc + penW;

            using (var path = new GraphicsPath())
            {
                if (n == 1)
                {
                    path.AddEllipse(C.X - ro, C.Y - ro, 2 * ro, 2 * ro);
                    path.AddEllipse(C.X - ri, C.Y - ri, 2 * ri, 2 * ri);
                }
                else
                {
                    double dO = Math.Asin(gap / 2 / ro) * 180 / Math.PI, dI = Math.Asin(gap / 2 / ri) * 180 / Math.PI;
                    double a0 = center - span / 2, a1 = center + span / 2;
                    path.AddArc(C.X - ro, C.Y - ro, 2 * ro, 2 * ro, (float)(a0 + dO), (float)(span - 2 * dO));
                    path.AddArc(C.X - ri, C.Y - ri, 2 * ri, 2 * ri, (float)(a1 - dI), (float)-(span - 2 * dI));
                    path.CloseFigure();
                }

                var bounds = path.GetBounds();
                bounds.Inflate(penW, penW);
                Color c1, c2;
                if (hot && !missing) { c1 = Tangerine1; c2 = Tangerine2; }
                else if (hot) { c1 = dark ? Color.FromArgb(90, 84, 80) : Color.FromArgb(232, 226, 220); c2 = c1; }
                else if (dark) { c1 = Color.FromArgb(250, 66, 58, 53); c2 = Color.FromArgb(250, 54, 48, 44); }
                else { c1 = Color.FromArgb(250, 255, 244, 232); c2 = Color.FromArgb(250, 255, 228, 206); }
                using (var br = new LinearGradientBrush(bounds, c1, c2, LinearGradientMode.Vertical))
                using (var pen = new Pen(br, penW) { LineJoin = LineJoin.Round })
                {
                    g.FillPath(br, path);
                    if (n > 1) g.DrawPath(pen, path);
                }
            }

            // label
            double rad = center * Math.PI / 180;
            float rm = (ro + ri) / 2 + 2 * sc;
            float cx = C.X + (float)(rm * Math.Cos(rad)), cy = C.Y + (float)(rm * Math.Sin(rad));
            if (n == 1) { cx = C.X; cy = C.Y - rm; }
            float boxW = (float)Math.Min(2 * rm * Math.Sin(Math.Min(Math.PI / 2, span * Math.PI / 360)) * 0.92, ro - ri + 30 * sc);
            Color tc = hot && !missing ? Color.White : Color.FromArgb(missing ? 115 : 255, TextColor);

            using (var sf = Centered())
            using (var tb = new SolidBrush(tc))
            {
                if (item.IsFormat)
                {
                    float size = (item.Label.Length > 4 ? 12.5f : 15f) * sc;
                    using (var f = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel))
                        g.DrawString(item.Label, f, tb, new RectangleF(cx - boxW / 2, cy - 14 * sc, boxW, 28 * sc), sf);
                }
                else
                {
                    using (var gf = new Font(glyphFont, 17 * sc, GraphicsUnit.Pixel))
                        g.DrawString(item.Glyph ?? "", gf, tb, new RectangleF(cx - 20 * sc, cy - 26 * sc, 40 * sc, 26 * sc), sf);
                    using (var f = new Font("Segoe UI Semibold", 10.5f * sc, GraphicsUnit.Pixel))
                    {
                        if (S.Rtl) sf.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
                        g.DrawString(item.Label, f, tb, new RectangleF(cx - boxW / 2, cy + 1 * sc, boxW, 30 * sc), sf);
                    }
                }
                if (missing)
                {
                    using (var f = new Font("Segoe UI", 8.5f * sc, GraphicsUnit.Pixel))
                    using (var mb = new SolidBrush(Color.FromArgb(150, TextColor)))
                        g.DrawString(item.Need == "ffmpeg" ? "FFmpeg" : "Office", f, mb, new RectangleF(cx - boxW / 2, cy + (item.IsFormat ? 12 : 26) * sc, boxW, 14 * sc), sf);
                }
            }
        }

        void DrawHub(Graphics g)
        {
            float r = rHub;
            using (var b = new SolidBrush(dark ? Color.FromArgb(245, 28, 26, 24) : Color.FromArgb(250, 255, 255, 255)))
                g.FillEllipse(b, C.X - r, C.Y - r, 2 * r, 2 * r);
            if (hover == Hub)
                using (var p = new Pen(Color.FromArgb(200, Tangerine2), 2.5f * sc)) g.DrawEllipse(p, C.X - r, C.Y - r, 2 * r, 2 * r);

            if (thumb != null)
            {
                float tw = r * 1.15f;
                float ar = thumb.Width / (float)thumb.Height;
                float w = ar >= 1 ? tw : tw * ar, h = ar >= 1 ? tw / ar : tw;
                var rect = new RectangleF(C.X - w / 2, C.Y - h / 2 - 6 * sc, w, h);
                if (Files.Count > 1)
                {
                    using (var back = RoundRect(new RectangleF(rect.X + 5 * sc, rect.Y - 4 * sc, rect.Width, rect.Height), 6 * sc))
                    using (var bb = new SolidBrush(dark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(70, 120, 70, 30)))
                        g.FillPath(bb, back);
                }
                using (var clip = RoundRect(rect, 6 * sc))
                {
                    var st = g.Save();
                    g.SetClip(clip);
                    g.DrawImage(thumb, rect);
                    g.Restore(st);
                }
            }

            // pill: size / count / cancel
            string pill;
            if (hover == Hub) pill = S.T("cancel");
            else if (Files.Count == 0) pill = "";
            else if (Files.Count > 1) pill = S.F("files_n", Files.Count);
            else if (Directory.Exists(Files[0])) pill = S.T("folder");
            else pill = Ops.Size(Ops.SizeOf(Files[0]));
            if (pill.Length > 0)
            {
                using (var f = new Font("Segoe UI Semibold", 10.5f * sc, GraphicsUnit.Pixel))
                {
                    var sz = g.MeasureString(pill, f);
                    var pr = new RectangleF(C.X - sz.Width / 2 - 9 * sc, C.Y + r * 0.42f, sz.Width + 18 * sc, 21 * sc);
                    using (var pp = RoundRect(pr, pr.Height / 2))
                    using (var pb = new SolidBrush(hover == Hub ? Tangerine2 : Color.FromArgb(215, 36, 28, 22)))
                        g.FillPath(pb, pp);
                    using (var sf = Centered())
                        g.DrawString(pill, f, Brushes.White, pr, sf);
                }
            }

            // page dots
            if (Pages > 1)
            {
                float dy = C.Y - r - 0 * sc, step = 9 * sc, x0 = C.X - (Pages - 1) * step / 2;
                for (int i = 0; i < Pages; i++)
                    using (var db = new SolidBrush(i == page ? Tangerine2 : Color.FromArgb(90, TextColor)))
                        g.FillEllipse(db, x0 + i * step - 2.5f * sc, C.Y + r + 3 * sc, 5 * sc, 5 * sc);
            }
        }

        void DrawChips(Graphics g)
        {
            var kinds = new[] { WheelKind.Convert, WheelKind.Tools, WheelKind.Actions };
            if (S.Rtl) Array.Reverse(kinds);
            using (var fKey = new Font("Segoe UI", 10f * sc, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fName = new Font("Segoe UI Semibold", 11f * sc, GraphicsUnit.Pixel))
            {
                var widths = new List<float>();
                foreach (var k in kinds)
                {
                    float kw = g.MeasureString(Settings.KeyLabel(Settings.KeyFor(k)), fKey).Width;
                    float nw = g.MeasureString(S.T(KindKey(k)), fName).Width;
                    widths.Add(kw + nw + 22 * sc);
                }
                float gapX = 6 * sc, total = widths.Sum() + gapX * (kinds.Length - 1);
                float x = C.X - total / 2, y = C.Y + R + pad * 0.55f, h = 26 * sc;
                for (int i = 0; i < kinds.Length; i++)
                {
                    var k = kinds[i];
                    bool on = k == Kind;
                    var rect = new RectangleF(x, y, widths[i], h);
                    using (var path = RoundRect(rect, h / 2))
                    {
                        Color bg = on ? Tangerine2 : (dark ? Color.FromArgb(235, 52, 47, 43) : Color.FromArgb(235, 255, 251, 247));
                        using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                        if (!on) using (var p = new Pen(Color.FromArgb(dark ? 40 : 30, dark ? 255 : 120, dark ? 255 : 60, dark ? 255 : 20), 1f * sc)) g.DrawPath(p, path);
                    }
                    Color fc = on ? Color.White : TextColor;
                    string keyText = Settings.KeyLabel(Settings.KeyFor(k)), name = S.T(KindKey(k));
                    float kw = g.MeasureString(keyText, fKey).Width;
                    using (var b = new SolidBrush(Color.FromArgb(on ? 220 : 140, fc)))
                    using (var nb = new SolidBrush(fc))
                    using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                    {
                        float inner = x + 9 * sc;
                        if (S.Rtl)
                        {
                            g.DrawString(name, fName, nb, new RectangleF(inner, y, widths[i] - kw - 18 * sc, h), sf);
                            g.DrawString(keyText, fKey, b, new RectangleF(x + widths[i] - 9 * sc - kw, y, kw + 2, h), sf);
                        }
                        else
                        {
                            g.DrawString(keyText, fKey, b, new RectangleF(inner, y, kw + 2, h), sf);
                            g.DrawString(name, fName, nb, new RectangleF(inner + kw + 4 * sc, y, widths[i], h), sf);
                        }
                    }
                    x += widths[i] + gapX;
                }
            }
        }

        static string KindKey(WheelKind k) { return k == WheelKind.Convert ? "convert" : k == WheelKind.Tools ? "tools" : "actions"; }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        void Apply(Bitmap bmp)
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            IntPtr mem = Native.CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(mem, hbmp);
            try
            {
                var size = new Native.SIZE(bmp.Width, bmp.Height);
                var src = new Native.POINT(0, 0);
                var dst = new Native.POINT(Left, Top);
                var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
                Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(mem, old);
                Native.DeleteObject(hbmp);
                Native.DeleteDC(mem);
                Native.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
