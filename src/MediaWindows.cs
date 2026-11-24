using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using WM = System.Windows.Media;

namespace Klementina
{
    /// Waveform (audio) or frame strip (video) with a selection, bleep ranges and a playhead.
    class Timeline : Control
    {
        public double Duration = 1, Start, End = 1, Playhead = -1;
        public float[] Peaks;                       // 0..1 per bin
        public List<Bitmap> Thumbs = new List<Bitmap>();
        public List<double[]> Ranges = new List<double[]>(); // bleep ranges
        public bool RangeMode;                      // drag adds a range instead of moving handles
        public event Action Changed;
        public event Action<double> Seek;

        enum DragKind { None, Start, End, Range, Seek }
        DragKind drag;
        double rangeFrom;

        public Timeline()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Height = 130;
            Cursor = Cursors.Hand;
        }

        const int RulerH = 22;
        Rectangle Track { get { return new Rectangle(14, 8, Math.Max(10, Width - 28), Math.Max(10, Height - 8 - RulerH)); } }
        float X(double t) { var r = Track; return r.X + (float)(t / Duration * r.Width); }
        double T(int x) { var r = Track; return Math.Max(0, Math.Min(Duration, (x - r.X) / (double)r.Width * Duration)); }

        public static string Clock(double t, bool tenths)
        {
            if (t < 0) t = 0;
            int h = (int)(t / 3600), m = (int)(t / 60) % 60;
            double sec = t - h * 3600 - m * 60;
            string ss = tenths ? sec.ToString("00.0", System.Globalization.CultureInfo.InvariantCulture) : ((int)sec).ToString("00");
            return (h > 0 ? h + ":" + m.ToString("00") : m.ToString()) + ":" + ss;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            var r = Track;
            using (var clip = Theme.Round(r, 8))
            {
                using (var bg = new SolidBrush(Color.FromArgb(255, 226, 200))) g.FillPath(bg, clip);
                var saved = g.Clip;
                g.SetClip(clip);
                if (Thumbs.Count > 0)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    float w = r.Width / (float)Thumbs.Count;
                    for (int i = 0; i < Thumbs.Count; i++)
                    {
                        // keep each frame's shape: crop the middle of it to the slot
                        var th = Thumbs[i];
                        float slotAspect = w / r.Height, imgAspect = th.Width / (float)th.Height;
                        RectangleF src = imgAspect > slotAspect
                            ? new RectangleF((th.Width - th.Height * slotAspect) / 2, 0, th.Height * slotAspect, th.Height)
                            : new RectangleF(0, (th.Height - th.Width / slotAspect) / 2, th.Width, th.Width / slotAspect);
                        g.DrawImage(th, new RectangleF(r.X + i * w, r.Y, w + 1, r.Height), src, GraphicsUnit.Pixel);
                    }
                }
                else if (Peaks != null)
                {
                    float mid = r.Y + r.Height / 2f, bw = r.Width / (float)Peaks.Length;
                    using (var p = new Pen(Color.FromArgb(240, 96, 20), Math.Max(1f, bw * 0.6f)))
                        for (int i = 0; i < Peaks.Length; i++)
                        {
                            float h = Math.Max(1, Peaks[i] * r.Height * 0.46f), x = r.X + i * bw + bw / 2;
                            g.DrawLine(p, x, mid - h, x, mid + h);
                        }
                }
                // outside the selection is dimmed (not in bleep mode, which has no selection)
                if (!RangeMode)
                    using (var dim = new SolidBrush(Color.FromArgb(165, 255, 246, 238)))
                    {
                        g.FillRectangle(dim, r.X, r.Y, X(Start) - r.X, r.Height);
                        g.FillRectangle(dim, X(End), r.Y, r.Right - X(End), r.Height);
                    }
                foreach (var rg in Ranges)
                    using (var rb = new SolidBrush(Color.FromArgb(130, 30, 30, 30))) g.FillRectangle(rb, X(rg[0]), r.Y, Math.Max(2, X(rg[1]) - X(rg[0])), r.Height);
                g.Clip = saved;
            }

