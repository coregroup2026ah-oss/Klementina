using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using WM = System.Windows.Media;
using WMI = System.Windows.Media.Imaging;

namespace Klementina
{
    /// PDF page work (merge, split, rotate, extract, compress) through qpdf, plus a raster fallback for strong compression.
    static class PdfTools
    {
        static void Qpdf(string args, string expectedOutput)
        {
            if (Engines.Qpdf == null) throw new Exception("qpdf is missing");
            string log;
            int code = Ops.Run(Engines.Qpdf, args, out log);
            // qpdf exit code 3 = success with warnings
            if ((code != 0 && code != 3) || (expectedOutput != null && !File.Exists(expectedOutput) && !Directory.Exists(expectedOutput)))
            {
                Ops.TryDelete(expectedOutput);
                var lines = log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                throw new Exception(lines.Length > 0 ? lines[lines.Length - 1] : "qpdf exit code " + code);
            }
        }

        public static int PageCount(string pdf)
        {
            string log;
            Ops.Run(Engines.Qpdf, "--show-npages " + Ops.Q(pdf), out log);
            int n;
            return int.TryParse(log.Trim(), out n) ? n : 0;
        }

        // ---------------- page operations ----------------

        public static OpResult Merge(List<string> files)
        {
            var pdfs = files.Where(f => Catalog.Ext(f) == "pdf").ToList();
            string dest = Ops.Unique(Ops.DirOf(pdfs[0]), Ops.BaseName(pdfs[0]) + " +" + (pdfs.Count - 1), "pdf");
            Qpdf("--empty --pages " + string.Join(" ", pdfs.Select(Ops.Q)) + " -- " + Ops.Q(dest), dest);
            var r = new OpResult();
            r.Outputs.Add(dest);
            return r;
        }

        public static string Split(string src)
        {
            string folder = Ops.Unique(Ops.DirOf(src), Ops.BaseName(src) + " (" + Ops.Suffix("pages") + ")", null);
            Directory.CreateDirectory(folder);
            try { Qpdf("--split-pages " + Ops.Q(src) + " " + Ops.Q(Path.Combine(folder, (S.Ar ? "صفحة" : "page") + "-%d.pdf")), folder); }
            catch { Ops.TryDelete(folder); throw; }
            return folder;
        }

        /// Splits into one file per range (qpdf range syntax each, e.g. "1-180", "181-z"), inside a new folder.
        public static string SplitRanges(string src, List<string> ranges)
        {
            string folder = Ops.Unique(Ops.DirOf(src), Ops.BaseName(src) + " (" + Ops.Suffix("parts") + ")", null);
            Directory.CreateDirectory(folder);
            try
            {
                int digits = ranges.Count.ToString().Length;
                for (int i = 0; i < ranges.Count; i++)
                {
                    string name = Ops.BaseName(src) + "-" + (S.Ar ? "جزء" : "part") + (i + 1).ToString().PadLeft(digits, '0') + " (" + ranges[i] + ").pdf";
                    string outFile = Path.Combine(folder, name);
                    Qpdf(Ops.Q(src) + " --pages . " + ranges[i] + " -- " + Ops.Q(outFile), outFile);
                }
            }
            catch { Ops.TryDelete(folder); throw; }
            return folder;
        }

        /// Every n pages become one file.
        public static string SplitEvery(string src, int n)
        {
            string folder = Ops.Unique(Ops.DirOf(src), Ops.BaseName(src) + " (" + Ops.Suffix("parts") + ")", null);
            Directory.CreateDirectory(folder);
            try { Qpdf("--split-pages=" + n + " " + Ops.Q(src) + " " + Ops.Q(Path.Combine(folder, Ops.BaseName(src) + "-%d.pdf")), folder); }
            catch { Ops.TryDelete(folder); throw; }
            return folder;
        }

        /// Ranges for k (nearly) equal parts of an n-page document.
        public static List<string> EqualParts(int pages, int parts)
        {
            parts = Math.Max(1, Math.Min(parts, pages));
            var res = new List<string>();
            int start = 1;
            for (int i = 0; i < parts; i++)
            {
                int size = pages / parts + (i < pages % parts ? 1 : 0);
                res.Add(size == 1 ? start.ToString() : start + "-" + (start + size - 1));
                start += size;
            }
            return res;
        }

        public static string Rotate(string src)
        {
            string dest = Ops.Out(src, "pdf", "rotated");
            Qpdf(Ops.Q(src) + " " + Ops.Q(dest) + " --rotate=+90", dest);
            return dest;
        }

