using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;

namespace Klementina
{
    static class ImageOps
    {
        // ---------- load / save ----------

        public static WMI.BitmapSource Load(string path)
        {
            if (Catalog.Ext(path) == "svg") return Materialize(Svg.Render(path));
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var dec = WMI.BitmapDecoder.Create(fs, WMI.BitmapCreateOptions.PreservePixelFormat, WMI.BitmapCacheOption.OnLoad);
                    WMI.BitmapFrame fr = dec.Frames[0];
                    ushort orient = 1;
                    try
                    {
                        var md = fr.Metadata as WMI.BitmapMetadata;
                        if (md != null)
                        {
                            object v = md.GetQuery("System.Photo.Orientation");
                            if (v is ushort) orient = (ushort)v;
                        }
                    }
                    catch { }
                    WMI.BitmapSource s = new WMI.FormatConvertedBitmap(fr, WM.PixelFormats.Bgra32, null, 0);
                    s = Orient(s, orient);
                    s = Materialize(s);
                    return s;
                }
            }
            catch (Exception)
            {
                // Fall back to FFmpeg for formats Windows has no codec for (e.g. AVIF/HEIC without the store extensions).
                if (Engines.Ffmpeg == null) throw;
                string tmp = Path.Combine(Ops.TempDir(), "decoded.png");
                Ops.Ffmpeg("-i " + Ops.Q(path) + " -frames:v 1 " + Ops.Q(tmp), tmp);
                try { return Load(tmp); } finally { Ops.TryDelete(Path.GetDirectoryName(tmp)); }
            }
        }

        static WMI.BitmapSource Orient(WMI.BitmapSource s, ushort o)
        {
            WM.Transform t;
            switch (o)
            {
                case 2: t = new WM.ScaleTransform(-1, 1); break;
                case 3: t = new WM.RotateTransform(180); break;
                case 4: t = new WM.ScaleTransform(1, -1); break;
                case 5: t = Group(new WM.RotateTransform(90), new WM.ScaleTransform(-1, 1)); break;
                case 6: t = new WM.RotateTransform(90); break;
                case 7: t = Group(new WM.RotateTransform(270), new WM.ScaleTransform(-1, 1)); break;
                case 8: t = new WM.RotateTransform(270); break;
                default: return s;
            }
            return new WMI.TransformedBitmap(s, t);
        }

        static WM.Transform Group(params WM.Transform[] ts)
        {
            var g = new WM.TransformGroup();
            foreach (var t in ts) g.Children.Add(t);
            return g;
        }

        /// Copies pixels into a plain, frozen Bgra32 bitmap so later steps never touch the decoder again.
        static WMI.BitmapSource Materialize(WMI.BitmapSource s)
        {
            if (s.Format != WM.PixelFormats.Bgra32) s = new WMI.FormatConvertedBitmap(s, WM.PixelFormats.Bgra32, null, 0);
            int w = s.PixelWidth, h = s.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            s.CopyPixels(px, stride, 0);
            var b = WMI.BitmapSource.Create(w, h, 96, 96, WM.PixelFormats.Bgra32, null, px, stride);
            b.Freeze();
            return b;
        }

        static byte[] Pixels(WMI.BitmapSource s)
        {
            var px = new byte[s.PixelWidth * 4 * s.PixelHeight];
            s.CopyPixels(px, s.PixelWidth * 4, 0);
            return px;
        }

        static bool HasAlpha(byte[] px)
        {
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) return true;
            return false;
        }

        /// Composites onto white and returns a 24-bit image (for JPEG/BMP/PDF).
        public static WMI.BitmapSource Flatten(WMI.BitmapSource s)
        {
            int w = s.PixelWidth, h = s.PixelHeight;
            byte[] px = Pixels(s);
            int stride3 = (w * 3 + 3) & ~3;
            var o = new byte[stride3 * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4, j = y * stride3 + x * 3;
                    int a = px[i + 3];
                    o[j] = (byte)((px[i] * a + 255 * (255 - a)) / 255);
                    o[j + 1] = (byte)((px[i + 1] * a + 255 * (255 - a)) / 255);
                    o[j + 2] = (byte)((px[i + 2] * a + 255 * (255 - a)) / 255);
                }
            var b = WMI.BitmapSource.Create(w, h, 96, 96, WM.PixelFormats.Bgr24, null, o, stride3);
            b.Freeze();
            return b;
        }

        public static byte[] Encode(WMI.BitmapSource s, string ext, int jpegQuality)
        {
            WMI.BitmapEncoder enc;
            switch (ext)
            {
                case "jpg":
                case "jpeg":
                    var j = new WMI.JpegBitmapEncoder { QualityLevel = jpegQuality };
                    s = Flatten(s);
                    enc = j;
                    break;
                case "bmp": enc = new WMI.BmpBitmapEncoder(); s = Flatten(s); break;
                case "gif": enc = new WMI.GifBitmapEncoder(); break;
                case "tif":
                case "tiff": enc = new WMI.TiffBitmapEncoder { Compression = WMI.TiffCompressOption.Lzw }; break;
                default: enc = new WMI.PngBitmapEncoder(); break;
            }
            enc.Frames.Add(WMI.BitmapFrame.Create(s));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        static string SaveAs(WMI.BitmapSource s, string dest, string ext, int quality)
        {
            switch (ext)
            {
                case "ico": File.WriteAllBytes(dest, Ico(s)); break;
                case "pdf": Pdf.WriteImages(new List<WMI.BitmapSource> { s }, dest); break;
                case "heic":
                {
                    // Windows' HEIF encoder (from the "HEIF Image Extensions" store app), when present
                    var enc = WMI.BitmapEncoder.Create(HeifContainer);
                    enc.Frames.Add(WMI.BitmapFrame.Create(Flatten(s)));
                    using (var fs = File.Create(dest)) enc.Save(fs);
                    break;
                }
                case "avif":
                {
                    string tmpA = Path.Combine(Ops.TempDir(), "src.png");
                    try
                    {
                        File.WriteAllBytes(tmpA, Encode(s, "png", 100));
                        int crf = Math.Max(18, Math.Min(50, 63 - quality / 2));
                        Ops.Ffmpeg("-i " + Ops.Q(tmpA) + " -c:v libaom-av1 -still-picture 1 -crf " + crf + " -b:v 0 -cpu-used 6 -pix_fmt yuv420p " + Ops.Q(dest), dest);
                    }
                    finally { Ops.TryDelete(Path.GetDirectoryName(tmpA)); }
                    break;
                }
                case "webp":
                    string tmp = Path.Combine(Ops.TempDir(), "src.png");
                    try
                    {
                        File.WriteAllBytes(tmp, Encode(s, "png", 100));
                        Ops.Ffmpeg("-i " + Ops.Q(tmp) + " -c:v libwebp -quality " + quality + " " + Ops.Q(dest), dest);
                    }
                    finally { Ops.TryDelete(Path.GetDirectoryName(tmp)); }
                    break;
                default: File.WriteAllBytes(dest, Encode(s, ext, quality)); break;
            }
            return dest;
        }

        public static readonly Guid HeifContainer = new Guid("e1e62521-6787-405b-a339-500715b5763f");

        /// True when Windows can write HEIC (needs the HEIF Image Extensions).
        public static bool CanWriteHeic()
        {
            // creating the encoder can succeed even when nothing can write HEIF, so encode a 2×2 test image
            try
            {
                var enc = WMI.BitmapEncoder.Create(HeifContainer);
                var px = new byte[2 * 2 * 3];
                enc.Frames.Add(WMI.BitmapFrame.Create(WMI.BitmapSource.Create(2, 2, 96, 96, WM.PixelFormats.Bgr24, null, px, 6)));
                using (var ms = new MemoryStream()) { enc.Save(ms); return ms.Length > 0; }
            }
            catch { return false; }
        }

        // ---------- GDI+ bridge for the editor windows ----------

        public static System.Drawing.Bitmap ToGdi(WMI.BitmapSource s)
        {
            using (var ms = new MemoryStream(Encode(s, "png", 100)))
            using (var b = new System.Drawing.Bitmap(ms))
                return new System.Drawing.Bitmap(b);
        }

        public static WMI.BitmapSource FromGdi(System.Drawing.Bitmap b)
        {
            using (var ms = new MemoryStream())
            {
                b.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var dec = new WMI.PngBitmapDecoder(ms, WMI.BitmapCreateOptions.PreservePixelFormat, WMI.BitmapCacheOption.OnLoad);
                return Materialize(dec.Frames[0]);
            }
        }

        public static void SaveGdi(System.Drawing.Bitmap b, string dest, string ext)
        {
            SaveAs(FromGdi(b), dest, ext, Math.Max(Settings.JpegQuality, 92));
        }

        public static string SameFormatExt(string src)
        {
            string e = Catalog.Ext(src);
            if (e == "jpeg" || e == "jfif") return "jpg";
            if (e == "png" || e == "jpg" || e == "bmp" || e == "gif" || e == "tif" || e == "tiff") return e;
            return "png"; // webp/heic/avif can't be written natively — keep a lossless copy
        }

        // ---------- conversions ----------

        public static List<string> ConvertTo(string src, string ext)
        {
            var img = Load(src);
            return new List<string> { SaveAs(img, Ops.Out(src, ext), ext, Settings.JpegQuality) };
        }

        // ---------- tools ----------

        public static OpResult Compress(List<string> files)
        {
            return Ops.Compress(files, src =>
            {
                var img = Load(src);
                const int max = 2560;
                int longest = Math.Max(img.PixelWidth, img.PixelHeight);
                if (longest > max) img = Scale(img, (double)max / longest);
                bool alpha = HasAlpha(Pixels(img));
                string ext = alpha ? "png" : "jpg";
                return SaveAs(img, Ops.Out(src, ext, "compressed"), ext, Math.Min(Settings.JpegQuality, 78));
            });
        }

        static WMI.BitmapSource Scale(WMI.BitmapSource s, double f)
        {
            return Materialize(new WMI.TransformedBitmap(s, new WM.ScaleTransform(f, f)));
        }

        public static string Half(string src)
        {
            var img = Scale(Load(src), 0.5);
            string ext = SameFormatExt(src);
            return SaveAs(img, Ops.Out(src, ext, "small"), ext, Settings.JpegQuality);
        }

        public static string Rotate(string src)
        {
            var img = Materialize(new WMI.TransformedBitmap(Load(src), new WM.RotateTransform(90)));
            string ext = SameFormatExt(src);
            return SaveAs(img, Ops.Out(src, ext, "rotated"), ext, 92);
        }

        public static string Flip(string src)
        {
            var img = Materialize(new WMI.TransformedBitmap(Load(src), new WM.ScaleTransform(-1, 1)));
            string ext = SameFormatExt(src);
            return SaveAs(img, Ops.Out(src, ext, "flipped"), ext, 92);
        }

        public static string Grayscale(string src)
        {
            var img = Load(src);
            byte[] px = Pixels(img);
            for (int i = 0; i < px.Length; i += 4)
            {
                byte l = (byte)Math.Round(0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2]);
                px[i] = px[i + 1] = px[i + 2] = l;
            }
            var g = WMI.BitmapSource.Create(img.PixelWidth, img.PixelHeight, 96, 96, WM.PixelFormats.Bgra32, null, px, img.PixelWidth * 4);
            g.Freeze();
            string ext = SameFormatExt(src);
            return SaveAs(g, Ops.Out(src, ext, "gray"), ext, 92);
        }

        /// Re-encodes without any EXIF/GPS/camera metadata (orientation is baked into the pixels).
        public static string Strip(string src)
        {
            string ext = SameFormatExt(src);
            return SaveAs(Load(src), Ops.Out(src, ext, "clean"), ext, 95);
        }

        public static OpResult CombinePdf(List<string> files)
        {
            var r = new OpResult();
            var imgs = new List<WMI.BitmapSource>();
            foreach (var f in files)
            {
                try { imgs.Add(Load(f)); }
                catch (Exception ex) { r.Errors.Add(Path.GetFileName(f) + ": " + Ops.Clean(ex)); }
            }
            if (imgs.Count == 0) return r;
            string dest = files.Count == 1 ? Ops.Out(files[0], "pdf") : Ops.Unique(Ops.DirOf(files[0]), Ops.BaseName(files[0]) + " +" + (files.Count - 1), "pdf");
            Pdf.WriteImages(imgs, dest);
            r.Outputs.Add(dest);
            return r;
        }

        // level: 0 light, 1 balanced, 2 strong, 3 maximum
        static readonly int[] LvMax = { 4000, 2560, 1920, 1280 }, LvQ = { 85, 75, 62, 48 };

        public static string CompressLevel(string src, int level)
        {
            var img = Load(src);
            int longest = Math.Max(img.PixelWidth, img.PixelHeight);
            if (longest > LvMax[level]) img = Scale(img, (double)LvMax[level] / longest);
            // keep transparency as PNG (lossless, so only resizing helps there); everything else becomes JPEG
            bool alpha = HasAlpha(Pixels(img));
            string ext = alpha ? "png" : "jpg";
            return SaveAs(img, Ops.Out(src, ext, "compressed"), ext, LvQ[level]);
        }

        /// JPEG under targetBytes: binary-search the quality, then shrink the image if quality alone isn't enough.
        public static string ToSize(string src, long targetBytes)
        {
            var img = Flatten(Load(src));
            byte[] best = null;
            for (int round = 0; round < 8 && best == null; round++)
            {
                int lo = 20, hi = 95;
                byte[] fit = null;
                while (lo <= hi)
                {
                    int q = (lo + hi) / 2;
                    byte[] data = Encode(img, "jpg", q);
                    if (data.Length <= targetBytes) { fit = data; lo = q + 1; } else hi = q - 1;
                }
                if (fit != null) best = fit;
                else img = Scale(img, 0.8); // still too big at the lowest quality: make it smaller and try again
            }
            if (best == null) best = Encode(img, "jpg", 20);
            string dest = Ops.Out(src, "jpg", "compressed");
            File.WriteAllBytes(dest, best);
            return dest;
        }

        /// A tidy grid of the images on a white card, about 2400 px wide.
        public static OpResult Collage(List<string> files)
        {
            var imgs = files.Select(Load).ToList();
            int n = imgs.Count;
            int cols = (int)Math.Ceiling(Math.Sqrt(n));
            int rows = (int)Math.Ceiling(n / (double)cols);
            const int W = 2400, gap = 24;
            int cell = (W - gap * (cols + 1)) / cols;
            int H = gap + rows * (cell + gap);
            var dv = new WM.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(WM.Brushes.White, null, new System.Windows.Rect(0, 0, W, H));
                for (int i = 0; i < n; i++)
                {
                    int r = i / cols, c = i % cols;
                    // centre the last, shorter row
                    int inRow = r == rows - 1 ? n - r * cols : cols;
                    double x0 = gap + c * (cell + gap) + (cols - inRow) * (cell + gap) / 2.0, y0 = gap + r * (cell + gap);
                    var im = imgs[i];
                    // cover-crop each image into its square cell
                    double s = Math.Max((double)cell / im.PixelWidth, (double)cell / im.PixelHeight);
                    double w = im.PixelWidth * s, h = im.PixelHeight * s;
                    dc.PushClip(new WM.RectangleGeometry(new System.Windows.Rect(x0, y0, cell, cell), 18, 18));
                    dc.DrawImage(im, new System.Windows.Rect(x0 + (cell - w) / 2, y0 + (cell - h) / 2, w, h));
                    dc.Pop();
                }
            }
            var rtb = new WMI.RenderTargetBitmap(W, H, 96, 96, WM.PixelFormats.Pbgra32);
            rtb.Render(dv);
            string dest = Ops.Unique(Ops.DirOf(files[0]), Ops.BaseName(files[0]) + "-" + Ops.Suffix("collage"), "jpg");
            File.WriteAllBytes(dest, Encode(rtb, "jpg", 90));
            var res = new OpResult();
            res.Outputs.Add(dest);
            return res;
        }

        /// Reads QR codes (and other common barcodes) in the image; the text goes to the clipboard.
        public static OpResult ReadCodes(List<string> files)
        {
            var found = new List<string>();
            foreach (var f in files)
            {
                var img = Load(f);
                using (var ms = new MemoryStream(Encode(img, "png", 100)))
                using (var bmp = new System.Drawing.Bitmap(ms))
                {
                    var reader = new ZXing.BarcodeReader { AutoRotate = true, Options = new ZXing.Common.DecodingOptions { TryHarder = true, TryInverted = true } };
                    var results = reader.DecodeMultiple(bmp);
                    if (results != null) found.AddRange(results.Select(r => r.Text));
                }
            }
            if (found.Count == 0) throw new Exception(S.T("no_code"));
            string text = string.Join("\r\n", found.Distinct());
            var res = new OpResult { ClipboardText = text, Message = S.T("code_copied") + "\n" + (text.Length > 120 ? text.Substring(0, 120) + "…" : text) };
            Uri uri;
            if (found.Count == 1 && Uri.TryCreate(found[0], UriKind.Absolute, out uri) && (uri.Scheme == "http" || uri.Scheme == "https")) res.Link = found[0];
            return res;
        }

        public static OpResult CopyToClipboard(List<string> files)
        {
            var img = Load(files[0]);
            System.Windows.Clipboard.SetImage(img);
            return new OpResult { Message = S.T("copied") };
        }

        // ---------- ICO ----------

        static byte[] Ico(WMI.BitmapSource src)
        {
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes) pngs.Add(Encode(Square(src, s), "png", 100));
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
                return ms.ToArray();
            }
        }

        /// Fits the image into an s×s transparent square.
        static WMI.BitmapSource Square(WMI.BitmapSource src, int s)
        {
            double f = (double)s / Math.Max(src.PixelWidth, src.PixelHeight);
            var scaled = Scale(src, f);
            int w = Math.Min(s, scaled.PixelWidth), h = Math.Min(s, scaled.PixelHeight);
            byte[] sp = Pixels(scaled);
            var o = new byte[s * s * 4];
            int ox = (s - w) / 2, oy = (s - h) / 2;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(sp, y * scaled.PixelWidth * 4, o, ((y + oy) * s + ox) * 4, w * 4);
            var b = WMI.BitmapSource.Create(s, s, 96, 96, WM.PixelFormats.Bgra32, null, o, s * 4);
            b.Freeze();
            return b;
        }
    }

    /// Minimal PDF writer: one JPEG image per page.
    static class Pdf
    {
        public class PageImage
        {
            public byte[] Jpeg;
            public int PixelWidth, PixelHeight;
            public double WidthPt, HeightPt;
        }

        /// One image per page; page size = image at 96 dpi, capped to A4 width (portrait) or A4 height (landscape).
        public static void WriteImages(List<WMI.BitmapSource> images, string dest)
        {
            var pages = new List<PageImage>();
            foreach (var img in images)
            {
                int pw = img.PixelWidth, ph = img.PixelHeight;
                double w = pw * 0.75, h = ph * 0.75;
                double cap = pw >= ph ? 842 : 595;
                if (w > cap) { h = h * cap / w; w = cap; }
                pages.Add(new PageImage { Jpeg = ImageOps.Encode(img, "jpg", 90), PixelWidth = pw, PixelHeight = ph, WidthPt = w, HeightPt = h });
            }
            WritePages(pages, dest);
        }

        public static void WritePages(List<PageImage> pages, string dest)
        {
            var inv = CultureInfo.InvariantCulture;
            var latin = Encoding.GetEncoding(28591);
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                var offsets = new List<long>();
                Action<string> W = s => { var b = latin.GetBytes(s); fs.Write(b, 0, b.Length); };
                Action<int> Obj = id => { while (offsets.Count < id) offsets.Add(0); offsets[id - 1] = fs.Position; W(id + " 0 obj\n"); };

                W("%PDF-1.4\n%âãÏÓ\n");
                int n = pages.Count;
                Obj(1); W("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
                var kids = new StringBuilder();
                for (int i = 0; i < n; i++) kids.Append(3 + i * 3).Append(" 0 R ");
                Obj(2); W("<< /Type /Pages /Kids [" + kids + "] /Count " + n + " >>\nendobj\n");

                for (int i = 0; i < n; i++)
                {
                    var pg = pages[i];
                    string W_ = pg.WidthPt.ToString("0.##", inv), H_ = pg.HeightPt.ToString("0.##", inv);
                    int pageId = 3 + i * 3, contentId = pageId + 1, imgId = pageId + 2;

                    Obj(pageId);
                    W("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + W_ + " " + H_ + "] /Resources << /XObject << /Im0 " + imgId +
                      " 0 R >> >> /Contents " + contentId + " 0 R >>\nendobj\n");
                    string content = "q " + W_ + " 0 0 " + H_ + " 0 0 cm /Im0 Do Q";
                    Obj(contentId); W("<< /Length " + content.Length + " >>\nstream\n" + content + "\nendstream\nendobj\n");
                    Obj(imgId);
                    W("<< /Type /XObject /Subtype /Image /Width " + pg.PixelWidth + " /Height " + pg.PixelHeight +
                      " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + pg.Jpeg.Length + " >>\nstream\n");
                    fs.Write(pg.Jpeg, 0, pg.Jpeg.Length);
                    W("\nendstream\nendobj\n");
                }

                long xref = fs.Position;
                W("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
                foreach (var o in offsets) W(o.ToString("D10", inv) + " 00000 n \n");
                W("trailer\n<< /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }
    }
}