            if (!RangeMode)
            {
                using (var frame = new Pen(Theme.Accent, 3))
                using (var sel = Theme.Round(new RectangleF(X(Start), r.Y + 1.5f, Math.Max(1, X(End) - X(Start)), r.Height - 3), 6))
                    g.DrawPath(frame, sel);
                foreach (var t in new[] { Start, End })
                {
                    var hr = new RectangleF(X(t) - 6, r.Y - 2, 12, r.Height + 4);
                    using (var hp = Theme.Round(hr, 5))
                    using (var hb = new SolidBrush(Theme.Accent)) g.FillPath(hb, hp);
                    float cy = r.Y + r.Height / 2f;
                    using (var grip = new Pen(Color.White, 1.5f))
                    {
                        g.DrawLine(grip, X(t) - 2, cy - 8, X(t) - 2, cy + 8);
                        g.DrawLine(grip, X(t) + 2, cy - 8, X(t) + 2, cy + 8);
                    }
                }
            }
            if (Playhead >= 0)
            {
                float px = X(Playhead);
                using (var ph = new Pen(Color.FromArgb(40, 30, 24), 2)) g.DrawLine(ph, px, r.Y - 4, px, r.Bottom + 4);
                using (var tri = new SolidBrush(Color.FromArgb(40, 30, 24)))
                    g.FillPolygon(tri, new[] { new PointF(px - 5, r.Y - 8), new PointF(px + 5, r.Y - 8), new PointF(px, r.Y - 2) });
            }

