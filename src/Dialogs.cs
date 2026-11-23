using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Klementina
{
    /// Small themed dialog builder used by tools that need one or two choices before running.
    class KDialog : Form
    {
        protected readonly FlowLayoutPanel body;
        readonly FlowLayoutPanel buttons;
        public static readonly Color Accent = Color.FromArgb(255, 112, 32);

        public KDialog(string title)
        {
            Text = title;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16, 12, 16, 12);
            Icon = Art.AppIcon(32);
            Ui.ApplyDirection(this);

            var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0), Anchor = AnchorStyles.Right };
            root.Controls.Add(body);
            root.Controls.Add(buttons);
            Controls.Add(root);
            Shown += (s, e) => Ui.ForceForeground(Handle);
        }

        public Label AddText(string text, bool bold = false, int width = 380)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 4, 0, 4) };
            if (bold) l.Font = new Font(Font, FontStyle.Bold);
            body.Controls.Add(l);
            return l;
        }

        public TextBox AddInput(string label, string value, int width = 380)
        {
            AddText(label);
            var t = new TextBox { Text = value, Width = width, Margin = new Padding(0, 0, 0, 6) };
            body.Controls.Add(t);
            return t;
        }

        /// A row of toggle buttons; returns a getter for the selected index.
        public Func<int> AddChoices(string label, string[] options, int selected)
        {
            if (label != null) AddText(label);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(420, 0), Margin = new Padding(0, 0, 0, 6) };
            var list = new List<RadioButton>();
            for (int i = 0; i < options.Length; i++)
            {
                var rb = new RadioButton
                {
                    Text = options[i], Appearance = Appearance.Button, AutoSize = true, Checked = i == selected,
                    FlatStyle = FlatStyle.Flat, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 6, 6), TextAlign = ContentAlignment.MiddleCenter,
                };
                rb.FlatAppearance.CheckedBackColor = Color.FromArgb(255, 222, 196);
                rb.FlatAppearance.BorderColor = Color.FromArgb(230, 170, 120);
                list.Add(rb);
                row.Controls.Add(rb);
            }
            body.Controls.Add(row);
            return () => list.FindIndex(r => r.Checked);
        }

        public TrackBar AddSlider(string label, int min, int max, int value, Func<int, string> format)
        {
            var l = AddText(label + ": " + format(value));
            var tb = new TrackBar { Minimum = min, Maximum = max, Value = value, Width = 380, TickStyle = TickStyle.None, Margin = new Padding(0, 0, 0, 6) };
            tb.ValueChanged += (s, e) => l.Text = label + ": " + format(tb.Value);
            body.Controls.Add(tb);
            return tb;
        }

        public void AddControl(Control c) { body.Controls.Add(c); }

        public Button AddButton(string text, bool primary, Action onClick)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(96, 32), Margin = new Padding(6, 0, 0, 0), FlatStyle = FlatStyle.Flat };
            if (primary) { b.BackColor = Accent; b.ForeColor = Color.White; b.FlatAppearance.BorderSize = 0; AcceptButton = b; }
            b.Click += (s, e) => onClick();
            buttons.Controls.Add(b);
            return b;
        }

        /// Shows the dialog and returns true when the primary button closed it.
        public bool Ask(string okText)
        {
            bool ok = false;
            AddButton(S.T("cancel"), false, () => Close());
            AddButton(okText, true, () => { if (Check == null || Check()) { ok = true; Close(); } });
            ShowDialog();
            return ok;
        }

        public Func<bool> Check;
    }
}
