using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Klementina
{
    /// Reorganize a PDF: page thumbnails you can drag to reorder, rotate or delete, then save a new copy.
    class PdfOrganizer : Form
    {
        class PageTile { public int Number; public int Rotation; public Bitmap Thumb; }

        readonly string path;
        readonly List<PageTile> pages = new List<PageTile>();
        readonly HashSet<PageTile> selected = new HashSet<PageTile>();
        readonly Panel grid;
        readonly string tmp;
        PageTile dragging;
        int dropIndex = -1;
        const int TileW = 150, TileH = 200, Gap = 16;

        public PdfOrganizer(string file)
        {
            path = file;
            Text = S.T("reorganize") + " — " + Path.GetFileName(file);
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1040, 760);
            BackColor = Color.FromArgb(255, 246, 238);
            Icon = Art.AppIcon(32);
            KeyPreview = true;
            Ui.ApplyDirection(this);

            var header = new Label { Text = S.T("reorganize"), Dock = DockStyle.Top, Height = 50, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Color.FromArgb(58, 36, 22), BackColor = Color.FromArgb(255, 176, 76) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 8, 12, 4) };
            bar.Controls.Add(Btn("⟳  " + S.T("rotate_selected"), false, () => { foreach (var p in selected) p.Rotation = (p.Rotation + 90) % 360; grid.Invalidate(); }));
            bar.Controls.Add(Btn("✕  " + S.T("delete_selected"), false, DeleteSelected));
            bar.Controls.Add(Btn(S.T("select_all"), false, () => { selected.Clear(); foreach (var p in pages) selected.Add(p); grid.Invalidate(); }));
            bar.Controls.Add(new Label { Text = S.T("organize_hint"), AutoSize = true, Margin = new Padding(12, 9, 0, 0), ForeColor = Color.FromArgb(120, 90, 70) });

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 8, 12, 10), BackColor = Color.FromArgb(255, 238, 224), FlowDirection = S.Rtl ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
            footer.Controls.Add(Btn(S.T("save_copy"), true, Save));
            footer.Controls.Add(Btn(S.T("close"), false, Close));

            grid = new DoubleBufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(70, 56, 48) };
            grid.RightToLeft = RightToLeft.No; // pages always read left to right
            grid.Paint += PaintGrid;
            grid.MouseDown += OnDown;
            grid.MouseMove += OnMove;
            grid.MouseUp += OnUp;
            grid.Resize += (s, e) => UpdateScroll();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) DeleteSelected(); if (e.KeyCode == Keys.Escape) Close(); };

            Controls.Add(grid);
            Controls.Add(footer);
            Controls.Add(bar);
            Controls.Add(header);

            tmp = Ops.TempDir();
            // open straight away; page previews fill in from a background thread (big PDFs have hundreds of pages)
            int count = PdfTools.PageCount(file);
            for (int i = 0; i < count; i++) pages.Add(new PageTile { Number = i + 1 });
            var byNumber = pages.ToDictionary(p => p.Number);
            UpdateScroll();
            Shown += (s, e) =>
            {
                Ui.ForceForeground(Handle);
                var th = new System.Threading.Thread(() =>
                {
                    try
                    {
                        PdfOps.RenderPagesProgressively(file, tmp, 30, (i, png) =>
                        {
                            Bitmap b;
                            using (var ms = new MemoryStream(File.ReadAllBytes(png))) using (var src = new Bitmap(ms)) b = new Bitmap(src);
                            try { File.Delete(png); } catch { }
                            if (closed) { b.Dispose(); return; }
                            try { BeginInvoke(new Action(() => { if (closed) { b.Dispose(); return; } byNumber[i + 1].Thumb = b; grid.Invalidate(); })); }
                            catch { b.Dispose(); }
                        }, () => closed);
                    }
                    catch { }
                }) { IsBackground = true };
                th.Start();
            };
        }

        volatile bool closed;

        class DoubleBufferedPanel : Panel { public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; } }

        Button Btn(string text, bool primary, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 34), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0) };
            b.BackColor = primary ? Color.FromArgb(240, 96, 20) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(58, 36, 22);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.Click += (s, e) => click();
            return b;
        }

        int Columns { get { return Math.Max(1, (grid.ClientSize.Width - Gap) / (TileW + Gap)); } }

        Rectangle TileRect(int i)
        {
            int c = i % Columns, r = i / Columns;
            return new Rectangle(Gap + c * (TileW + Gap) + grid.AutoScrollPosition.X, Gap + r * (TileH + Gap + 22) + grid.AutoScrollPosition.Y, TileW, TileH);
        }

        void UpdateScroll()
        {
            int rows = (pages.Count + Columns - 1) / Columns;
            grid.AutoScrollMinSize = new Size(0, Gap + rows * (TileH + Gap + 22));
            grid.Invalidate();
        }

        int HitIndex(Point p)
        {
            for (int i = 0; i < pages.Count; i++) if (TileRect(i).Contains(p)) return i;
            return -1;
        }

        /// Insertion point for a drop: before the tile under the cursor (or after it on its right half).
        int InsertIndex(Point p)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                var r = TileRect(i);
                var band = new Rectangle(r.X - Gap / 2, r.Y, r.Width + Gap, r.Height + 22);
                if (band.Contains(p)) return p.X < r.X + r.Width / 2 ? i : i + 1;
            }
            return pages.Count;
        }

        void PaintGrid(object s, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                var r = TileRect(i);
                if (!e.ClipRectangle.IntersectsWith(new Rectangle(r.X, r.Y, r.Width, r.Height + 22))) continue;
                bool sel = selected.Contains(p);
                using (var bg = new SolidBrush(sel ? Color.FromArgb(255, 150, 60) : Color.FromArgb(95, 78, 68))) g.FillRectangle(bg, r);
                // thumbnail, rotated and fitted
                var st = g.Save();
                g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                g.RotateTransform(p.Rotation);
                bool sideways = p.Rotation % 180 != 0;
                float pw = p.Thumb != null ? p.Thumb.Width : 85, ph = p.Thumb != null ? p.Thumb.Height : 110; // placeholder until the preview is ready
                float tw = sideways ? ph : pw, th = sideways ? pw : ph;
                float k = Math.Min((r.Width - 14) / tw, (r.Height - 14) / th);
                float w = pw * k, h = ph * k;
                g.FillRectangle(p.Thumb != null ? Brushes.White : Brushes.Gainsboro, -w / 2, -h / 2, w, h);
                if (p.Thumb != null) g.DrawImage(p.Thumb, -w / 2, -h / 2, w, h);
                g.Restore(st);
                using (var f = new Font("Segoe UI Semibold", 9.5f))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(S.F("page_n", p.Number) + (p.Rotation != 0 ? "  ⟳" + p.Rotation + "°" : ""), f, Brushes.White, new RectangleF(r.X, r.Bottom + 3, r.Width, 20), sf);
            }
            if (dragging != null && dropIndex >= 0)
            {
                Rectangle r = dropIndex < pages.Count ? TileRect(dropIndex) : TileRect(pages.Count - 1);
                int x = dropIndex < pages.Count ? r.X - Gap / 2 : r.Right + Gap / 2;
                using (var pen = new Pen(Color.FromArgb(255, 170, 70), 4)) g.DrawLine(pen, x, r.Y, x, r.Bottom);
            }
        }

        void OnDown(object s, MouseEventArgs e)
        {
            grid.Focus();
            int i = HitIndex(e.Location);
            if (i < 0) { selected.Clear(); grid.Invalidate(); return; }
            var p = pages[i];
            if ((ModifierKeys & Keys.Control) != 0) { if (!selected.Remove(p)) selected.Add(p); }
            else if (!selected.Contains(p)) { selected.Clear(); selected.Add(p); }
            dragging = p;
            dropIndex = -1;
            grid.Invalidate();
        }

        void OnMove(object s, MouseEventArgs e)
        {
            if (dragging == null || e.Button != MouseButtons.Left) return;
            dropIndex = InsertIndex(e.Location);
            grid.Invalidate();
        }

        void OnUp(object s, MouseEventArgs e)
        {
            if (dragging != null && dropIndex >= 0)
            {
                // move every selected page, keeping their order, to the drop point
                var moving = pages.Where(selected.Contains).ToList();
                int insert = dropIndex - pages.Take(dropIndex).Count(selected.Contains);
                pages.RemoveAll(selected.Contains);
                pages.InsertRange(Math.Max(0, Math.Min(pages.Count, insert)), moving);
            }
            dragging = null;
            dropIndex = -1;
            grid.Invalidate();
        }

        void DeleteSelected()
        {
            if (selected.Count == 0 || selected.Count == pages.Count) return; // keep at least one page
            foreach (var p in selected) if (p.Thumb != null) p.Thumb.Dispose();
            pages.RemoveAll(selected.Contains);
            selected.Clear();
            UpdateScroll();
        }

        void Save()
        {
            string order = string.Join(",", pages.Select(p => p.Number));
            var rotations = pages.Select((p, i) => new { p.Rotation, Index = i + 1 }).Where(x => x.Rotation != 0)
                                 .GroupBy(x => x.Rotation).Select(gr => "--rotate=+" + gr.Key + ":" + string.Join(",", gr.Select(x => x.Index))).ToList();
            string src = path;
            Program.RunWorker(() =>
            {
                string dest = Ops.Out(src, "pdf", "organized");
                string log;
                int code = Ops.Run(Engines.Qpdf, "--empty --pages " + Ops.Q(src) + " " + order + " -- " + string.Join(" ", rotations) + " " + Ops.Q(dest), out log);
                if ((code != 0 && code != 3) || !File.Exists(dest)) throw new Exception(log.Trim().Split('\n').Last());
                var r = new OpResult(); r.Outputs.Add(dest); return r;
            }, true);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closed = true;
            foreach (var p in pages) if (p.Thumb != null) p.Thumb.Dispose();
            string dir = tmp;
            new System.Threading.Thread(() => { System.Threading.Thread.Sleep(2000); Ops.TryDelete(dir); }) { IsBackground = true }.Start();
            base.OnFormClosed(e);
        }
    }
}
