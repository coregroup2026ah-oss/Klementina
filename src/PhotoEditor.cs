using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Klementina
{
    /// Light & colour adjustments with a live preview (Exposure, Brightness, Contrast, Highlights, Shadows, Whites, Blacks,
    /// Gamma, Temperature, Tint, Saturation, Vibrance).
    class PhotoEditor : EditorWindow
    {
        class Params
        {
            public double Exposure, Brightness, Contrast, Highlights, Shadows, Whites, Blacks, Gamma, Temperature, Tint, Saturation, Vibrance;
            public bool IsIdentity
            {
                get { return Exposure == 0 && Brightness == 0 && Contrast == 0 && Highlights == 0 && Shadows == 0 && Whites == 0 && Blacks == 0 && Gamma == 0 && Temperature == 0 && Tint == 0 && Saturation == 0 && Vibrance == 0; }
            }
        }

        readonly Bitmap small;    // downscaled copy for the live preview
        Bitmap shown;
        readonly Timer debounce = new Timer { Interval = 40 };
        TrackBar exposure, brightness, contrast, highlights, shadows, whites, blacks, gamma, temperature, tint, saturation, vibrance;

        public PhotoEditor(string path) : base(S.T("edit_photo"), path)
        {
            small = Downscale(Source, 1400);
            debounce.Tick += (s, e) => { debounce.Stop(); RefreshPreview(); };
            Action changed = () => { debounce.Stop(); debounce.Start(); };
            Func<int, string> signed = v => (v > 0 ? "+" : "") + v;

            AddSection(S.T("light"));
            exposure = AddSlider(S.T("exposure"), -200, 200, 0, v => (v / 100.0).ToString("+0.00;-0.00;0.00", Ops.Inv) + " EV", changed);
            brightness = AddSlider(S.T("brightness"), -100, 100, 0, signed, changed);
            contrast = AddSlider(S.T("contrast"), -100, 100, 0, signed, changed);
            highlights = AddSlider(S.T("highlights"), -100, 100, 0, signed, changed);
            shadows = AddSlider(S.T("shadows"), -100, 100, 0, signed, changed);
            whites = AddSlider(S.T("whites"), -100, 100, 0, signed, changed);
            blacks = AddSlider(S.T("blacks"), -100, 100, 0, signed, changed);
            gamma = AddSlider(S.T("gamma"), -100, 100, 0, signed, changed);
            AddSection(S.T("color"));
            temperature = AddSlider(S.T("temperature"), -100, 100, 0, signed, changed);
            tint = AddSlider(S.T("tint"), -100, 100, 0, signed, changed);
            saturation = AddSlider(S.T("saturation"), -100, 100, 0, signed, changed);
            vibrance = AddSlider(S.T("vibrance"), -100, 100, 0, signed, changed);
            AddSection(S.T("quick"));
            AddSideButton(S.T("auto_enhance"), AutoEnhance);
            AddSideButton(S.T("black_white"), () => { saturation.Value = -100; });
            RefreshPreview();
        }

        static Bitmap Downscale(Bitmap src, int max)
        {
            double f = Math.Min(1.0, (double)max / Math.Max(src.Width, src.Height));
            var b = new Bitmap(Math.Max(1, (int)(src.Width * f)), Math.Max(1, (int)(src.Height * f)), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(src, 0, 0, b.Width, b.Height); }
            return b;
        }

        Params Current()
        {
            return new Params
            {
                Exposure = exposure.Value / 100.0, Brightness = brightness.Value / 100.0, Contrast = contrast.Value / 100.0,
                Highlights = highlights.Value / 100.0, Shadows = shadows.Value / 100.0, Whites = whites.Value / 100.0, Blacks = blacks.Value / 100.0,
                Gamma = gamma.Value / 100.0, Temperature = temperature.Value / 100.0, Tint = tint.Value / 100.0,
                Saturation = saturation.Value / 100.0, Vibrance = vibrance.Value / 100.0,
            };
        }

        protected override void RefreshPreview()
        {
            if (exposure == null || IsDisposed || Disposing) return;
            var old = shown;
            shown = Apply(small, Current());
            canvas.Image = shown;
            canvas.Invalidate();
            if (old != null) old.Dispose();
        }

        protected override Bitmap RenderFull(Bitmap source) { return Apply(source, paramsAtSave); }

        Params paramsAtSave;
        protected override void BeforeSave() { paramsAtSave = Current(); }

        protected override void ResetAll()
        {
            foreach (var t in new[] { exposure, brightness, contrast, highlights, shadows, whites, blacks, gamma, temperature, tint, saturation, vibrance }) t.Value = 0;
        }

        /// Stretches levels to the 0.5%/99.5% percentiles and adds a little vibrance.
        void AutoEnhance()
        {
            var hist = new int[256];
            var d = small.LockBits(new Rectangle(0, 0, small.Width, small.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new byte[d.Stride * d.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            small.UnlockBits(d);
            int n = 0;
            for (int i = 0; i < px.Length; i += 4) { hist[(px[i] * 29 + px[i + 1] * 150 + px[i + 2] * 77) >> 8]++; n++; }
            int lo = 0, hi = 255, acc = 0;
            while (lo < 255 && (acc += hist[lo]) < n * 0.005) lo++;
            acc = 0;
            while (hi > 0 && (acc += hist[hi]) < n * 0.005) hi--;
            blacks.Value = Math.Max(-100, Math.Min(100, (int)(lo / 255.0 / 0.25 * 100)));
            whites.Value = Math.Max(-100, Math.Min(100, (int)((255 - hi) / 255.0 / 0.25 * 100)));
            vibrance.Value = 25;
        }

        static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        static Bitmap Apply(Bitmap src, Params p)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            var sd = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new byte[sd.Stride * sd.Height];
            Marshal.Copy(sd.Scan0, px, 0, px.Length);
            src.UnlockBits(sd);

            if (!p.IsIdentity)
            {
                double expo = Math.Pow(2, p.Exposure);
                double contrast = p.Contrast >= 0 ? 1 + p.Contrast * 1.5 : 1 + p.Contrast * 0.9;
                double bp = p.Blacks * 0.25, wp = 1 - p.Whites * 0.25;
                if (wp - bp < 0.05) wp = bp + 0.05;
                double gammaExp = 1.0 / (1 + p.Gamma * 0.8);
                double temp = p.Temperature * 0.12, tint = p.Tint * 0.10;

                // tone curve depends only on the channel value, so precompute it
                var lut = new double[256];
                for (int v = 0; v < 256; v++)
                {
                    double c = v / 255.0 * expo;
                    c += p.Brightness * 0.25;
                    c = (c - 0.5) * contrast + 0.5;
                    c = (c - bp) / (wp - bp);
                    c = Math.Pow(Clamp(c), gammaExp);
                    lut[v] = c;
                }

                for (int i = 0; i < px.Length; i += 4)
                {
                    double b = lut[px[i]], g = lut[px[i + 1]], r = lut[px[i + 2]];
                    r += temp; b -= temp; g -= tint; r += tint * 0.5; b += tint * 0.5;
                    double L = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    if (p.Shadows != 0 || p.Highlights != 0)
                    {
                        double sh = (1 - Clamp(L)); sh *= sh;
                        double hl = Clamp(L); hl *= hl;
                        double delta = p.Shadows * sh * 0.45 + p.Highlights * hl * 0.45;
                        r += delta; g += delta; b += delta;
                        L += delta;
                    }
                    if (p.Saturation != 0 || p.Vibrance != 0)
                    {
                        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
                        double sat = mx - mn;
                        double k = 1 + p.Saturation + p.Vibrance * (1 - Clamp(sat)) * 1.2;
                        r = L + (r - L) * k; g = L + (g - L) * k; b = L + (b - L) * k;
                    }
                    px[i] = (byte)(Clamp(b) * 255 + 0.5);
                    px[i + 1] = (byte)(Clamp(g) * 255 + 0.5);
                    px[i + 2] = (byte)(Clamp(r) * 255 + 0.5);
                }
            }
            var dd = dst.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, dd.Scan0, px.Length);
            dst.UnlockBits(dd);
            return dst;
        }

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
