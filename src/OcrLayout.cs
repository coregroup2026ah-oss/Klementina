using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Klementina
{
    /// Rebuilds a page that is only a picture (a scan, or a design exported as an image) from what the OCR read:
    /// each word with its position and colour, the coloured areas behind text (banners, table headers, labels),
    /// and the photos on the page, cut out as pictures. The result goes through the same layout as a real PDF
    /// page, so lines, Arabic reading order, tables and paragraphs come out the same way.
    static class OcrLayout
    {
        public class Word
        {
            public string Text;
            public RectangleF Box;      // pixels of the page image
        }

        public static PdfText.Page Build(PdfText.Page scan, List<Word> words, string imageFile, string outDir)
        {
            using (var bmp = new Bitmap(imageFile))
            {
                int w = bmp.Width, h = bmp.Height;
                var px = new int[w * h];
                using (var b32 = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(b32)) g.DrawImage(bmp, 0, 0, w, h);
                    var bd = b32.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(bd.Scan0 + y * bd.Stride, px, y * w, w);
                    b32.UnlockBits(bd);
                }
                var rc = scan.ScanRect;
                double sx = rc.Width / w, sy = rc.Height / h;
                Func<RectangleF, RectangleF> toPage = r => new RectangleF((float)(rc.X + r.X * sx), (float)(rc.Bottom - (r.Y + r.Height) * sy), (float)(r.Width * sx), (float)(r.Height * sy));

                int bg = Background(px, w, h);
                var regions = Regions(px, w, h, bg, words);

                // photos: cut out of the page image; OCR noise inside them is dropped
                var pictures = new List<Tuple<RectangleF, string>>();
                int n = 0;
                foreach (var r in regions.Where(r => r.Picture))
                {
                    string file = Path.Combine(outDir, Path.GetFileNameWithoutExtension(imageFile) + "-photo" + (++n) + ".png");
                    var box = Rectangle.Round(r.Box);
                    box.Intersect(new Rectangle(0, 0, w, h));
                    if (box.Width < 8 || box.Height < 8) continue;
                    using (var crop = bmp.Clone(box, PixelFormat.Format32bppArgb)) crop.Save(file, ImageFormat.Png);
                    pictures.Add(Tuple.Create(toPage(box), file));
                }
                var photoBoxes = regions.Where(r => r.Picture).Select(r => r.Box).ToList();
                words = words.Where(wd => !photoBoxes.Any(p => p.Contains(wd.Box.X + wd.Box.Width / 2, wd.Box.Y + wd.Box.Height / 2))).ToList();

                // words: size from the box height, colour from the ink inside the box
                var glyphs = new List<PdfText.OcrGlyph>();
                foreach (var wd in words)
                {
                    var box = toPage(wd.Box);
                    glyphs.Add(new PdfText.OcrGlyph
                    {
                        // an Arabic line's box spans dots and tails (≈1.1 em); a Latin box is mostly cap height (≈0.8 em)
                        Text = wd.Text, X = box.X, Y = box.Y + box.Height * 0.22, W = box.Width,
                        Size = Math.Max(5, box.Height / (LineHasArabic(words, wd) ? 1.1 : 0.8)),
                        Color = InkColor(px, w, h, Rectangle.Round(wd.Box)), Bold = IsBold(px, w, h, Rectangle.Round(wd.Box)),
                    });
                }
                var shapes = regions.Where(r => !r.Picture).Select(r => Tuple.Create(toPage(r.Box), r.Color)).ToList();
                var page = PdfText.FromOcr(scan, glyphs, shapes, pictures);
                if (!IsWhite(bg)) page.Background = bg;
                page.Ocr = true;
                return page;
            }
        }

        /// Words of one OCR line share the same box height and top; does any of them have Arabic letters?
        static bool LineHasArabic(List<Word> words, Word w)
        {
            return words.Any(o => Math.Abs(o.Box.Y - w.Box.Y) < 0.5 && Math.Abs(o.Box.Height - w.Box.Height) < 0.5
                                  && o.Text.Any(c => c >= 0x0600 && c <= 0x06FF));
        }

        // ---------- colours ----------

        static int R(int c) { return (c >> 16) & 255; }
        static int G(int c) { return (c >> 8) & 255; }
        static int B(int c) { return c & 255; }
        static int Diff(int a, int b) { return Math.Max(Math.Abs(R(a) - R(b)), Math.Max(Math.Abs(G(a) - G(b)), Math.Abs(B(a) - B(b)))); }
        static bool IsWhite(int c) { return R(c) > 245 && G(c) > 245 && B(c) > 245; }

        /// The page colour: the most common colour (sampled, rounded).
        static int Background(int[] px, int w, int h)
        {
            var count = new Dictionary<int, int>();
            int step = Math.Max(1, (int)Math.Sqrt((double)w * h / 40000));
            for (int y = 0; y < h; y += step)
                for (int x = 0; x < w; x += step)
                {
                    int c = px[y * w + x] & 0xF8F8F8;
                    int v; count.TryGetValue(c, out v); count[c] = v + 1;
                }
            int best = count.OrderByDescending(kv => kv.Value).First().Key;
            return best | 0x040404;
        }

        /// Colour of the text in a word box: the pixels least like the box's own background.
        static int InkColor(int[] px, int w, int h, Rectangle box)
        {
            box.Intersect(new Rectangle(0, 0, w, h));
            if (box.Width < 2 || box.Height < 2) return 0;
            // background of the box: its border pixels
            var border = new List<int>();
            for (int x = box.Left; x < box.Right; x++) { border.Add(px[box.Top * w + x] & 0xFFFFFF); border.Add(px[(box.Bottom - 1) * w + x] & 0xFFFFFF); }
            for (int y = box.Top; y < box.Bottom; y++) { border.Add(px[y * w + box.Left] & 0xFFFFFF); border.Add(px[y * w + box.Right - 1] & 0xFFFFFF); }
            int bg = border.GroupBy(c => c & 0xF0F0F0).OrderByDescending(gr => gr.Count()).First().First();
            var ink = new List<int>();
            for (int y = box.Top; y < box.Bottom; y++)
                for (int x = box.Left; x < box.Right; x++)
                {
                    int c = px[y * w + x] & 0xFFFFFF;
                    if (Diff(c, bg) > 90) ink.Add(c);
                }
            if (ink.Count < 4) return 0;
            // the strongest third of the ink (anti-aliased edges are paler)
            var strong = ink.OrderByDescending(c => Diff(c, bg)).Take(Math.Max(4, ink.Count / 3)).ToList();
            int rr = (int)strong.Average(c => R(c)), gg = (int)strong.Average(c => G(c)), bb = (int)strong.Average(c => B(c));
            int color = (rr << 16) | (gg << 8) | bb;
            return Math.Max(rr, Math.Max(gg, bb)) < 70 ? 0 : color;   // near-black text is plain black
        }

        /// Heavy strokes: a large share of the word box is ink.
        static bool IsBold(int[] px, int w, int h, Rectangle box)
        {
            box.Intersect(new Rectangle(0, 0, w, h));
            if (box.Width < 4 || box.Height < 4) return false;
            int bgc = px[box.Top * w + box.Left] & 0xFFFFFF, ink = 0, total = 0;
            for (int y = box.Top; y < box.Bottom; y++)
                for (int x = box.Left; x < box.Right; x++) { total++; if (Diff(px[y * w + x] & 0xFFFFFF, bgc) > 90) ink++; }
            return total > 0 && ink * 100 / total > 34;
        }

        // ---------- areas: coloured shapes and photos ----------

        class Region { public RectangleF Box; public int Color; public bool Picture; }

        static List<Region> Regions(int[] px, int w, int h, int bg, List<Word> words)
        {
            int cs = Math.Max(6, w / 300);                 // grid cell size in pixels
            int gw = (w + cs - 1) / cs, gh = (h + cs - 1) / cs;
            var mean = new int[gw * gh];
            var busy = new bool[gw * gh];
            var spread = new double[gw * gh];
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    long sr = 0, sg = 0, sb = 0, sl = 0, sl2 = 0; int cnt = 0, far = 0;
                    for (int y = gy * cs; y < Math.Min(h, gy * cs + cs); y++)
                        for (int x = gx * cs; x < Math.Min(w, gx * cs + cs); x++)
                        {
                            int c = px[y * w + x];
                            int r = R(c), g = G(c), b = B(c);
                            sr += r; sg += g; sb += b; cnt++;
                            int l = (r * 3 + g * 6 + b) / 10; sl += l; sl2 += l * l;
                            if (Diff(c, bg) > 40) far++;
                        }
                    int m = (int)((sr / cnt) << 16 | (sg / cnt) << 8 | (sb / cnt));
                    mean[gy * gw + gx] = m;
                    spread[gy * gw + gx] = Math.Sqrt(Math.Max(0, (double)sl2 / cnt - Math.Pow((double)sl / cnt, 2)));
                    busy[gy * gw + gx] = Diff(m, bg) > 22 || far * 4 > cnt;
                }
            // cells under recognised words
            var text = new bool[gw * gh];
            foreach (var wd in words)
            {
                int x0 = Math.Max(0, (int)(wd.Box.Left - 2) / cs), x1 = Math.Min(gw - 1, (int)(wd.Box.Right + 2) / cs);
                int y0 = Math.Max(0, (int)(wd.Box.Top - 2) / cs), y1 = Math.Min(gh - 1, (int)(wd.Box.Bottom + 2) / cs);
                for (int gy = y0; gy <= y1; gy++) for (int gx = x0; gx <= x1; gx++) text[gy * gw + gx] = true;
            }

            var result = new List<Region>();
            var seen = new bool[gw * gh];
            var queue = new Queue<int>();
            for (int start = 0; start < gw * gh; start++)
            {
                if (!busy[start] || seen[start]) continue;
                var cells = new List<int>();
                seen[start] = true; queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue(); cells.Add(c);
                    int cx = c % gw, cy = c / gw;
                    foreach (int nb in new[] { cx > 0 ? c - 1 : -1, cx < gw - 1 ? c + 1 : -1, cy > 0 ? c - gw : -1, cy < gh - 1 ? c + gw : -1 })
                        if (nb >= 0 && busy[nb] && !seen[nb]) { seen[nb] = true; queue.Enqueue(nb); }
                }
                int minX = cells.Min(c => c % gw), maxX = cells.Max(c => c % gw), minY = cells.Min(c => c / gw), maxY = cells.Max(c => c / gw);
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (bw * cs < 20 || bh * cs < 16) continue;
                var box = new RectangleF(minX * cs, minY * cs, Math.Min(w, (maxX + 1) * cs) - minX * cs, Math.Min(h, (maxY + 1) * cs) - minY * cs);
                int textCells = cells.Count(c => text[c]);
                double fill = (double)cells.Count / (bw * bh);
                var plain = cells.Where(c => !text[c]).ToList();
                bool hasWords = words.Any(wd => box.Contains(wd.Box.X + wd.Box.Width / 2, wd.Box.Y + wd.Box.Height / 2));
                if (textCells * 2 >= cells.Count && !(hasWords && fill > 0.85 && plain.Count > cells.Count / 5)) continue;   // the text itself
                if (plain.Count == 0) continue;
                int avg = Average(plain.Select(c => mean[c]));
                double variety = plain.Average(c => spread[c]) + Spread(plain.Select(c => mean[c]));
                double textShare = (double)textCells / cells.Count;
                if (hasWords && variety > 22 && textShare < 0.35 && fill > 0.6 && bw * cs >= 60 && bh * cs >= 60)
                    result.Add(new Region { Box = box, Picture = true });                    // a photo / promo image with some text printed on it
                else if (hasWords && fill > 0.6)
                    result.Add(new Region { Box = box, Color = avg });                       // a banner / label / header behind text
                else if (!hasWords && variety > 14 && fill > 0.3 && bw * cs >= 40 && bh * cs >= 40)
                    result.Add(new Region { Box = box, Picture = true });                    // a photo or drawing
                else if (!hasWords && variety <= 14 && fill > 0.6)
                    result.Add(new Region { Box = box, Color = avg });                       // a plain coloured bar
            }
            return result;
        }

        static int Average(IEnumerable<int> colors)
        {
            var list = colors.ToList();
            int r = (int)list.Average(c => R(c)), g = (int)list.Average(c => G(c)), b = (int)list.Average(c => B(c));
            return (r << 16) | (g << 8) | b;
        }

        static double Spread(IEnumerable<int> colors)
        {
            var l = colors.Select(c => (R(c) * 3 + G(c) * 6 + B(c)) / 10.0).ToList();
            if (l.Count < 2) return 0;
            double m = l.Average();
            return Math.Sqrt(l.Average(v => (v - m) * (v - m)));
        }
    }
}
