using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Klementina
{
    /// The app's look in one place: the warm tangerine palette, buttons, cards, switches and status chips used by
    /// the settings window and the tool windows.
    static class Theme
    {
        public static readonly Color Warm1 = Color.FromArgb(255, 200, 92), Warm2 = Color.FromArgb(255, 140, 60);
        public static readonly Color Page = Color.FromArgb(255, 246, 238), Card = Color.White, Line = Color.FromArgb(240, 222, 206);
        public static readonly Color Ink = Color.FromArgb(58, 36, 22), Muted = Color.FromArgb(128, 98, 78), Accent = Color.FromArgb(240, 96, 20);
        public static readonly Color FooterBack = Color.FromArgb(255, 238, 224);

        public static readonly Font Body = new Font("Segoe UI", 10f);
        public static readonly Font Strong = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font ButtonFont = new Font("Segoe UI Semibold", 10f);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font HeaderTitle = new Font("Segoe UI", 17f, FontStyle.Bold);
        public static readonly Font ChipFont = new Font("Segoe UI", 9.5f);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// Primary (filled tangerine) or secondary (white, thin border) button.
        public static Button MakeButton(string text, bool primary)
        {
            var b = new Button { Text = text, Width = 132, Height = 38, FlatStyle = FlatStyle.Flat, Font = ButtonFont, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(230, 190, 160);
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 116, 40) : Color.FromArgb(255, 244, 234);
            b.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(214, 82, 12) : Color.FromArgb(255, 232, 214);
            b.BackColor = primary ? Accent : Color.White;
            b.ForeColor = primary ? Color.White : Ink;
            return b;
        }

        /// The warm gradient band at the top of a window, with the fruit, a title and a line under it.
        public static Panel Header(string title, string subtitle, int height = 92)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = height };
            header.Paint += (s, e) =>
            {
                var r = header.ClientRectangle;
                if (r.Width < 1 || r.Height < 1) return;
                using (var b = new LinearGradientBrush(r, Warm1, Warm2, 0f)) e.Graphics.FillRectangle(b, r);
            };
            var logo = new PictureBox { Image = Art.Fruit(56), Size = new Size(56, 56), Location = new Point(22, (height - 56) / 2), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.Transparent };
            var t = new Label { Text = title, AutoSize = true, Location = new Point(90, subtitle == null ? (height - 32) / 2 : 18), Font = HeaderTitle, ForeColor = Ink, BackColor = Color.Transparent };
            header.Controls.Add(logo);
            header.Controls.Add(t);
            if (subtitle != null)
                header.Controls.Add(new Label { Text = subtitle, AutoSize = true, Location = new Point(93, 54), ForeColor = Color.FromArgb(96, 58, 30), BackColor = Color.Transparent });
            return header;
        }
    }

    /// A white rounded card with a title, holding a group of settings.
    class CardPanel : Panel
    {
        public CardPanel(string title)
        {
            DoubleBuffered = true;
            BackColor = Theme.Page;
            Padding = new Padding(18, 14, 18, 14);
            if (title != null)
                Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(18, 14), Font = Theme.CardTitle, ForeColor = Theme.Accent, BackColor = Theme.Card });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 12))
            using (var fill = new SolidBrush(Theme.Card))
            using (var line = new Pen(Theme.Line))
            {
                g.FillPath(fill, path);
                g.DrawPath(line, path);
            }
        }
    }

    /// An on/off switch. Space or a click flips it; the label next to it can be wired to flip it too.
    class ToggleSwitch : Control
    {
        bool on;
        float knob;                       // 0 = off … 1 = on, animated
        readonly Timer anim = new Timer { Interval = 15 };
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(46, 26);
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Theme.Card;
            anim.Tick += (s, e) =>
            {
                float target = on ? 1 : 0;
                knob += Math.Sign(target - knob) * 0.2f;
                if (Math.Abs(knob - target) < 0.2f) { knob = target; anim.Stop(); }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                if (IsHandleCreated && Visible) anim.Start(); else knob = on ? 1 : 0;
                Invalidate();
                if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Focus(); Checked = !Checked; }

        protected override AccessibleObject CreateAccessibilityInstance() { return new SwitchAccessible(this); }

        class SwitchAccessible : ControlAccessibleObject
        {
            readonly ToggleSwitch owner;
            public SwitchAccessible(ToggleSwitch o) : base(o) { owner = o; }
            public override AccessibleRole Role { get { return AccessibleRole.CheckButton; } }
            public override AccessibleStates State { get { return base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None); } }
            public override string DefaultAction { get { return owner.Checked ? "Uncheck" : "Check"; } }
            public override void DoDefaultAction() { owner.Checked = !owner.Checked; }
        }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            Color offC = Color.FromArgb(214, 198, 186), onC = Theme.Accent;
            Color track = Color.FromArgb(
                (int)(offC.R + (onC.R - offC.R) * knob), (int)(offC.G + (onC.G - offC.G) * knob), (int)(offC.B + (onC.B - offC.B) * knob));
            if (!Enabled) track = Color.FromArgb(232, 224, 218);
            using (var path = Theme.Round(r, r.Height / 2))
            using (var b = new SolidBrush(track)) g.FillPath(b, path);
            if (Focused)
                using (var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
                using (var p = new Pen(Color.FromArgb(120, Theme.Accent), 1.5f)) g.DrawPath(p, path);
            float d = r.Height - 6, x = r.X + 3 + (r.Width - d - 6) * knob;
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillEllipse(shadow, x, r.Y + 4, d, d);
            g.FillEllipse(Brushes.White, x, r.Y + 3, d, d);
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }
    }

    /// A small rounded status pill: a coloured dot and a name (FFmpeg ✓, Word ✗ …).
    class StatusChip : Control
    {
        readonly bool ok;

        public StatusChip(string text, bool ok)
        {
            this.ok = ok;
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Font = Theme.ChipFont;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = text + ": " + S.T(ok ? "found" : "missing");
            var sz = TextRenderer.MeasureText(text, Font);
            Size = new Size(sz.Width + 34, 28);
            Margin = new Padding(0, 0, 8, 8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color back = ok ? Color.FromArgb(232, 247, 236) : Color.FromArgb(246, 238, 232);
            Color dot = ok ? Color.FromArgb(46, 160, 90) : Color.FromArgb(190, 160, 140);
            using (var path = Theme.Round(r, r.Height / 2))
            using (var b = new SolidBrush(back)) g.FillPath(b, path);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, 11, Height / 2f - 4, 8, 8);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 28, Height), ok ? Color.FromArgb(30, 100, 60) : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }
}