            // ruler: ticks at a round step that leaves room for the labels
            var steps = new[] { 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
            double step = steps[steps.Length - 1];
            foreach (var s in steps) if (s / Duration * r.Width >= 70) { step = s; break; }
            var font = SystemFonts.DefaultFont;
            using (var tick = new Pen(Color.FromArgb(200, 170, 145)))
                for (double t = 0; t <= Duration + 1e-6; t += step)
                {
                    float x = X(t);
                    g.DrawLine(tick, x, r.Bottom + 3, x, r.Bottom + 7);
                    string lab = Clock(t, step < 1);
                    var sz = TextRenderer.MeasureText(lab, font);
                    float lx = Math.Max(0, Math.Min(Width - sz.Width, x - sz.Width / 2f));
                    TextRenderer.DrawText(g, lab, font, new Point((int)lx, r.Bottom + 7), Color.FromArgb(150, 115, 90));
                }

            // while a handle is dragged, its time floats over the strip
            if (drag == DragKind.Start || drag == DragKind.End)
            {
                double t = drag == DragKind.Start ? Start : End;
                string lab = Clock(t, true);
                using (var f = new Font("Segoe UI Semibold", 9f))
                {
                    var sz = TextRenderer.MeasureText(lab, f);
                    float bx = Math.Max(r.X, Math.Min(r.Right - sz.Width - 8, X(t) - sz.Width / 2f - 4));
                    var box = new RectangleF(bx, r.Y + 4, sz.Width + 8, sz.Height + 2);
                    using (var bp = Theme.Round(box, 5))
                    using (var bb = new SolidBrush(Color.FromArgb(230, 40, 30, 24))) g.FillPath(bb, bp);
                    TextRenderer.DrawText(g, lab, f, new Point((int)box.X + 4, (int)box.Y + 1), Color.White);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (RangeMode) { drag = DragKind.Range; rangeFrom = T(e.X); Ranges.Add(new[] { rangeFrom, rangeFrom }); return; }
            if (Math.Abs(e.X - X(Start)) < 10) drag = DragKind.Start;
            else if (Math.Abs(e.X - X(End)) < 10) drag = DragKind.End;
            else { drag = DragKind.Seek; if (Seek != null) Seek(T(e.X)); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag == DragKind.None) { Cursor = !RangeMode && (Math.Abs(e.X - X(Start)) < 10 || Math.Abs(e.X - X(End)) < 10) ? Cursors.SizeWE : Cursors.Hand; return; }
            double t = T(e.X);
            switch (drag)
            {
                case DragKind.Start: Start = Math.Min(t, End - 0.05); if (Seek != null) Seek(Start); break;
                case DragKind.End: End = Math.Max(t, Start + 0.05); if (Seek != null) Seek(End); break;
                case DragKind.Range: Ranges[Ranges.Count - 1] = new[] { Math.Min(rangeFrom, t), Math.Max(rangeFrom, t) }; break;
                case DragKind.Seek: if (Seek != null) Seek(t); break;
            }
            Invalidate();
            if (Changed != null) Changed();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (drag == DragKind.Range)
            {
                var last = Ranges[Ranges.Count - 1];
                if (last[1] - last[0] < 0.05) Ranges.RemoveAt(Ranges.Count - 1); // a click, not a drag
                if (Changed != null) Changed();
            }
            drag = DragKind.None;
            Invalidate();
        }
    }

    /// Audio/video tools with a timeline: Trim (+ snapshot for video), Bleep, Channels.
    class MediaToolWindow : Form
    {
        public enum Mode { Trim, Bleep, Channels }
        readonly Mode mode;
        readonly string path;
        readonly MediaOps.Info info;
        readonly bool isVideo;
        readonly Timeline timeline = new Timeline { Dock = DockStyle.Fill };
        readonly Label timeLabel = new Label { AutoSize = true, Font = new Font("Consolas", 11f), ForeColor = Color.FromArgb(58, 36, 22) };
        readonly Timer tick = new Timer { Interval = 40 };
        WM.MediaPlayer audio;
        System.Windows.Forms.Integration.ElementHost videoHost;
        System.Windows.Controls.MediaElement video;
        PictureBox framePreview;           // fallback when Windows can't play the format
        double stopAt = -1;
        string tempDir;
        CheckBox trimSilence;
        Func<int> channelMode;
        TrackBar leftVol, rightVol;
        Label rangeCount;

        public MediaToolWindow(string file, Mode m)
        {
            path = file; mode = m;
            info = MediaOps.Probe(file);
            if (info.Duration <= 0) throw new Exception(S.T("no_duration"));
            isVideo = info.HasVideo && Catalog.CatOf(file) == Cat.Video;
            tempDir = Ops.TempDir();

            string title = S.T(m == Mode.Trim ? "trim" : m == Mode.Bleep ? "bleep" : "channels");
            Text = title + " — " + Path.GetFileName(file);
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            Size = isVideo ? new Size(1000, 760) : new Size(900, 470);
            MinimumSize = new Size(640, 380);
            BackColor = Color.FromArgb(255, 246, 238);
            Icon = Art.AppIcon(32);
            KeyPreview = true;
            Ui.ApplyDirection(this);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); if (e.KeyCode == Keys.Space) { TogglePlay(); e.Handled = true; } };

            var header = new Panel { Dock = DockStyle.Top, Height = 54 };
            header.Paint += (s, e) =>
            {
                var hr = header.ClientRectangle;
                if (hr.Width < 1 || hr.Height < 1) return;
                using (var gb = new System.Drawing.Drawing2D.LinearGradientBrush(hr, Theme.Warm1, Theme.Warm2, 0f)) e.Graphics.FillRectangle(gb, hr);
            };
            header.Controls.Add(new Label
            {
                Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Ink,
            });

            var tlPanel = new Panel { Dock = isVideo ? DockStyle.Bottom : DockStyle.Fill, Height = 150, Padding = new Padding(12, 8, 12, 4) };
            tlPanel.Controls.Add(timeline);
            timeline.RightToLeft = RightToLeft.No; // time always runs left to right

            var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 6, 12, 6), WrapContents = true };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 8, 12, 10), BackColor = Color.FromArgb(255, 238, 224), FlowDirection = FlowDirection.RightToLeft };
            if (S.Rtl) footer.FlowDirection = FlowDirection.LeftToRight;

            controls.Controls.Add(Btn("▶  " + S.T(m == Mode.Trim ? "play_selection" : "play_original"), false, () => PlayRange(timeline.Start, timeline.End, path)));
            if (m == Mode.Trim)
            {
                trimSilence = new CheckBox { Text = S.T("trim_silence"), AutoSize = true, Margin = new Padding(12, 8, 6, 0), Enabled = info.HasAudio };
                trimSilence.CheckedChanged += (s, e) => { if (trimSilence.Checked) SnapToSound(); };
                controls.Controls.Add(trimSilence);
                if (isVideo) controls.Controls.Add(Btn(S.T("snapshot"), false, Snapshot));
            }
            if (m == Mode.Bleep)
            {
                timeline.RangeMode = true;
                controls.Controls.Add(Btn("▶  " + S.T("play_bleeped"), false, () => PreviewProcessed()));
                controls.Controls.Add(Btn("↶  " + S.T("undo"), false, () => { if (timeline.Ranges.Count > 0) { timeline.Ranges.RemoveAt(timeline.Ranges.Count - 1); timeline.Invalidate(); UpdateLabels(); } }));
                rangeCount = new Label { AutoSize = true, Margin = new Padding(12, 9, 0, 0) };
                controls.Controls.Add(rangeCount);
            }
            if (m == Mode.Channels)
            {
                var opts = new[] { S.T("mono"), S.T("stereo") };
                var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
                var rbs = new List<RadioButton>();
                for (int i = 0; i < 2; i++)
                {
                    var rb = new RadioButton { Text = opts[i], Appearance = Appearance.Button, AutoSize = true, Checked = i == 1, FlatStyle = FlatStyle.Flat, Padding = new Padding(10, 3, 10, 3), BackColor = Color.White };
                    rb.FlatAppearance.CheckedBackColor = Color.FromArgb(255, 214, 180);
                    rbs.Add(rb); row.Controls.Add(rb);
                }
                channelMode = () => rbs.FindIndex(r => r.Checked);
                controls.Controls.Add(row);
                controls.Controls.Add(new Label { Text = S.T("left"), AutoSize = true, Margin = new Padding(12, 9, 0, 0) });
                leftVol = new TrackBar { Minimum = 0, Maximum = 200, Value = 100, Width = 160, TickStyle = TickStyle.None };
                controls.Controls.Add(leftVol);
                controls.Controls.Add(new Label { Text = S.T("right"), AutoSize = true, Margin = new Padding(12, 9, 0, 0) });
                rightVol = new TrackBar { Minimum = 0, Maximum = 200, Value = 100, Width = 160, TickStyle = TickStyle.None };
                controls.Controls.Add(rightVol);
                controls.Controls.Add(Btn("▶  " + S.T("play_output"), false, () => PreviewProcessed()));
            }
            controls.Controls.Add(timeLabel);

            footer.Controls.Add(Btn(S.T(m == Mode.Bleep ? "save_bleeped" : "apply"), true, Apply));
            footer.Controls.Add(Btn(S.T("close"), false, Close));
            if (m == Mode.Bleep) footer.Controls.Add(new Label { Text = S.T("bleep_hint"), AutoSize = true, Margin = new Padding(12, 9, 12, 0), ForeColor = Color.FromArgb(120, 90, 70) });

            if (isVideo) BuildVideoArea();
            Controls.Add(tlPanel);
            Controls.Add(controls);
            Controls.Add(footer);
            Controls.Add(header);
            if (isVideo) { videoHost.BringToFront(); }

            timeline.Duration = info.Duration;
            timeline.Start = 0; timeline.End = info.Duration;
            timeline.Changed += UpdateLabels;
            timeline.Seek += SeekTo;
            tick.Tick += (s, e) => OnTick();
            tick.Start();
            UpdateLabels();
            Shown += (s, e) => { Ui.ForceForeground(Handle); LoadVisuals(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // trim window only, and not while a slider has the keys (the channel volumes use the arrows)
            if (mode == Mode.Trim && !(ActiveControl is TrackBar) && !(ActiveControl is TextBoxBase))
            {
                double ph = timeline.Playhead >= 0 ? timeline.Playhead : timeline.Start;
                double step = (keyData & Keys.Shift) != 0 ? 1.0 : 0.1;
                switch (keyData & ~Keys.Shift)
                {
                    case Keys.I: timeline.Start = Math.Min(ph, timeline.End - 0.05); timeline.Invalidate(); UpdateLabels(); return true;
                    case Keys.O: timeline.End = Math.Max(ph, timeline.Start + 0.05); timeline.Invalidate(); UpdateLabels(); return true;
                    case Keys.Left: SeekTo(Math.Max(0, ph - step)); return true;
                    case Keys.Right: SeekTo(Math.Min(info.Duration, ph + step)); return true;
                    case Keys.Home: SeekTo(timeline.Start); return true;
                    case Keys.End: SeekTo(timeline.End); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        Button Btn(string text, bool primary, Action click)
        {
            var b = Theme.MakeButton(text, primary);
            b.AutoSize = true;
            b.MinimumSize = new Size(110, 36);
            b.Margin = new Padding(0, 0, 8, 0);
            b.Click += (s, e) => click();
            return b;
        }

        void BuildVideoArea()
        {
            videoHost = new System.Windows.Forms.Integration.ElementHost { Dock = DockStyle.Fill, BackColor = Color.Black };
            video = new System.Windows.Controls.MediaElement
            {
                LoadedBehavior = System.Windows.Controls.MediaState.Manual, UnloadedBehavior = System.Windows.Controls.MediaState.Manual,
                ScrubbingEnabled = true, Stretch = WM.Stretch.Uniform,
            };
            var grid = new System.Windows.Controls.Grid { Background = WM.Brushes.Black };
            grid.Children.Add(video);
            videoHost.Child = grid;
            video.MediaFailed += (s, e) => UseFramePreview();
            video.Source = new Uri(path);
            video.Play(); video.Pause();
            Controls.Add(videoHost);
        }

        /// Windows couldn't decode it (e.g. some MKV/WebM/AVI): show still frames from FFmpeg instead.
        void UseFramePreview()
        {
            if (framePreview != null) return;
            video = null;
            videoHost.Visible = false;
            framePreview = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            Controls.Add(framePreview);
            framePreview.BringToFront();
            ShowFrameAt(0);
        }

        void ShowFrameAt(double t)
        {
            if (framePreview == null) return;
            try
            {
                string f = Path.Combine(tempDir, "frame.jpg");
                Ops.Ffmpeg("-ss " + t.ToString("0.###", Ops.Inv) + " -i " + Ops.Q(path) + " -frames:v 1 -q:v 3 -vf scale='min(960,iw)':-2 " + Ops.Q(f), f);
                using (var fs = new MemoryStream(File.ReadAllBytes(f)))
                {
                    var old = framePreview.Image;
                    framePreview.Image = new Bitmap(fs);
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }

        /// Waveform for audio, a strip of frames for video (both from FFmpeg, off the UI thread).
        void LoadVisuals()
        {
            string tmp = tempDir;
            var th = new System.Threading.Thread(() =>
            {
                float[] peaks = null;
                var thumbs = new List<Bitmap>();
                try
                {
                    if (info.HasAudio)
                    {
                        string raw = Path.Combine(tmp, "pcm.raw");
                        Ops.Ffmpeg("-i " + Ops.Q(path) + " -vn -ac 1 -ar 4000 -f s16le " + Ops.Q(raw), raw);
                        var bytes = File.ReadAllBytes(raw);
                        int n = bytes.Length / 2, bins = 900;
                        peaks = new float[bins];
                        float max = 1;
                        for (int b = 0; b < bins; b++)
                        {
                            int a = (int)((long)b * n / bins), z = (int)((long)(b + 1) * n / bins);
                            int pk = 0;
                            for (int i = a; i < z; i++) { int v = Math.Abs((int)BitConverter.ToInt16(bytes, i * 2)); if (v > pk) pk = v; }
                            peaks[b] = pk; if (pk > max) max = pk;
                        }
                        for (int b = 0; b < bins; b++) peaks[b] /= max;
                    }
                    if (isVideo)
                    {
                        // one quick seek per frame, a few at a time: a long film no longer has to be decoded end to end
                        int count = 16;
                        var frames = new Bitmap[count];
                        System.Threading.Tasks.Parallel.For(0, count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                        {
                            try
                            {
                                double at = (i + 0.5) * info.Duration / count;
                                string f = Path.Combine(tmp, "t" + i.ToString("00") + ".jpg");
                                Ops.Ffmpeg("-ss " + at.ToString("0.###", Ops.Inv) + " -i " + Ops.Q(path) + " -frames:v 1 -vf scale=-2:110 -q:v 4 " + Ops.Q(f), f);
                                using (var ms = new MemoryStream(File.ReadAllBytes(f))) frames[i] = new Bitmap(ms);
                            }
                            catch { }
                        });
                        thumbs.AddRange(frames.Where(fr => fr != null));
                    }
                }
                catch { }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        timeline.Peaks = peaks;
                        timeline.Thumbs = thumbs;
                        timeline.Invalidate();
                    }));
                }
                catch { }
            }) { IsBackground = true };
            th.Start();
        }

        // ---------------- playback ----------------

        void SeekTo(double t)
        {
            timeline.Playhead = t;
            if (video != null) { video.Pause(); video.Position = TimeSpan.FromSeconds(t); }
            else if (framePreview != null) ShowFrameAt(t);
            if (audio != null) audio.Pause();
            stopAt = -1;
            timeline.Invalidate();
        }

        /// Plays [from, to] of the original (video in the preview, otherwise audio only).
        void PlayRange(double from, double to, string file)
        {
            StopAll();
            if (video != null && file == path)
            {
                video.Position = TimeSpan.FromSeconds(from);
                video.Play();
            }
            else StartAudio(file, from);
            stopAt = to;
        }

        void StartAudio(string file, double from)
        {
            var player = new WM.MediaPlayer();
            // position can only be set once the file is open
            player.MediaOpened += (s, e) => { player.Position = TimeSpan.FromSeconds(from); player.Play(); };
            player.Open(new Uri(file));
            audio = player;
        }

        void TogglePlay()
        {
            if (stopAt >= 0) { StopAll(); return; }
            PlayRange(timeline.Start, timeline.End, path);
        }

        void StopAll()
        {
            if (video != null) video.Pause();
            if (audio != null) { audio.Stop(); audio.Close(); audio = null; }
            stopAt = -1;
        }

        void OnTick()
        {
            if (stopAt < 0) return;
            double pos;
            if (audio != null) pos = audio.Position.TotalSeconds;
            else if (video != null) pos = video.Position.TotalSeconds;
            else return;
            timeline.Playhead = pos;
            timeline.Invalidate();
            if (pos >= stopAt) StopAll();
        }

        static string Fmt(double t)
        {
            int m = (int)(t / 60);
            return m + ":" + (t - m * 60).ToString("00.00", Ops.Inv);
        }

        void UpdateLabels()
        {
            timeLabel.Text = mode == Mode.Bleep ? "" : S.T("start") + " " + Fmt(timeline.Start) + "   " + S.T("end") + " " + Fmt(timeline.End) + "   (" + Fmt(timeline.End - timeline.Start) + ")";
            if (rangeCount != null) rangeCount.Text = S.T("bleeps") + ": " + timeline.Ranges.Count;
        }

        /// Moves the handles inward past silence at both ends (anything quieter than 4% of the peak).
        void SnapToSound()
        {
            var p = timeline.Peaks;
            if (p == null) return;
            int a = 0, b = p.Length - 1;
            while (a < b && p[a] < 0.04f) a++;
            while (b > a && p[b] < 0.04f) b--;
            timeline.Start = Math.Max(0, (a - 1) / (double)p.Length * info.Duration);
            timeline.End = Math.Min(info.Duration, (b + 2) / (double)p.Length * info.Duration);
            timeline.Invalidate();
            UpdateLabels();
        }

        void Snapshot()
        {
            double t = Math.Max(0, timeline.Playhead);
            string src = path;
            Program.RunWorker(() =>
            {
                string dest = Ops.Out(src, "jpg", "frame");
                Ops.Ffmpeg("-ss " + t.ToString("0.###", Ops.Inv) + " -i " + Ops.Q(src) + " -frames:v 1 -q:v 2 " + Ops.Q(dest), dest);
                var r = new OpResult(); r.Outputs.Add(dest); return r;
            }, false);
        }

        // ---------------- processing ----------------

        static string Between(List<double[]> ranges)
        {
            return string.Join("+", ranges.Select(r => "between(t," + r[0].ToString("0.###", Ops.Inv) + "," + r[1].ToString("0.###", Ops.Inv) + ")"));
        }

        /// The audio filter for the current settings (bleep or channels).
        string AudioFilter(out bool complex)
        {
            complex = false;
            if (mode == Mode.Bleep)
            {
                if (timeline.Ranges.Count == 0) return null;
                complex = true;
                string on = Between(timeline.Ranges);
                return "[0:a]volume=enable='" + on + "':volume=0[muted];" +
                       "sine=frequency=1000:sample_rate=48000:duration=" + info.Duration.ToString("0.###", Ops.Inv) + ",volume=0.25,volume=enable='not(" + on + ")':volume=0[tone];" +
                       "[muted][tone]amix=inputs=2:duration=first:normalize=0[aout]";
            }
            double l = leftVol.Value / 100.0, r = rightVol.Value / 100.0;
            string L = l.ToString("0.###", Ops.Inv), R = r.ToString("0.###", Ops.Inv);
            return channelMode() == 0 ? "pan=mono|c0=" + (l * 0.5).ToString("0.###", Ops.Inv) + "*c0+" + (r * 0.5).ToString("0.###", Ops.Inv) + "*c1"
                                      : "pan=stereo|c0=" + L + "*c0|c1=" + R + "*c1";
        }

        void PreviewProcessed()
        {
            bool complex;
            string filter = AudioFilter(out complex);
            if (filter == null) { PlayRange(0, info.Duration, path); return; }
            string wav = Path.Combine(tempDir, "preview" + Environment.TickCount + ".wav");
            double len = Math.Min(info.Duration, mode == Mode.Bleep ? info.Duration : 20);
            try
            {
                Cursor = Cursors.WaitCursor;
                if (complex) Ops.Ffmpeg("-i " + Ops.Q(path) + " -filter_complex \"" + filter + "\" -map \"[aout]\" -t " + len.ToString("0.###", Ops.Inv) + " " + Ops.Q(wav), wav);
                else Ops.Ffmpeg("-i " + Ops.Q(path) + " -vn -af \"" + filter + "\" -t " + len.ToString("0.###", Ops.Inv) + " " + Ops.Q(wav), wav);
            }
            finally { Cursor = Cursors.Default; }
            StopAll();
            StartAudio(wav, 0);
            stopAt = len;
        }

        void Apply()
        {
            StopAll();
            string src = path, ext = Catalog.Ext(path);
            if (mode == Mode.Trim)
            {
                double a = timeline.Start, b = timeline.End;
                Program.RunWorker(() => Ops.Each(new List<string> { src }, f => MediaOps.Trim(f, a.ToString("0.###", Ops.Inv), b.ToString("0.###", Ops.Inv))), true);
            }
            else
            {
                bool complex;
                string filter = AudioFilter(out complex);
                if (filter == null) { Close(); return; }
                bool video = isVideo;
                string suffix = mode == Mode.Bleep ? "bleeped" : "channels";
                Program.RunWorker(() =>
                {
                    string dest = Ops.Out(src, ext, suffix);
                    string args = "-i " + Ops.Q(src) + " ";
                    if (complex) args += "-filter_complex \"" + filter + "\" " + (video ? "-map 0:v -c:v copy " : "") + "-map \"[aout]\" ";
                    else args += (video ? "-map 0:v -c:v copy -map 0:a " : "") + "-af \"" + filter + "\" ";
                    args += MediaOps.AudioCodecFor(ext);
                    Ops.Ffmpeg(args + " " + Ops.Q(dest), dest);
                    var r = new OpResult(); r.Outputs.Add(dest); return r;
                }, true);
            }
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop();
            StopAll();
            if (video != null) { video.Stop(); video.Source = null; }
            foreach (var t in timeline.Thumbs) t.Dispose();
            if (framePreview != null && framePreview.Image != null) framePreview.Image.Dispose();
            string tmp = tempDir;
            new System.Threading.Thread(() => { System.Threading.Thread.Sleep(1500); Ops.TryDelete(tmp); }) { IsBackground = true }.Start();
            base.OnFormClosed(e);
        }
    }
}
