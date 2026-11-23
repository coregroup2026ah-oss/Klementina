using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace Klementina
{
    // ======================================================================= Crop

    class CropEditor : EditorWindow
    {
        RectangleF crop;                 // in Source pixels
        Func<int> aspect;
        static readonly float[] Ratios = { 0, -1, 1f, 4f / 3, 3f / 4, 16f / 9, 9f / 16, 3f / 2 }; // 0 free, -1 original
        enum Drag { None, New, Move, TL, TR, BL, BR }
        Drag drag;
        PointF dragStart; RectangleF cropStart;
        Label sizeLabel;

        public CropEditor(string path, Bitmap frame = null, bool transforms = true) : base(S.T("crop"), path, 290, frame)
        {
            AddSection(S.T("aspect"));
            aspect = AddSegments(null, new[] { S.T("free"), S.T("original"), "1:1", "4:3", "3:4", "16:9", "9:16", "3:2" }, 0, ApplyAspect);
            if (transforms)
            {
                AddSection(S.T("rotate_flip"));
                AddSideButton("⟲  " + S.T("rotate_left"), () => Transform(RotateFlipType.Rotate270FlipNone));
                AddSideButton("⟳  " + S.T("rotate_right"), () => Transform(RotateFlipType.Rotate90FlipNone));
                AddSideButton("⇋  " + S.T("flip_h"), () => Transform(RotateFlipType.RotateNoneFlipX));
                AddSideButton("⇵  " + S.T("flip_v"), () => Transform(RotateFlipType.RotateNoneFlipY));
            }
            sizeLabel = AddNote("");
            AddNote(S.T("crop_hint"));

            crop = new RectangleF(0, 0, Source.Width, Source.Height);
            canvas.Image = Source;
            canvas.PaintOverlay += Overlay;
            canvas.MouseDown += OnDown;
            canvas.MouseMove += OnMove;
            canvas.MouseUp += (s, e) => drag = Drag.None;
            UpdateLabel();
        }

        void Transform(RotateFlipType t)
        {
            Source.RotateFlip(t);
            crop = new RectangleF(0, 0, Source.Width, Source.Height);
            ApplyAspect();
            canvas.Invalidate();
        }

        float CurrentRatio()
        {
            float r = Ratios[aspect()];
            return r == -1 ? (float)Source.Width / Source.Height : r;
        }

        void ApplyAspect()
        {
            float r = CurrentRatio();
            if (r > 0)
            {
                // largest centred rectangle of that ratio inside the current crop
                float w = crop.Width, h = w / r;
                if (h > crop.Height) { h = crop.Height; w = h * r; }
                crop = new RectangleF(crop.X + (crop.Width - w) / 2, crop.Y + (crop.Height - h) / 2, w, h);
            }
            UpdateLabel();
            canvas.Invalidate();
        }

        void UpdateLabel() { sizeLabel.Text = (int)crop.Width + " × " + (int)crop.Height + " px"; }

        Drag HitHandle(Point p)
        {
            var a = canvas.ToScreen(new PointF(crop.Left, crop.Top));
            var b = canvas.ToScreen(new PointF(crop.Right, crop.Bottom));
            Func<float, float, bool> near = (x, y) => Math.Abs(p.X - x) < 14 && Math.Abs(p.Y - y) < 14;
            if (near(a.X, a.Y)) return Drag.TL;
            if (near(b.X, a.Y)) return Drag.TR;
            if (near(a.X, b.Y)) return Drag.BL;
            if (near(b.X, b.Y)) return Drag.BR;
            if (p.X > a.X && p.X < b.X && p.Y > a.Y && p.Y < b.Y) return Drag.Move;
            return Drag.New;
        }

        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            drag = HitHandle(e.Location);
            dragStart = canvas.ToImage(e.Location);
            cropStart = crop;
        }

        void OnMove(object s, MouseEventArgs e)
        {
            var h = HitHandle(e.Location);
            canvas.Cursor = h == Drag.Move ? Cursors.SizeAll : h == Drag.TL || h == Drag.BR ? Cursors.SizeNWSE : h == Drag.TR || h == Drag.BL ? Cursors.SizeNESW : Cursors.Cross;
            if (drag == Drag.None) return;
            var p = canvas.ToImage(e.Location);
            float W = Source.Width, H = Source.Height;
            p = new PointF(Math.Max(0, Math.Min(W, p.X)), Math.Max(0, Math.Min(H, p.Y)));
            float ratio = CurrentRatio();
            if (drag == Drag.Move)
            {
                float dx = p.X - dragStart.X, dy = p.Y - dragStart.Y;
                float x = Math.Max(0, Math.Min(W - cropStart.Width, cropStart.X + dx));
                float y = Math.Max(0, Math.Min(H - cropStart.Height, cropStart.Y + dy));
                crop = new RectangleF(x, y, cropStart.Width, cropStart.Height);
            }
            else
            {
                // the fixed corner is opposite the one being dragged
                PointF anchor = drag == Drag.New ? dragStart :
                    drag == Drag.TL ? new PointF(cropStart.Right, cropStart.Bottom) :
                    drag == Drag.TR ? new PointF(cropStart.Left, cropStart.Bottom) :
                    drag == Drag.BL ? new PointF(cropStart.Right, cropStart.Top) : new PointF(cropStart.Left, cropStart.Top);
                float w = Math.Abs(p.X - anchor.X), hgt = Math.Abs(p.Y - anchor.Y);
                if (ratio > 0) { if (w / Math.Max(1, hgt) > ratio) w = hgt * ratio; else hgt = w / ratio; }
                float x = p.X < anchor.X ? anchor.X - w : anchor.X, y = p.Y < anchor.Y ? anchor.Y - hgt : anchor.Y;
                if (w >= 8 && hgt >= 8) crop = RectangleF.Intersect(new RectangleF(x, y, w, hgt), new RectangleF(0, 0, W, H));
            }
            UpdateLabel();
            canvas.Invalidate();
        }

        void Overlay(Graphics g, RectangleF img)
        {
            var a = canvas.ToScreen(new PointF(crop.Left, crop.Top));
            var b = canvas.ToScreen(new PointF(crop.Right, crop.Bottom));
            var r = RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
            using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (var region = new Region(img))
            {
                region.Exclude(r);
                g.FillRegion(dim, region);
            }
            using (var thin = new Pen(Color.FromArgb(110, 255, 255, 255), 1))
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(thin, r.X + r.Width * i / 3, r.Y, r.X + r.Width * i / 3, r.Bottom);
                    g.DrawLine(thin, r.X, r.Y + r.Height * i / 3, r.Right, r.Y + r.Height * i / 3);
                }
            using (var pen = new Pen(Color.White, 2)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using (var hb = new SolidBrush(Color.FromArgb(255, 140, 50)))
                foreach (var c in new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom) })
                    g.FillEllipse(hb, c.X - 7, c.Y - 7, 14, 14);
        }

        protected Rectangle cropAtSave;
        protected override void BeforeSave() { cropAtSave = Rectangle.Round(crop); }

        protected override Bitmap RenderFull(Bitmap source)
        {
            var r = Rectangle.Intersect(cropAtSave, new Rectangle(0, 0, source.Width, source.Height));
            if (r.Width < 1 || r.Height < 1) return new Bitmap(source);
            return source.Clone(r, PixelFormat.Format32bppArgb);
        }

        protected override string OutputSuffix { get { return "cropped"; } }

        protected override void ResetAll() { crop = new RectangleF(0, 0, Source.Width, Source.Height); ApplyAspect(); }
    }

    // ======================================================================= Annotate

    class Mark
    {
        public string Kind;            // pen, highlight, line, arrow, rect, ellipse, text, redact
        public Color Color;
        public float Width;
        public List<PointF> Points = new List<PointF>();
        public string Text;
        public int RedactMode;         // 0 black, 1 pixelate, 2 blur

        public RectangleF Bounds
        {
            get
            {
                float x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
                return RectangleF.FromLTRB(x0, y0, x1, y1);
            }
        }

        public void Draw(Graphics g, Bitmap source)
        {
            if (Points.Count == 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Kind == "redact") { DrawRedact(g, source); return; }
            using (var pen = new Pen(Kind == "highlight" ? Color.FromArgb(110, Color) : Color, Kind == "highlight" ? Width * 3 : Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                var a = Points[0];
                var b = Points[Points.Count - 1];
                switch (Kind)
                {
                    case "pen":
                    case "highlight":
                        if (Points.Count > 1) g.DrawLines(pen, Points.ToArray()); else g.FillEllipse(new SolidBrush(pen.Color), a.X - Width / 2, a.Y - Width / 2, Width, Width);
                        break;
                    case "line": g.DrawLine(pen, a, b); break;
                    case "arrow":
                        pen.CustomEndCap = new AdjustableArrowCap(3.2f, 3.6f, true);
                        g.DrawLine(pen, a, b);
                        break;
                    case "rect": g.DrawRectangle(pen, Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height); break;
                    case "ellipse": g.DrawEllipse(pen, Bounds); break;
                    case "text":
                        using (var f = new Font("Segoe UI Semibold", Width * 5, GraphicsUnit.Pixel))
                        using (var br = new SolidBrush(Color))
                        using (var halo = new Pen(Color.GetBrightness() > 0.6 ? Color.FromArgb(160, 0, 0, 0) : Color.FromArgb(200, 255, 255, 255), Width * 0.9f) { LineJoin = LineJoin.Round })
                        using (var path = new GraphicsPath())
                        {
                            // text starts at the click point and runs right; GDI+ still shapes and orders Arabic correctly
                            path.AddString(Text, f.FontFamily, (int)FontStyle.Bold, f.Size, a, StringFormat.GenericTypographic);
                            g.DrawPath(halo, path);
                            g.FillPath(br, path);
                        }
                        break;
                }
            }
        }

        static bool ContainsArabic(string s) { return s != null && s.Any(c => c >= '؀' && c <= 'ۿ'); }

        void DrawRedact(Graphics g, Bitmap source)
        {
            var r = Rectangle.Round(Bounds);
            r.Intersect(new Rectangle(0, 0, source.Width, source.Height));
            if (r.Width < 2 || r.Height < 2) return;
            if (RedactMode == 0) { g.FillRectangle(Brushes.Black, r); return; }
            // pixelate: shrink hard, scale back up with nearest-neighbour; blur: shrink and smooth back up (twice)
            int block = RedactMode == 1 ? Math.Max(6, Math.Max(r.Width, r.Height) / 18) : Math.Max(4, Math.Max(r.Width, r.Height) / 40);
            using (var piece = source.Clone(r, PixelFormat.Format32bppArgb))
            using (var tiny = new Bitmap(Math.Max(1, r.Width / block), Math.Max(1, r.Height / block)))
            {
                using (var tg = Graphics.FromImage(tiny)) { tg.InterpolationMode = InterpolationMode.HighQualityBilinear; tg.DrawImage(piece, 0, 0, tiny.Width, tiny.Height); }
                var state = g.Save();
                g.SetClip(r);
                g.InterpolationMode = RedactMode == 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(tiny, r);
                g.Restore(state);
            }
        }
    }

    class MarkupEditor : EditorWindow
    {
        readonly bool redactOnly;
        readonly List<Mark> marks = new List<Mark>();
        Mark current;
        Func<int> tool, colorPick, redactMode;
        TrackBar width;
        static readonly Color[] Palette = { Color.FromArgb(255, 70, 50), Color.FromArgb(255, 140, 30), Color.FromArgb(255, 210, 40), Color.FromArgb(40, 190, 90), Color.FromArgb(30, 140, 255), Color.FromArgb(150, 80, 230), Color.White, Color.Black };
        static readonly string[] ToolKinds = { "pen", "highlight", "arrow", "line", "rect", "ellipse", "text", "redact" };

        public MarkupEditor(string path, bool redact) : base(S.T(redact ? "redact" : "annotate"), path, 300)
        {
            redactOnly = redact;
            if (!redact)
            {
                AddSection(S.T("tool"));
                tool = AddSegments(null, new[] { S.T("t_pen"), S.T("t_highlight"), S.T("t_arrow"), S.T("t_line"), S.T("t_rect"), S.T("t_ellipse"), S.T("t_text"), S.T("redact") }, 2, null);
                AddSection(S.T("color"));
                colorPick = AddSwatches();
                width = AddSlider(S.T("thickness"), 1, 40, 8, v => v.ToString(), () => { });
            }
            AddSection(S.T("redact_style"));
            redactMode = AddSegments(null, new[] { S.T("r_black"), S.T("r_pixel"), S.T("r_blur") }, redact ? 1 : 0, () =>
            {
                foreach (var m in marks) if (m.Kind == "redact") m.RedactMode = redactMode();
                canvas.Invalidate();
            });
            AddNote(S.T(redact ? "redact_hint" : "annotate_hint"));
            AddSideButton("↶  " + S.T("undo"), () => { if (marks.Count > 0) { marks.RemoveAt(marks.Count - 1); canvas.Invalidate(); } });

            canvas.Image = Source;
            canvas.PaintOverlay += (g, r) =>
            {
                var st = g.Save();
                g.TranslateTransform(r.X, r.Y);
                g.ScaleTransform(canvas.ViewScale, canvas.ViewScale);
                g.SetClip(new RectangleF(0, 0, Source.Width, Source.Height));
                foreach (var m in marks) m.Draw(g, Source);
                if (current != null)
                {
                    if (current.Kind == "redact")
                        using (var p = new Pen(Color.FromArgb(255, 140, 50), 2 / canvas.ViewScale) { DashStyle = DashStyle.Dash }) { var b = current.Bounds; g.DrawRectangle(p, b.X, b.Y, b.Width, b.Height); }
                    else current.Draw(g, Source);
                }
                g.Restore(st);
            };
            canvas.Cursor = Cursors.Cross;
            canvas.MouseDown += OnDown;
            canvas.MouseMove += (s, e) =>
            {
                if (current == null) return;
                var p = canvas.ToImage(e.Location);
                if (current.Kind == "pen" || current.Kind == "highlight") current.Points.Add(p);
                else { if (current.Points.Count > 1) current.Points[1] = p; else current.Points.Add(p); }
                canvas.Invalidate();
            };
            canvas.MouseUp += (s, e) =>
            {
                if (current == null) return;
                if (current.Kind == "pen" || current.Kind == "highlight" || current.Bounds.Width > 3 || current.Bounds.Height > 3) marks.Add(current);
                current = null;
                canvas.Invalidate();
            };
        }

        Func<int> AddSwatches()
        {
            var row = new FlowLayoutPanel { Width = SideInner, AutoSize = true, MaximumSize = new Size(SideInner, 0), Margin = new Padding(0, 0, 0, 8) };
            int selected = 0;
            var buttons = new List<Button>();
            for (int i = 0; i < Palette.Length; i++)
            {
                int idx = i;
                var b = new Button { Width = 30, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Palette[i], Margin = new Padding(0, 0, 5, 5) };
                b.FlatAppearance.BorderSize = i == 0 ? 3 : 1;
                b.FlatAppearance.BorderColor = Color.FromArgb(240, 96, 20);
                b.Click += (s, e) => { selected = idx; for (int j = 0; j < buttons.Count; j++) buttons[j].FlatAppearance.BorderSize = j == idx ? 3 : 1; };
                buttons.Add(b);
                row.Controls.Add(b);
            }
            side.Controls.Add(row);
            return () => selected;
        }

        float StrokeWidth { get { return width.Value * Math.Max(Source.Width, Source.Height) / 1000f; } }

        void OnDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var p = canvas.ToImage(e.Location);
            string kind = redactOnly ? "redact" : ToolKinds[tool()];
            if (kind == "text")
            {
                var dlg = new KDialog(S.T("t_text"));
                var box = dlg.AddInput(S.T("text_prompt"), "");
                if (dlg.Ask(S.T("ok")) && box.Text.Trim().Length > 0)
                {
                    marks.Add(new Mark { Kind = "text", Color = Palette[colorPick()], Width = StrokeWidth, Text = box.Text.Trim(), Points = { p } });
                    canvas.Invalidate();
                }
                return;
            }
            current = new Mark { Kind = kind, Color = redactOnly ? Color.Black : Palette[colorPick()], Width = redactOnly ? 1 : StrokeWidth, RedactMode = redactMode(), Points = { p } };
        }

        List<Mark> marksAtSave;
        protected override void BeforeSave()
        {
            foreach (var m in marks.Where(m => m.Kind == "redact")) m.RedactMode = redactMode();
            marksAtSave = marks.ToList();
        }

        protected override Bitmap RenderFull(Bitmap source)
        {
            var result = new Bitmap(source);
            using (var g = Graphics.FromImage(result))
                foreach (var m in marksAtSave) m.Draw(g, m.Kind == "redact" ? source : result);
            return result;
        }

        protected override string OutputSuffix { get { return redactOnly ? "redacted" : "annotated"; } }

        protected override void ResetAll() { marks.Clear(); canvas.Invalidate(); }
    }
}
