using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Klementina
{
    /// Shows a file's properties (everything Windows knows about it), lets you edit audio/video tags,
    /// and removes all hidden data with one button.
    class MetadataWindow : Form
    {
        readonly string path;
        readonly Cat cat;
        readonly Dictionary<string, TextBox> tagBoxes = new Dictionary<string, TextBox>();
        static readonly string[] MediaTags = { "title", "artist", "album", "date", "genre", "comment" };

        public MetadataWindow(string file)
        {
            path = file;
            cat = Catalog.CatOf(file);
            Text = S.T("metadata") + " — " + Path.GetFileName(file);
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(720, 640);
            MinimumSize = new Size(520, 420);
            Icon = Art.AppIcon(32);
            BackColor = Color.FromArgb(255, 246, 238);
            Ui.ApplyDirection(this);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            var list = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None };
            list.Columns.Add(S.T("field"), 210);
            list.Columns.Add(S.T("value"), 440);
            foreach (var kv in ReadShellProperties(file)) list.Items.Add(new ListViewItem(new[] { kv.Key, kv.Value }));
            var copyItem = new ContextMenuStrip();
            copyItem.Items.Add(S.T("copy"), null, (s, e) => { if (list.SelectedItems.Count > 0) Clipboard.SetText(list.SelectedItems[0].SubItems[1].Text); });
            list.ContextMenuStrip = copyItem;

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.FromArgb(255, 238, 224) };
            if (cat == Cat.Audio || cat == Cat.Video)
            {
                var current = Engines.Ffmpeg != null ? ReadMediaTags(file) : new Dictionary<string, string>();
                var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = 660 };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
                foreach (var tag in MediaTags)
                {
                    string v;
                    current.TryGetValue(tag, out v);
                    var tb = new TextBox { Text = v ?? "", Width = 500 };
                    grid.Controls.Add(new Label { Text = S.T("tag_" + tag), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, TextAlign = ContentAlignment.MiddleLeft });
                    grid.Controls.Add(tb);
                    tagBoxes[tag] = tb;
                }
                bottom.Controls.Add(new Label { Text = S.T("edit_tags"), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
                bottom.Controls.Add(grid);
            }
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            if (tagBoxes.Count > 0) buttons.Controls.Add(Btn(S.T("save_tags"), true, SaveTags));
            buttons.Controls.Add(Btn(S.T("remove_all_meta"), tagBoxes.Count == 0, RemoveAll));
            buttons.Controls.Add(Btn(S.T("close"), false, Close));
            bottom.Controls.Add(buttons);

            Controls.Add(list);
            Controls.Add(bottom);
            Shown += (s, e) => Ui.ForceForeground(Handle);
        }

        Button Btn(string text, bool primary, Action click)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(120, 34), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 8, 0) };
            b.BackColor = primary ? Color.FromArgb(240, 96, 20) : Color.White;
            b.ForeColor = primary ? Color.White : Color.FromArgb(58, 36, 22);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.Click += (s, e) => click();
            return b;
        }

        /// Every non-empty property Windows' shell reports for the file (EXIF, camera, GPS, author, duration, bitrate…).
        public static List<KeyValuePair<string, string>> ReadShellProperties(string file)
        {
            var res = new List<KeyValuePair<string, string>>();
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                dynamic folder = shell.NameSpace(Path.GetDirectoryName(file));
                dynamic item = folder.ParseName(Path.GetFileName(file));
                var seen = new HashSet<string>();
                for (int i = 0; i < 400; i++)
                {
                    string name = folder.GetDetailsOf(null, i);
                    if (string.IsNullOrEmpty(name)) continue;
                    string value = folder.GetDetailsOf(item, i);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    // strip invisible direction marks Windows adds around numbers
                    value = value.Replace("‎", "").Replace("‏", "").Replace("‪", "").Replace("‬", "").Trim();
                    if (seen.Add(name)) res.Add(new KeyValuePair<string, string>(name, value));
                }
            }
            catch { }
            return res;
        }

        static Dictionary<string, string> ReadMediaTags(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string log = MediaOps.Probe(file).Raw;
            foreach (Match m in Regex.Matches(log, @"^\s{4}(\w+)\s*:\s(.*)$", RegexOptions.Multiline))
                if (!d.ContainsKey(m.Groups[1].Value)) d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            return d;
        }

        void SaveTags()
        {
            var tags = tagBoxes.ToDictionary(k => k.Key, v => v.Value.Text.Trim());
            string src = path;
            Program.RunWorker(() =>
            {
                string ext = Catalog.Ext(src);
                string dest = Ops.Out(src, ext, "tagged");
                var args = new System.Text.StringBuilder("-i " + Ops.Q(src) + " -map 0 -c copy");
                foreach (var t in tags) args.Append(" -metadata ").Append(t.Key).Append('=').Append(Ops.Q(t.Value.Replace("\"", "'")));
                Ops.Ffmpeg(args + " " + Ops.Q(dest), dest);
                var r = new OpResult();
                r.Outputs.Add(dest);
                return r;
            }, true);
            Close();
        }

        void RemoveAll()
        {
            string src = path;
            Cat c = cat;
            Program.RunWorker(() =>
            {
                string dest;
                if (c == Cat.Image) dest = ImageOps.Strip(src);
                else if (c == Cat.Pdf) dest = PdfTools.StripInfo(src);
                else if (c == Cat.Audio || c == Cat.Video) dest = MediaOps.StripMetadata(src);
                else throw new NotSupportedException(Path.GetFileName(src));
                var r = new OpResult();
                r.Outputs.Add(dest);
                return r;
            }, true);
            Close();
        }
    }
}
