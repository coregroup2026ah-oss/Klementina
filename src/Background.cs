using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Klementina
{
    /// "Add background": put the image on a colour/gradient/photo backdrop with padding, rounded corners and a shadow.
    class BackgroundEditor : EditorWindow
    {
        class Backdrop { public Color A, B; public float Angle; public Bitmap Photo; }

        static readonly List<Backdrop> Presets = new List<Backdrop>
        {
            G(255, 200, 92, 255, 112, 32, 45), G(120, 220, 255, 60, 140, 255, 90), G(255, 240, 250, 190, 200, 255, 60), G(255, 255, 255, 220, 236, 255, 90),
            G(16, 60, 56, 0, 24, 30, 120), G(255, 150, 130, 255, 90, 120, 45), G(110, 190, 255, 80, 110, 240, 45), G(40, 210, 180, 30, 140, 170, 45),
            G(170, 110, 255, 120, 60, 220, 60), G(255, 120, 170, 255, 70, 110, 30), G(250, 250, 250, 250, 250, 250, 0), G(30, 30, 34, 30, 30, 34, 0),
            G(40, 60, 90, 40, 60, 90, 0), G(130, 80, 210, 130, 80, 210, 0), G(230, 70, 70, 230, 70, 70, 0), G(240, 150, 40, 240, 150, 40, 0),
        };
        static Backdrop G(int r1, int g1, int b1, int r2, int g2, int b2, float ang) { return new Backdrop { A = Color.FromArgb(r1, g1, b1), B = Color.FromArgb(r2, g2, b2), Angle = ang }; }

        Backdrop backdrop = Presets[0];
        TrackBar padding, corners, shadow;
        Func<int> ratio;
        readonly Bitmap small;
        Bitmap shown;
        readonly Timer debounce = new Timer { Interval = 30 };

        public BackgroundEditor(string path) : base(S.T("add_bg"), path)
        {
            small = Shrink(Source, 1200);
            debounce.Tick += (s, e) => { debounce.Stop(); RefreshPreview(); };
            Action changed = () => { debounce.Stop(); debounce.Start(); };

            AddSection(S.T("background"));
            AddBackdrops(changed);
            AddSideButton(S.T("bg_photo"), () =>
            {
                using (var d = new OpenFileDialog { Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.heic;*.tif;*.tiff" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                        try { backdrop = new Backdrop { Photo = ImageOps.ToGdi(ImageOps.Load(d.FileName)) }; changed(); } catch { }
            });
            AddSection(S.T("layout"));
            padding = AddSlider(S.T("padding"), 0, 40, 10, v => v + "%", changed);
            corners = AddSlider(S.T("corners"), 0, 25, 4, v => v + "%", changed);
            shadow = AddSlider(S.T("shadow"), 0, 100, 40, v => v.ToString(), changed);
            ratio = AddSegments(S.T("ratio"), new[] { S.T("auto"), "1:1", "16:9", "4:5", "9:16" }, 0, changed);
            RefreshPreview();
        }

        static Bitmap Shrink(Bitmap src, int max)
        {
            double f = Math.Min(1.0, (double)max / Math.Max(src.Width, src.Height));
            var b = new Bitmap(Math.Max(1, (int)(src.Width * f)), Math.Max(1, (int)(src.Height * f)), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, b.Width, b.Height); }
            return b;
        }

        void AddBackdrops(Action changed)
        {
            var row = new FlowLayoutPanel { Width = SideInner, AutoSize = true, MaximumSize = new Size(SideInner, 0), Margin = new Padding(0, 0, 0, 6) };
            var buttons = new List<Button>();
            for (int i = 0; i < Presets.Count; i++)
            {
                var p = Presets[i];
                var b = new Button { Width = 52, Height = 40, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 6, 6) };
                b.FlatAppearance.BorderSize = i == 0 ? 3 : 1;
                b.FlatAppearance.BorderColor = Color.FromArgb(240, 96, 20);
                b.Paint += (s, e) =>
                {
                    var r = ((Button)s).ClientRectangle; r.Inflate(-3, -3);
                    using (var br = new LinearGradientBrush(r, p.A, p.B, p.Angle)) e.Graphics.FillRectangle(br, r);
                };
                b.Click += (s, e) =>
                {
                    backdrop = p;
                    foreach (var o in buttons) o.FlatAppearance.BorderSize = o == s ? 3 : 1;
                    changed();
                };
                buttons.Add(b);
                row.Controls.Add(b);
            }
            side.Controls.Add(row);
        }

        class BgLayout { public Backdrop Bg; public float Pad, Corner, Shadow; public int Ratio; }

        BgLayout Current() { return new BgLayout { Bg = backdrop, Pad = padding.Value / 100f, Corner = corners.Value / 100f, Shadow = shadow.Value / 100f, Ratio = ratio() }; }

        static Bitmap Compose(Bitmap img, BgLayout l)
        {
            float m = Math.Max(img.Width, img.Height);
            float pad = l.Pad * m;
            float W = img.Width + 2 * pad, H = img.Height + 2 * pad;
            float[] ratios = { 0, 1f, 16f / 9, 4f / 5, 9f / 16 };
            float r = ratios[l.Ratio];
            if (r > 0) { if (W / H < r) W = H * r; else H = W / r; }
            var outBmp = new Bitmap((int)Math.Ceiling(W), (int)Math.Ceiling(H), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(outBmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var all = new RectangleF(0, 0, outBmp.Width, outBmp.Height);
                if (l.Bg.Photo != null)
                {
                    float s = Math.Max(all.Width / l.Bg.Photo.Width, all.Height / l.Bg.Photo.Height);
                    float pw = l.Bg.Photo.Width * s, ph = l.Bg.Photo.Height * s;
                    g.DrawImage(l.Bg.Photo, (all.Width - pw) / 2, (all.Height - ph) / 2, pw, ph);
                }
                else using (var br = new LinearGradientBrush(all, l.Bg.A, l.Bg.B, l.Bg.Angle)) g.FillRectangle(br, all);

                var dest = new RectangleF((outBmp.Width - img.Width) / 2f, (outBmp.Height - img.Height) / 2f, img.Width, img.Height);
                float radius = l.Corner * Math.Min(img.Width, img.Height);
                // soft shadow: stacked translucent rounded rects, growing and fading
                if (l.Shadow > 0)
                {
                    float spread = m * 0.035f * l.Shadow;
                    for (int i = 12; i >= 1; i--)
                    {
                        float k = i / 12f;
                        var sr = dest; sr.Inflate(spread * k, spread * k); sr.Offset(0, spread * 0.45f);
                        using (var sp = WheelForm.RoundRect(sr, radius + spread * k))
                        using (var sb = new SolidBrush(Color.FromArgb((int)(10 * l.Shadow * (1.2f - k)), 0, 0, 0)))
                            g.FillPath(sb, sp);
                    }
                }
                using (var clip = radius > 0.5f ? WheelForm.RoundRect(dest, radius) : null)
                {
                    var st = g.Save();
                    if (clip != null) g.SetClip(clip);
                    g.DrawImage(img, dest);
                    g.Restore(st);
                }
            }
            return outBmp;
        }

        protected override void RefreshPreview()
        {
            if (padding == null || IsDisposed || Disposing) return;
            var old = shown;
            shown = Compose(small, Current());
            canvas.Image = shown;
            canvas.Invalidate();
            if (old != null) old.Dispose();
        }

        BgLayout atSave;
        protected override void BeforeSave() { atSave = Current(); }
        protected override Bitmap RenderFull(Bitmap source) { return Compose(source, atSave); }
        protected override string OutputSuffix { get { return "bg"; } }
        protected override string OutputExt { get { return "png"; } }

        protected override void ResetAll() { padding.Value = 10; corners.Value = 4; shadow.Value = 40; }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            debounce.Stop();
            debounce.Dispose();
            small.Dispose();
            if (shown != null) shown.Dispose();
            base.OnFormClosed(e);
        }
    }
}