        /// pages: qpdf range syntax, e.g. "1-3,5,8-z"
        public static string Extract(string src, string pages)
        {
            string dest = Ops.Out(src, "pdf", "pages");
            Qpdf(Ops.Q(src) + " --pages . " + pages + " -- " + Ops.Q(dest), dest);
            return dest;
        }

        public static string StripInfo(string src)
        {
            string dest = Ops.Out(src, "pdf", "clean");
            Qpdf(Ops.Q(src) + " " + Ops.Q(dest) + " --remove-info --remove-metadata", dest);
            return dest;
        }

        /// Turns "1-3, 5 ، ٧-٩" (Arabic digits/commas too) into qpdf's "1-3,5,7-9"; null if invalid.
        public static string NormalizeRanges(string text, int pageCount)
        {
            var sb = new StringBuilder();
            foreach (char ch in text ?? "")
            {
                if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else if (ch == '،' || ch == ';') sb.Append(',');
                else if (ch == '–' || ch == '—') sb.Append('-');
                else if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            }
            string s = sb.ToString().Trim(',');
            if (!Regex.IsMatch(s, @"^\d+(-\d+)?(,\d+(-\d+)?)*$")) return null;
            foreach (Match m in Regex.Matches(s, @"\d+"))
            {
                int p = int.Parse(m.Value);
                if (p < 1 || (pageCount > 0 && p > pageCount)) return null;
            }
            return s;
        }

        // ---------------- compression ----------------

        /// Lossless-ish: recompresses streams and turns suitable images into JPEG. Keeps text selectable.
        static string Optimize(string src, string dest, int jpegQuality)
        {
            Qpdf(Ops.Q(src) + " " + Ops.Q(dest) + " --object-streams=generate --recompress-flate --compression-level=9 --optimize-images --jpeg-quality=" + jpegQuality, dest);
            return dest;
        }

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

