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
            Height = 110;
            Cursor = Cursors.Hand;
        }

        Rectangle Track { get { return new Rectangle(14, 10, Math.Max(10, Width - 28), Math.Max(10, Height - 20)); } }
        float X(double t) { var r = Track; return r.X + (float)(t / Duration * r.Width); }
        double T(int x) { var r = Track; return Math.Max(0, Math.Min(Duration, (x - r.X) / (double)r.Width * Duration)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = Track;
            using (var bg = new SolidBrush(Color.FromArgb(255, 226, 200))) g.FillRectangle(bg, r);
            if (Thumbs.Count > 0)
            {
                float w = r.Width / (float)Thumbs.Count;
                for (int i = 0; i < Thumbs.Count; i++) g.DrawImage(Thumbs[i], r.X + i * w, r.Y, w + 1, r.Height);
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
            using (var dim = new SolidBrush(Color.FromArgb(150, 255, 246, 238)))
            {
                g.FillRectangle(dim, r.X, r.Y, X(Start) - r.X, r.Height);
                g.FillRectangle(dim, X(End), r.Y, r.Right - X(End), r.Height);
            }
            foreach (var rg in Ranges)
                using (var rb = new SolidBrush(Color.FromArgb(120, 30, 30, 30))) g.FillRectangle(rb, X(rg[0]), r.Y, Math.Max(2, X(rg[1]) - X(rg[0])), r.Height);
            if (!RangeMode)
            {
                using (var frame = new Pen(Color.FromArgb(240, 80, 10), 3)) g.DrawRectangle(frame, X(Start), r.Y, X(End) - X(Start), r.Height);
                foreach (var t in new[] { Start, End })
                    using (var hb = new SolidBrush(Color.FromArgb(240, 80, 10)))
                    {
                        g.FillRectangle(hb, X(t) - 6, r.Y, 12, r.Height);
                        g.FillRectangle(Brushes.White, X(t) - 1, r.Y + r.Height / 2 - 10, 2, 20);
                    }
            }
            if (Playhead >= 0)
                using (var ph = new Pen(Color.FromArgb(40, 40, 40), 2)) g.DrawLine(ph, X(Playhead), r.Y - 6, X(Playhead), r.Bottom + 6);
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

            var header = new Label
            {
                Text = title, Dock = DockStyle.Top, Height = 50, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Color.FromArgb(58, 36, 22), BackColor = Color.FromArgb(255, 176, 76),
            };

            var tlPanel = new Panel { Dock = isVideo ? DockStyle.Bottom : DockStyle.Fill, Height = 130, Padding = new Padding(12, 6, 12, 6) };
            tlPanel.Controls.Add(timeline);
            timeline.RightToLeft = RightToLeft.No; // time always runs left to right

            var controls = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 6, 12, 6), WrapContents = true };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 8, 12, 10), BackColor = Color.FromArgb(255, 238, 224), FlowDirection = FlowDirection.RightToLeft };
            if (S.Ar) footer.FlowDirection = FlowDirection.LeftToRight;

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

        Button Btn(string text, bool primary, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(110, 34), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0) };
            b.BackColor = primary ? Color.FromArgb(240, 96, 20) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(58, 36, 22);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
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
                        int count = 14;
                        string pattern = Path.Combine(tmp, "t%02d.jpg");
                        Ops.Ffmpeg("-i " + Ops.Q(path) + " -vf \"fps=" + (count / info.Duration).ToString("0.#####", Ops.Inv) + ",scale=-2:96\" -frames:v " + count + " -q:v 5 " + Ops.Q(pattern), null);
                        foreach (var f in Directory.GetFiles(tmp, "t*.jpg").OrderBy(x => x))
                            using (var ms = new MemoryStream(File.ReadAllBytes(f))) thumbs.Add(new Bitmap(ms));
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
