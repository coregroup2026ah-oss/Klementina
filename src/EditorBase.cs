using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Klementina
{
    /// Shared look for editor windows: warm header, dark preview canvas, a column of controls, and Reset / Save buttons.
    class EditorWindow : Form
    {
        protected static readonly Color Warm1 = Color.FromArgb(255, 200, 92), Warm2 = Color.FromArgb(255, 140, 60);
        protected static readonly Color Panel1 = Color.FromArgb(255, 246, 238), Ink = Color.FromArgb(58, 36, 22), Accent = Color.FromArgb(240, 96, 20);

        protected readonly string SrcPath;
        protected Bitmap Source;          // full resolution, owned by the editor
        protected readonly Canvas canvas;
        protected readonly FlowLayoutPanel side;
        protected readonly Button saveButton, resetButton;
        readonly Label titleLabel;

        public EditorWindow(string title, string path, int sideWidth = 330, Bitmap preloaded = null)
        {
            SrcPath = path;
            Text = title + " — " + Path.GetFileName(path);
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1180, 800);
            MinimumSize = new Size(820, 560);
            BackColor = Panel1;
            Icon = Art.AppIcon(32);
            KeyPreview = true;
            Ui.ApplyDirection(this);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            var header = new Panel { Dock = DockStyle.Top, Height = 54 };
            header.Paint += (s, e) =>
            {
                var r = header.ClientRectangle;
                if (r.Width < 1 || r.Height < 1) return; // minimized / mid-layout
                using (var b = new LinearGradientBrush(r, Warm1, Warm2, 0f)) e.Graphics.FillRectangle(b, r);
            };
            titleLabel = new Label
            {
                Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Ink,
            };
            header.Controls.Add(titleLabel);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(16, 10, 16, 10), BackColor = Color.FromArgb(255, 238, 224) };
            saveButton = MakeButton(S.T("save_copy"), true);
            saveButton.Dock = S.Ar ? DockStyle.Left : DockStyle.Right;
            saveButton.Click += (s, e) => Save();
            resetButton = MakeButton(S.T("reset"), false);
            resetButton.Dock = S.Ar ? DockStyle.Right : DockStyle.Left;
            resetButton.Click += (s, e) => ResetAll();
            footer.Controls.Add(saveButton);
            footer.Controls.Add(resetButton);

            side = new FlowLayoutPanel
            {
                Dock = S.Ar ? DockStyle.Left : DockStyle.Right, Width = sideWidth, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(14, 12, 14, 12), BackColor = Panel1,
            };
            canvas = new Canvas { Dock = DockStyle.Fill };

            Controls.Add(canvas);
            Controls.Add(side);
            Controls.Add(footer);
            Controls.Add(header);

            if (preloaded != null) Source = preloaded;
            else using (var img = ImageOps.ToGdi(ImageOps.Load(path))) Source = new Bitmap(img);
            Shown += (s, e) => { Ui.ForceForeground(Handle); canvas.Focus(); };
        }

        protected Button MakeButton(string text, bool primary)
        {
            var b = new Button { Text = text, Width = 150, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 10f) };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(230, 190, 160);
            b.BackColor = primary ? Accent : Color.White;
            b.ForeColor = primary ? Color.White : Ink;
            return b;
        }

        // ---------------- side-panel helpers ----------------

        protected int SideInner { get { return side.Width - side.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4; } }

        protected Label AddSection(string text)
        {
            var l = new Label { Text = text, AutoSize = false, Width = SideInner, Height = 30, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Accent, TextAlign = ContentAlignment.BottomLeft, Margin = new Padding(0, 8, 0, 2) };
            if (S.Ar) l.TextAlign = ContentAlignment.BottomRight;
            side.Controls.Add(l);
            return l;
        }

        protected Label AddNote(string text)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(SideInner, 0), ForeColor = Color.FromArgb(120, 90, 70), Margin = new Padding(0, 2, 0, 6) };
            side.Controls.Add(l);
            return l;
        }

        /// A labelled slider with a value readout and a small reset button. Returns the TrackBar.
        protected TrackBar AddSlider(string label, int min, int max, int value, Func<int, string> format, Action changed)
        {
            var row = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Width = SideInner, Height = 58, Margin = new Padding(0, 0, 0, 2) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
            var name = new Label { Text = label, AutoSize = true, ForeColor = Ink, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            var val = new Label { Text = format(value), AutoSize = true, ForeColor = Color.FromArgb(150, 110, 80), Anchor = AnchorStyles.Right };
            var tb = new TrackBar { Minimum = min, Maximum = max, Value = value, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Height = 30 };
            int def = value;
            var reset = new Button { Text = "↺", FlatStyle = FlatStyle.Flat, Width = 26, Height = 26, ForeColor = Color.FromArgb(170, 120, 90), Margin = new Padding(0) };
            reset.FlatAppearance.BorderSize = 0;
            reset.Click += (s, e) => tb.Value = def;
            tb.ValueChanged += (s, e) => { val.Text = format(tb.Value); changed(); };
            row.Controls.Add(name, 0, 0);
            row.Controls.Add(val, 1, 0);
            row.Controls.Add(tb, 0, 1);
            row.SetColumnSpan(tb, 2);
            row.Controls.Add(reset, 2, 1);
            side.Controls.Add(row);
            return tb;
        }

        /// A segmented choice (toggle buttons). Returns a getter for the selected index.
        protected Func<int> AddSegments(string label, string[] options, int selected, Action changed)
        {
            if (label != null) AddNote(label).ForeColor = Ink;
            var row = new FlowLayoutPanel { Width = SideInner, AutoSize = true, MaximumSize = new Size(SideInner, 0), WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
            var list = new List<RadioButton>();
            for (int i = 0; i < options.Length; i++)
            {
                var rb = new RadioButton
                {
                    Text = options[i], Appearance = Appearance.Button, AutoSize = true, Checked = i == selected, FlatStyle = FlatStyle.Flat,
                    Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 0, 4, 4), TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.White,
                };
                rb.FlatAppearance.CheckedBackColor = Color.FromArgb(255, 214, 180);
                rb.FlatAppearance.BorderColor = Color.FromArgb(230, 180, 140);
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked && changed != null) changed(); };
                list.Add(rb);
                row.Controls.Add(rb);
            }
            side.Controls.Add(row);
            return () => Math.Max(0, list.FindIndex(r => r.Checked));
        }

        protected Button AddSideButton(string text, Action click)
        {
            var b = MakeButton(text, false);
            b.Width = SideInner; b.Height = 34; b.Margin = new Padding(0, 2, 0, 4);
            b.Click += (s, e) => click();
            side.Controls.Add(b);
            return b;
        }

        // ---------------- overridables ----------------

        /// The image shown in the canvas (may be a quick, downscaled render).
        protected virtual void RefreshPreview() { canvas.Image = Source; canvas.Invalidate(); }

        /// The finished full-resolution image. Runs on a worker thread; must not touch controls.
        protected virtual Bitmap RenderFull(Bitmap source) { return new Bitmap(source); }

        protected virtual void ResetAll() { }

        protected virtual string OutputSuffix { get { return "edited"; } }

        /// Default: keep the source format; PNG when the result needs transparency or the source can't be written.
        protected virtual string OutputExt { get { return ImageOps.SameFormatExt(SrcPath); } }

        /// Called on the UI thread right before saving: copy whatever the worker needs out of the controls.
        protected virtual void BeforeSave() { }

        protected virtual void Save()
        {
            BeforeSave();
            Bitmap src = new Bitmap(Source);
            string ext = OutputExt, suffix = OutputSuffix, path = SrcPath;
            Func<Bitmap, Bitmap> render = RenderFull;
            saveButton.Enabled = false;
            Program.RunWorker(() =>
            {
                using (src)
                using (var result = render(src))
                {
                    string dest = Ops.Out(path, ext, suffix);
                    ImageOps.SaveGdi(result, dest, ext);
                    var r = new OpResult();
                    r.Outputs.Add(dest);
                    return r;
                }
            }, false);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // stop painting before the bitmaps it shows are disposed
            canvas.Image = null;
            canvas.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (Source != null) Source.Dispose();
            base.OnFormClosed(e);
        }
    }

    /// Dark, double-buffered preview surface that fits an image and maps mouse points to image pixels.
    class Canvas : Panel
    {
        public Image Image;
        public event Action<Graphics, RectangleF> PaintOverlay;
        public float Zoom = 1f; // extra zoom on top of fit (1 = fit)

        public Canvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(26, 20, 16);
            SetStyle(ControlStyles.Selectable, true);
        }

        public RectangleF ImageRect
        {
            get
            {
                if (Image == null) return RectangleF.Empty;
                float pad = 18;
                float s = Math.Min((Width - 2 * pad) / Image.Width, (Height - 2 * pad) / Image.Height) * Zoom;
                if (s <= 0) s = 0.01f;
                float w = Image.Width * s, h = Image.Height * s;
                return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
            }
        }

        public float ViewScale { get { var r = ImageRect; return Image == null || r.Width <= 0 ? 1 : r.Width / Image.Width; } }

        public PointF ToImage(Point p)
        {
            var r = ImageRect;
            float s = ViewScale;
            return new PointF((p.X - r.X) / s, (p.Y - r.Y) / s);
        }

        public PointF ToScreen(PointF p)
        {
            var r = ImageRect;
            float s = ViewScale;
            return new PointF(r.X + p.X * s, r.Y + p.Y * s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Image == null || Width < 40 || Height < 40) return;
            var r = ImageRect;
            if (r.Width < 1 || r.Height < 1) return;
            // checkerboard behind transparent images
            using (var hb = new HatchBrush(HatchStyle.LargeCheckerBoard, Color.FromArgb(60, 60, 60), Color.FromArgb(45, 45, 45)))
                g.FillRectangle(hb, r);
            g.DrawImage(Image, r);
            if (PaintOverlay != null) PaintOverlay(g, r);
        }
    }
}