        /// Strong: every page becomes a JPEG at the given resolution. Much smaller for scans; text is no longer selectable.
        static string Rasterize(string src, string dest, int dpi, int quality)
        {
            StorageFile file = Wait(StorageFile.GetFileFromPathAsync(src));
            PdfDocument doc = Wait(PdfDocument.LoadFromFileAsync(file));
            var pages = new List<Pdf.PageImage>();
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
                    WMI.BitmapSource img;
                    using (var mem = new MemoryStream(png))
                    {
                        var dec = WMI.BitmapDecoder.Create(mem, WMI.BitmapCreateOptions.None, WMI.BitmapCacheOption.OnLoad);
                        img = dec.Frames[0];
                    }
                    pages.Add(new Pdf.PageImage
                    {
                        Jpeg = ImageOps.Encode(img, "jpg", quality), PixelWidth = img.PixelWidth, PixelHeight = img.PixelHeight,
                        WidthPt = page.Size.Width * 0.75, HeightPt = page.Size.Height * 0.75,
                    });
                }
            }
            Pdf.WritePages(pages, dest);
            return dest;
        }

        /// The default "Compress": keep text, squeeze streams and images; keep only if smaller.
        public static string CompressBalanced(string src)
        {
            return CompressLevel(src, 1);
        }

        // level: 0 light, 1 balanced, 2 strong, 3 maximum
        static readonly int[] LevelMaxSide = { 3000, 2000, 1400, 1000 };
        static readonly int[] LevelQuality = { 80, 70, 58, 45 };

        public static string CompressLevel(string src, int level)
        {
            string dest = Ops.Out(src, "pdf", "compressed");
            CompressImages(src, dest, LevelMaxSide[level], LevelQuality[level]);
            return dest;
        }

        /// Text-preserving compression: every photo inside the PDF is decoded, shrunk to maxSide pixels on its long
        /// side and re-saved as JPEG at the given quality; text, fonts and vector art are untouched.
        /// Uses qpdf's JSON round trip: export streams to files, swap image data, import the changes.
        public static string CompressImages(string src, string dest, int maxSide, int quality)
        {
            string tmp = Ops.TempDir();
            try
            {
                string json = Path.Combine(tmp, "pdf.json");
                Qpdf(Ops.Q(src) + " --json-output --json-stream-data=file --json-stream-prefix=" + Ops.Q(Path.Combine(tmp, "s")) + " " + Ops.Q(json), json);
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
                var root = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(json, Encoding.UTF8));
                var parts = (object[])root["qpdf"];
                var header = parts[0];
                var objects = (Dictionary<string, object>)parts[1];
                var updates = new Dictionary<string, object>();
                int n = 0;
                foreach (var kv in objects)
                {
                    var obj = kv.Value as Dictionary<string, object>;
                    object streamObj;
                    if (obj == null || !obj.TryGetValue("stream", out streamObj)) continue;
                    var stream = (Dictionary<string, object>)streamObj;
                    var dict = stream["dict"] as Dictionary<string, object>;
                    object dataFile;
                    if (dict == null || !stream.TryGetValue("datafile", out dataFile) || Get(dict, "/Subtype") as string != "/Image") continue;
                    try
                    {
                        var replaced = ShrinkImage(dict, (string)dataFile, objects, maxSide, quality, Path.Combine(tmp, "n" + (n++) + ".jpg"));
                        if (replaced != null) updates[kv.Key] = new Dictionary<string, object> { { "stream", replaced } };
                    }
                    catch { /* leave any image we can't handle exactly as it was */ }
                }
                string extra = " --object-streams=generate --recompress-flate --compression-level=9";
                if (updates.Count == 0) Qpdf(Ops.Q(src) + " " + Ops.Q(dest) + extra, dest);
                else
                {
                    string mod = Path.Combine(tmp, "update.json");
                    File.WriteAllText(mod, ser.Serialize(new Dictionary<string, object> { { "qpdf", new object[] { header, updates } } }), new UTF8Encoding(false));
                    Qpdf(Ops.Q(src) + " " + Ops.Q(dest) + " --update-from-json=" + Ops.Q(mod) + extra, dest);
                }
                return dest;
            }
            finally { Ops.TryDelete(tmp); }
        }

        static object Get(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) ? v : null; }

        static string AsName(object v)
        {
            var list = v as object[];
            if (list != null) return list.Length == 1 ? list[0] as string : null; // a one-filter array
            return v as string;
        }

        /// Number of colour components, or 0 when the colour space isn't one we can safely re-encode as JPEG.
        static int Components(object cs, Dictionary<string, object> objects, out bool keepOriginalSpace)
        {
            keepOriginalSpace = false;
            var name = cs as string;
            if (name == "/DeviceRGB") return 3;
            if (name == "/DeviceGray") return 1;
            if (name != null && name.EndsWith(" R"))
            {
                // indirect: usually [/ICCBased <stream>]
                var target = Get(objects, "obj:" + name) as Dictionary<string, object>;
                return target == null ? 0 : Components(Get(target, "value"), objects, out keepOriginalSpace);
            }
            var arr = cs as object[];
            if (arr != null && arr.Length == 2 && arr[0] as string == "/ICCBased" && arr[1] is string)
            {
                var iccObj = Get(objects, "obj:" + (string)arr[1]) as Dictionary<string, object>;
                var iccStream = iccObj == null ? null : Get(iccObj, "stream") as Dictionary<string, object>;
                var iccDict = iccStream == null ? null : Get(iccStream, "dict") as Dictionary<string, object>;
                int nComp = iccDict == null ? 0 : Convert.ToInt32(Get(iccDict, "/N") ?? 0);
                if (nComp == 1 || nComp == 3) { keepOriginalSpace = true; return nComp; }
            }
            return 0;
        }

        /// Returns the replacement {dict, datafile} for one image, or null to leave it alone.
        static Dictionary<string, object> ShrinkImage(Dictionary<string, object> dict, string dataFile, Dictionary<string, object> objects, int maxSide, int quality, string outFile)
        {
            if (Get(dict, "/ImageMask") is bool && (bool)Get(dict, "/ImageMask")) return null;
            if (Get(dict, "/Decode") != null || Get(dict, "/Mask") is object[]) return null; // exact colours matter there
            int w = Convert.ToInt32(Get(dict, "/Width") ?? 0), h = Convert.ToInt32(Get(dict, "/Height") ?? 0);
            int bpc = Convert.ToInt32(Get(dict, "/BitsPerComponent") ?? 8);
            if (w < 64 || h < 64) return null; // icons and tiny bits aren't worth it
            bool keepSpace;
            int comps = Components(Get(dict, "/ColorSpace"), objects, out keepSpace);
            if (comps == 0) return null;
            string filter = AsName(Get(dict, "/Filter"));
            byte[] data = File.ReadAllBytes(dataFile);
            long oldSize = data.Length;

            WMI.BitmapSource img;
            if (filter == "/DCTDecode")
            {
                using (var ms = new MemoryStream(data))
                    img = WMI.BitmapDecoder.Create(ms, WMI.BitmapCreateOptions.IgnoreColorProfile, WMI.BitmapCacheOption.OnLoad).Frames[0];
                if (img.Format == WM.PixelFormats.Cmyk32) return null;
            }
            else if (filter == "/FlateDecode" && bpc == 8)
            {
                byte[] raw = Inflate(data);
                var parms = Get(dict, "/DecodeParms") as Dictionary<string, object>;
                int predictor = parms == null ? 1 : Convert.ToInt32(Get(parms, "/Predictor") ?? 1);
                if (predictor >= 10) raw = UnPng(raw, w, h, comps);
                else if (predictor != 1) return null;
                int stride = w * comps;
                if (raw.Length < stride * h) return null;
                img = WMI.BitmapSource.Create(w, h, 96, 96, comps == 3 ? WM.PixelFormats.Rgb24 : WM.PixelFormats.Gray8, null, raw, stride);
            }
            else return null;

            int longest = Math.Max(img.PixelWidth, img.PixelHeight);
            if (longest > maxSide)
                img = new WMI.TransformedBitmap(img, new WM.ScaleTransform((double)maxSide / longest, (double)maxSide / longest));
            if (comps == 1 && img.Format != WM.PixelFormats.Gray8) img = new WMI.FormatConvertedBitmap(img, WM.PixelFormats.Gray8, null, 0);
            if (comps == 3 && img.Format != WM.PixelFormats.Bgr24) img = new WMI.FormatConvertedBitmap(img, WM.PixelFormats.Bgr24, null, 0);

            var enc = new WMI.JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(WMI.BitmapFrame.Create(img));
            using (var fs = File.Create(outFile)) enc.Save(fs);
            long newSize = new FileInfo(outFile).Length;
            if (newSize > oldSize * 0.9) return null; // not worth it

            var nd = new Dictionary<string, object>(dict);
            nd.Remove("/DecodeParms");
            nd.Remove("/Length");
            nd["/Filter"] = "/DCTDecode";
            nd["/Width"] = img.PixelWidth;
            nd["/Height"] = img.PixelHeight;
            nd["/BitsPerComponent"] = 8;
            if (!keepSpace) nd["/ColorSpace"] = comps == 3 ? "/DeviceRGB" : "/DeviceGray";
            return new Dictionary<string, object> { { "dict", nd }, { "datafile", outFile } };
        }

        static byte[] Inflate(byte[] zlib)
        {
            using (var input = new MemoryStream(zlib, 2, zlib.Length - 2)) // skip the 2-byte zlib header
            using (var z = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                z.CopyTo(output);
                return output.ToArray();
            }
        }

        /// Undoes PNG row filters (PDF predictors 10–15).
        static byte[] UnPng(byte[] data, int w, int h, int bpp)
        {
            int stride = w * bpp;
            var outp = new byte[stride * h];
            var prev = new byte[stride];
            int pos = 0;
            for (int y = 0; y < h && pos < data.Length; y++)
            {
                int ft = data[pos++];
                var row = new byte[stride];
                Array.Copy(data, pos, row, 0, Math.Min(stride, data.Length - pos));
                pos += stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? row[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                    switch (ft)
                    {
                        case 1: row[x] = (byte)(row[x] + a); break;
                        case 2: row[x] = (byte)(row[x] + b); break;
                        case 3: row[x] = (byte)(row[x] + ((a + b) >> 1)); break;
                        case 4:
                            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                            row[x] = (byte)(row[x] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                            break;
                    }
                }
                Array.Copy(row, 0, outp, y * stride, stride);
                prev = row;
            }
            return outp;
        }

        /// Hit a byte budget: try the text-preserving pass first, then raster passes of falling quality.
        public static string CompressToSize(string src, long targetBytes)
        {
            string dest = Ops.Out(src, "pdf", "compressed");
            string tmpDir = Ops.TempDir();
            try
            {
                string best = null; long bestSize = long.MaxValue;
                Action<string> consider = p =>
                {
                    long s = Ops.SizeOf(p);
                    if (s > 0 && s < bestSize) { best = p; bestSize = s; }
                };
                // text-preserving passes first, from gentle to strong
                for (int lv = 0; lv < LevelMaxSide.Length; lv++)
                {
                    string p = Path.Combine(tmpDir, "lv" + lv + ".pdf");
                    try { consider(CompressImages(src, p, LevelMaxSide[lv], LevelQuality[lv])); } catch { }
                    if (bestSize <= targetBytes) break;
                }
                if (bestSize > targetBytes)
                {
                    foreach (var lv in new[] { new[] { 200, 80 }, new[] { 150, 70 }, new[] { 120, 60 }, new[] { 96, 50 }, new[] { 72, 40 } })
                    {
                        string p = Path.Combine(tmpDir, "r" + lv[0] + ".pdf");
                        consider(Rasterize(src, p, lv[0], lv[1]));
                        if (bestSize <= targetBytes) break;
                    }
                }
                if (best == null) throw new Exception("PDF compression failed");
                File.Copy(best, dest);
                return dest;
            }
            finally { Ops.TryDelete(tmpDir); }
        }
    }
}
