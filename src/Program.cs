using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Klementina
{
    static class Program
    {
        public static App App;

        /// Results of jobs run while the app isn't running (self-tests): collected here instead of notifications.
        public static readonly List<OpResult> TestResults = new List<OpResult>();

        public static void RunWorker(Func<OpResult> work, bool slow)
        {
            if (App != null) { App.RunWorker(work, slow); return; }
            try { TestResults.Add(work()); }
            catch (Exception ex) { TestResults.Add(new OpResult { Errors = { Ops.Clean(ex) } }); }
        }

        public static void ReportCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klementina");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "errors.log"), DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
            try { if (App != null) App.Notify(S.T("failed"), Ops.Clean(ex), null, ToolTipIcon.Warning); } catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--write-icon") { Art.WriteIco(args[1]); return; }
            if (args.Length == 2 && args[0] == "--installer-art") { Art.WriteInstallerArt(args[1]); return; }

            bool created;
            using (var mutex = new Mutex(true, "Klementina.SingleInstance", out created))
            {
                if (!created) return;
                Native.MakeDpiAware();
                // never show the raw .NET crash dialog: log it and show a short notice instead
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => ReportCrash(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportCrash(e.ExceptionObject as Exception);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.Load();
                Engines.Refresh();
                App = new App();
                // "--float <files>" opens the Float shelf right away (handy for Send To shortcuts).
                if (args.Length > 1 && args[0] == "--float")
                    new FloatForm(args.Skip(1).Where(a => File.Exists(a) || Directory.Exists(a)).ToList()).Show();
                Application.Run(App);
            }
        }
    }

    class App : ApplicationContext
    {
        readonly NotifyIcon tray = new NotifyIcon();
        readonly WheelForm wheel = new WheelForm();
        readonly InputHooks hooks;
        readonly HotkeyWindow hotkey = new HotkeyWindow();
        readonly SynchronizationContext ui;
        readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer { Interval = 350 };
        ToolStripMenuItem pauseItem;
        bool paused;
        int jobs;
        string lastOutput, lastLink;

        public App()
        {
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(ui);

            tray.Icon = Art.AppIcon(32);
            tray.Visible = true;
            tray.BalloonTipClicked += (s, e) =>
            {
                if (lastLink != null) { try { Process.Start(lastLink); } catch { } }
                else if (lastOutput != null) Ui.RevealPath(lastOutput);
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowHelp(); };
            BuildMenu();

            wheel.Execute += Run;
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); if (!wheel.KeyboardMode) wheel.Dismiss(); };

            hooks = new InputHooks();
            hooks.KeyboardWheelOpen = () => wheel.Visible && wheel.KeyboardMode;
            hooks.ModifierTapWhileDragging += OnTapWhileDragging;
            hooks.ModifierTap += key =>
            {
                var k = Settings.KindForKey(key);
                if (k != null && wheel.Visible) wheel.SwitchKind(k.Value);
            };
            hooks.MouseReleased += () => { if (wheel.Visible && !wheel.KeyboardMode) { hideTimer.Stop(); hideTimer.Start(); } };
            hooks.MouseDownAt += pt => { if (wheel.Visible && wheel.KeyboardMode && !wheel.Bounds.Contains(pt)) wheel.Dismiss(); };
            hooks.NavKey += k => wheel.Nav(k);

            hotkey.Pressed += OnHotkey;
            ApplyHotkey();

            if (Settings.FirstRun)
            {
                Settings.FirstRun = false;
                Settings.Save();
                ShowHelp();
            }
            else Notify("كلمنتينة · Klementina", S.T("welcome"), null);
        }

        void BuildMenu()
        {
            var menu = new ContextMenuStrip { RightToLeft = S.Rtl ? RightToLeft.Yes : RightToLeft.No };
            menu.Items.Add(S.T("tray_help"), null, (s, e) => ShowHelp());
            menu.Items.Add(S.T("tray_settings"), null, (s, e) => ShowSettings());
            pauseItem = new ToolStripMenuItem(S.T("tray_pause"), null, (s, e) => TogglePause()) { Checked = paused };
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(S.T("tray_exit"), null, (s, e) => ExitThread());
            var old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
            UpdateTray();
        }

        void TogglePause()
        {
            paused = !paused;
            pauseItem.Checked = paused;
            wheel.Dismiss();
            UpdateTray();
        }

        void UpdateTray()
        {
            string t = jobs > 0 ? S.T("working") : paused ? S.T("paused") : S.T("tray_tip");
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void ApplyHotkey()
        {
            hotkey.Unregister();
            if (Settings.HotkeyEnabled && !hotkey.Register(0x0001 | 0x0002 | 0x4000, (uint)Settings.HotkeyLetter[0])) // Ctrl+Alt+letter (no repeat)
                Notify("كلمنتينة · Klementina", S.T("hotkey_taken"), null, ToolTipIcon.Warning);
        }

        // ---------------- triggers ----------------

        void OnTapWhileDragging(string key, Point cursor, Point dragStart)
        {
            if (paused || !Settings.DragEnabled) return;
            var k = Settings.KindForKey(key);
            if (k == null) return;
            hideTimer.Stop();
            if (wheel.Visible && !wheel.KeyboardMode) { wheel.SwitchKind(k.Value); return; }
            // Pre-fill from the Explorer selection; the real file list arrives with the drag itself.
            var pre = ExplorerSel.AtPoint(dragStart);
            wheel.Present(k.Value, pre, cursor, false);
        }

        void OnHotkey()
        {
            if (paused) return;
            IntPtr fg = Native.GetAncestor(Native.GetForegroundWindow(), Native.GA_ROOT);
            var files = ExplorerSel.Of(fg);
            if (files.Count == 0) files = ExplorerSel.TopmostExplorer(); // focus was elsewhere: use the last Explorer window
            if (files.Count == 0) { Notify("كلمنتينة · Klementina", S.T("select_first"), null); return; }
            wheel.Present(WheelKind.Convert, files, Cursor.Position, true);
        }

        // ---------------- running operations ----------------

        void Run(OpItem item, List<string> files)
        {
            if (item.Need != null && !Engines.Has(item.Need))
            {
                Engines.Refresh(); // maybe it was installed meanwhile
                if (!Engines.Has(item.Need)) { ExplainMissing(item.Need); return; }
            }
            if (item.OnUi)
            {
                try { Report(item.Run(files)); }
                catch (Exception ex) { Fail(Ops.Clean(ex)); }
                return;
            }
            RunWorker(() => item.Run(files), item.Slow);
        }

        public void RunWorker(Func<OpResult> work, bool slow)
        {
            jobs++;
            UpdateTray();
            if (slow) Notify(S.T("working"), "", null);
            var th = new Thread(() =>
            {
                OpResult r = null;
                string err = null;
                try { r = work(); }
                catch (Exception ex) { err = Ops.Clean(ex); }
                ui.Post(_ =>
                {
                    jobs--;
                    UpdateTray();
                    if (err != null) Fail(err); else Report(r);
                }, null);
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }

        void Report(OpResult r)
        {
            if (r == null) return;
            if (r.ClipboardText != null) { try { Clipboard.SetText(r.ClipboardText); } catch { } }
            if (r.Quiet && r.Errors.Count == 0) return;
            if (r.Outputs.Count == 0 && r.Errors.Count > 0) { Fail(string.Join("\n", r.Errors.Take(3))); return; }

            string title = S.T("done") + " ✓";
            string body;
            if (r.Outputs.Count == 1) body = Path.GetFileName(r.Outputs[0].TrimEnd('\\'));
            else if (r.Outputs.Count > 1) body = S.F("created_n", r.Outputs.Count);
            else body = "";
            if (r.Message != null) body = body.Length > 0 && !r.Message.StartsWith(body) ? body + "\n" + r.Message : r.Message;
            if (r.Errors.Count > 0) body += "\n⚠ " + r.Errors[0];
            Notify(title, body, r.Outputs.FirstOrDefault());
            lastLink = r.Link;
        }

        void Fail(string message)
        {
            Notify(S.T("failed"), message, null, ToolTipIcon.Warning);
        }

        public void Notify(string title, string text, string output, ToolTipIcon icon = ToolTipIcon.None)
        {
            lastOutput = output;
            lastLink = null;
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
            tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(4000);
        }

        void ExplainMissing(string need)
        {
            using (var owner = Ui.Owner())
            {
                var opts = S.Rtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0;
                if (need == "ffmpeg")
                {
                    if (MessageBox.Show(owner, S.T("need_ffmpeg_long"), "FFmpeg", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, opts) == DialogResult.Yes)
                        try { Process.Start("https://ffmpeg.org/download.html#build-windows"); } catch { }
                }
                else MessageBox.Show(owner, S.T("need_office_long"), "Microsoft Office", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, opts);
            }
        }

        // ---------------- windows ----------------

        HelpForm help;
        SettingsForm settings;

        void ShowHelp()
        {
            if (help == null || help.IsDisposed) help = new HelpForm();
            help.Show();
            Ui.ForceForeground(help.Handle);
        }

        void ShowSettings()
        {
            if (settings != null && !settings.IsDisposed) { Ui.ForceForeground(settings.Handle); return; }
            settings = new SettingsForm();
            settings.Saved += () =>
            {
                Engines.Refresh();
                ApplyHotkey();
                BuildMenu();
                if (help != null && !help.IsDisposed) help.Close();
            };
            settings.Show();
            Ui.ForceForeground(settings.Handle);
        }

        protected override void ExitThreadCore()
        {
            hooks.Dispose();
            hotkey.Unregister();
            hotkey.DestroyHandle();
            tray.Visible = false;
            tray.Dispose();
            wheel.Dispose();
            base.ExitThreadCore();
        }
    }

    class HotkeyWindow : NativeWindow
    {
        public event Action Pressed;
        bool registered;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(uint mods, uint vk) { registered = Native.RegisterHotKey(Handle, 1, mods, vk); return registered; }

        public void Unregister()
        {
            if (registered) Native.UnregisterHotKey(Handle, 1);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed();
            base.WndProc(ref m);
        }
    }

    class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "كلمنتينة · Klementina";
            Font = new Font("Segoe UI", 10.5f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 520);
            Icon = Art.AppIcon(32);
            BackColor = Color.FromArgb(255, 250, 245);
            Ui.ApplyDirection(this);

            var logo = new PictureBox { Image = Art.Fruit(72), Size = new Size(72, 72), Location = new Point(24, 20), SizeMode = PictureBoxSizeMode.CenterImage };
            var title = new Label
            {
                Text = S.T("app_name"), Font = new Font("Segoe UI", 22f, FontStyle.Bold),
                ForeColor = Color.FromArgb(232, 96, 16), AutoSize = true, Location = new Point(108, 18),
            };
            var sub = new Label
            {
                Text = S.T("tagline"),
                ForeColor = Color.FromArgb(110, 80, 60), AutoSize = true, Location = new Point(112, 64),
            };
            string body = string.Format(S.T("help_body"), Settings.KeyLabel(Settings.KeyConvert), Settings.KeyLabel(Settings.KeyTools), Settings.KeyLabel(Settings.KeyActions));
            var text = new Label { Text = body, Location = new Point(24, 112), Size = new Size(572, 340), ForeColor = Color.FromArgb(50, 34, 24) };
            var ok = new Button
            {
                Text = S.T("help_close"), Size = new Size(120, 36), Location = new Point(476, 466),
                BackColor = Color.FromArgb(255, 122, 36), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { logo, title, sub, text, ok });
            AcceptButton = ok;
        }
    }

    /// Settings: a warm header with the fruit, then cards (general, keyboard, output, engines) and Save / Cancel.
    class SettingsForm : Form
    {
        public event Action Saved;

        const int Col = 362, Gap = 16, Margin0 = 20, Top0 = 108;
        internal static float TestScale;     // self-test: pretend a high-DPI screen

        static Label Text0(string t, int x, int y, int w = 0, int h = 0)
        {
            var l = new Label { Text = t, Location = new Point(x, y), ForeColor = Theme.Ink, BackColor = Theme.Card };
            if (w > 0) { l.AutoSize = false; l.Size = new Size(w, h > 0 ? h : 24); l.TextAlign = ContentAlignment.MiddleLeft; }
            else l.AutoSize = true;
            return l;
        }

        static ComboBox Combo(int x, int y, int w)
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(x, y), Width = w, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Theme.Ink };
        }

        /// A switch with its label; clicking the label flips the switch too.
        static ToggleSwitch Toggle(Control card, string text, bool value, int y, int height = 36)
        {
            var t = new ToggleSwitch { Location = new Point(18, y + (height - 26) / 2), Checked = value, AccessibleName = text };
            var l = Text0(text, 76, y, Col - 94, height);
            l.Cursor = Cursors.Hand;
            l.Click += (s, e) => { t.Focus(); t.Checked = !t.Checked; };
            card.Controls.Add(t);
            card.Controls.Add(l);
            return t;
        }

        public SettingsForm()
        {
            SuspendLayout();
            Text = S.T("set_title");
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;   // scaled by hand below (WinForms autoscale misplaces mirrored Arabic layouts)
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Margin0 * 2 + Col * 2 + Gap, 566);
            BackColor = Theme.Page;
            Icon = Art.AppIcon(32);
            Ui.ApplyDirection(this);

            var version = typeof(SettingsForm).Assembly.GetName().Version;
            var header = Theme.Header(S.T("set_title"), S.T("app_name") + " " + version.Major + "." + version.Minor + "." + version.Build + "  ·  " + S.T("made_by"));
            int x1 = Margin0, x2 = Margin0 + Col + Gap;

            // ---- general ----
            var general = new CardPanel(S.T("sec_general")) { Location = new Point(x1, Top0), Size = new Size(Col, 182) };
            general.Controls.Add(Text0(S.T("set_lang"), 18, 56));
            var lang = Combo(Col - 18 - 180, 52, 180);
            lang.Items.AddRange(S.Languages.Select(l => (object)l[1]).ToArray());
            lang.SelectedIndex = Math.Max(0, Array.FindIndex(S.Languages, l => l[0] == Settings.Lang));
            general.Controls.Add(lang);
            var drag = Toggle(general, S.T("set_drag"), Settings.DragEnabled, 90, 44);
            var startup = Toggle(general, S.T("set_startup"), Settings.StartWithWindows, 136);

            // ---- output ----
            var output = new CardPanel(S.T("sec_output")) { Location = new Point(x1, Top0 + 182 + 14), Size = new Size(Col, 178) };
            output.Controls.Add(Text0(S.T("set_quality"), 18, 56));
            var qValue = Text0(Settings.JpegQuality.ToString(), Col - 18 - 40, 56, 40);
            qValue.TextAlign = ContentAlignment.MiddleRight;
            qValue.Font = new Font("Segoe UI Semibold", 10f);
            qValue.ForeColor = Theme.Accent;
            var q = new TrackBar { Minimum = 30, Maximum = 100, Value = Math.Max(30, Math.Min(100, Settings.JpegQuality)), TickStyle = TickStyle.None, AutoSize = false, Height = 30, Location = new Point(140, 52), Width = Col - 140 - 64, BackColor = Theme.Card };
            q.ValueChanged += (s, e) => qValue.Text = q.Value.ToString();
            output.Controls.AddRange(new Control[] { q, qValue });
            output.Controls.Add(Text0(S.T("set_ffmpeg"), 18, 98));
            var ff = new TextBox { Text = Settings.FfmpegPath, Location = new Point(18, 126), Width = Col - 36 - 112, BorderStyle = BorderStyle.FixedSingle };
            var browse = Theme.MakeButton(S.T("set_browse"), false);
            browse.Size = new Size(104, 30);
            browse.Location = new Point(Col - 18 - 104, 124);
            browse.Click += (s, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe", FileName = "ffmpeg.exe" })
                    if (d.ShowDialog(this) == DialogResult.OK) ff.Text = d.FileName;
            };
            output.Controls.AddRange(new Control[] { ff, browse });

            // ---- keyboard ----
            var keyboard = new CardPanel(S.T("sec_keyboard")) { Location = new Point(x2, Top0), Size = new Size(Col, 222) };
            var hk = Toggle(keyboard, S.T("set_hotkey"), Settings.HotkeyEnabled, 48, 44);
            keyboard.Controls.Add(Text0(S.T("set_hotkey_letter"), 18, 104));
            var letter = Combo(Col - 18 - 130, 100, 130);
            letter.Items.AddRange(Settings.HotkeyLetters.Select(l => (object)("Ctrl+Alt+" + l)).ToArray());
            letter.SelectedIndex = Math.Max(0, Array.IndexOf(Settings.HotkeyLetters, Settings.HotkeyLetter));
            letter.Enabled = hk.Checked;
            hk.CheckedChanged += (s, e) => letter.Enabled = hk.Checked;
            keyboard.Controls.Add(letter);

            var keyNames = new[] { "shift", "ctrl", "alt" };
            int kw = (Col - 36 - 2 * 10) / 3;
            Func<int, string, string, ComboBox> keyCol = (i, name, current) =>
            {
                int kx = 18 + i * (kw + 10);
                var l = Text0(name, kx, 146, kw, 22);
                l.ForeColor = Theme.Muted;
                keyboard.Controls.Add(l);
                var c = Combo(kx, 172, kw);
                c.Items.AddRange(keyNames.Select(Settings.KeyLabel).Cast<object>().ToArray());
                c.SelectedIndex = Math.Max(0, Array.IndexOf(keyNames, current));
                keyboard.Controls.Add(c);
                return c;
            };
            var kc = keyCol(0, S.T("convert"), Settings.KeyConvert);
            var kt = keyCol(1, S.T("tools"), Settings.KeyTools);
            var ka = keyCol(2, S.T("actions"), Settings.KeyActions);

            // ---- engines ----
            var engines = new CardPanel(S.T("set_status")) { Location = new Point(x2, Top0 + 222 + 14), Size = new Size(Col, 138) };
            var chips = new FlowLayoutPanel { Location = new Point(18, 50), Size = new Size(Col - 36, 76), BackColor = Theme.Card, WrapContents = true };
            chips.Controls.Add(new StatusChip("FFmpeg", Engines.Ffmpeg != null));
            chips.Controls.Add(new StatusChip("Word", Engines.Word));
            chips.Controls.Add(new StatusChip("Excel", Engines.Excel));
            chips.Controls.Add(new StatusChip("PowerPoint", Engines.PowerPoint));
            chips.Controls.Add(new StatusChip("qpdf", Engines.Qpdf != null));
            chips.Controls.Add(new StatusChip("Edge", Engines.Edge != null));
            engines.Controls.Add(chips);

            // ---- footer ----
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 66, BackColor = Theme.FooterBack };
            var save = Theme.MakeButton(S.T("set_save"), true);
            var cancel = Theme.MakeButton(S.T("set_cancel"), false);
            save.Location = new Point(ClientSize.Width - Margin0 - save.Width * 2 - 10, 14);
            cancel.Location = new Point(ClientSize.Width - Margin0 - cancel.Width, 14);
            cancel.Click += (s, e) => Close();
            save.Click += (s, e) =>
            {
                var picks = new[] { kc.SelectedIndex, kt.SelectedIndex, ka.SelectedIndex };
                if (picks.Distinct().Count() != 3) { MessageBox.Show(this, S.T("set_dup_keys"), Text); return; }
                Settings.Lang = S.Languages[Math.Max(0, lang.SelectedIndex)][0];
                Settings.DragEnabled = drag.Checked;
                Settings.HotkeyEnabled = hk.Checked;
                Settings.HotkeyLetter = Settings.HotkeyLetters[Math.Max(0, letter.SelectedIndex)];
                Settings.KeyConvert = keyNames[kc.SelectedIndex];
                Settings.KeyTools = keyNames[kt.SelectedIndex];
                Settings.KeyActions = keyNames[ka.SelectedIndex];
                Settings.JpegQuality = q.Value;
                Settings.FfmpegPath = ff.Text.Trim();
                try { Settings.StartWithWindows = startup.Checked; } catch { }
                Settings.Save();
                if (Saved != null) Saved();
                Close();
            };
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);

            Controls.AddRange(new Control[] { general, output, keyboard, engines, footer, header });
            AcceptButton = save; CancelButton = cancel;
            ActiveControl = save;      // no blue selection sitting in the first box
            // the coordinates above are 96-DPI pixels: scale them to the screen once, before the window exists (fonts
            // are in points and already follow the DPI)
            float k = TestScale > 0 ? TestScale : Native.ScaleAt(Cursor.Position);
            if (k > 1.01f) Scale(new SizeF(k, k));
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
