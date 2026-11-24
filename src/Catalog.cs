using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Klementina
{
    enum WheelKind { Convert = 0, Tools = 1, Actions = 2 }

    enum Cat { Image, Video, Audio, Pdf, Doc, Text, Slides, Sheet, Csv, Subtitle, Archive, Ebook, Folder, Other }

    class OpResult
    {
        public List<string> Outputs = new List<string>();
        public List<string> Errors = new List<string>();
        public string Message;
        public string ClipboardText;
        public string Link; // opened when the notification is clicked
        public bool Quiet;
    }

    class OpItem
    {
        public string Label, Glyph;
        public bool IsFormat, IsMore, OnUi, Slow;
        public string Need; // null, "ffmpeg", "word", "excel", "ppt"
        public Func<List<string>, OpResult> Run;
    }

    static class Catalog
    {
        static readonly Dictionary<string, Cat> extCat = new Dictionary<string, Cat>(StringComparer.OrdinalIgnoreCase);

        static Catalog()
        {
            Reg(Cat.Image, "jpg jpeg jfif png bmp gif tif tiff ico webp heic heif avif svg");
            Reg(Cat.Ebook, "epub");
            Reg(Cat.Video, "mp4 mov mkv webm avi wmv m4v flv mpg mpeg 3gp ts mts");
            Reg(Cat.Audio, "mp3 m4a wav flac ogg opus aiff aif wma aac");
            Reg(Cat.Pdf, "pdf");
            Reg(Cat.Doc, "docx doc rtf odt");
            Reg(Cat.Text, "txt md markdown html htm");
            Reg(Cat.Slides, "pptx ppt odp");
            Reg(Cat.Sheet, "xlsx xls ods");
            Reg(Cat.Csv, "csv tsv");
            Reg(Cat.Subtitle, "srt vtt");
            Reg(Cat.Archive, "zip tar gz tgz rar 7z");
        }

        static void Reg(Cat c, string exts) { foreach (var e in exts.Split(' ')) extCat[e] = c; }

        public static string Ext(string path)
        {
            string e = Path.GetExtension(path);
            return string.IsNullOrEmpty(e) ? "" : e.Substring(1).ToLowerInvariant();
        }

        public static Cat CatOf(string path)
        {
            if (Directory.Exists(path)) return Cat.Folder;
            Cat c;
            return extCat.TryGetValue(Ext(path), out c) ? c : Cat.Other;
        }

        static string Norm(string ext)
        {
            if (ext == "jpeg" || ext == "jfif") return "jpg";
            if (ext == "tif") return "tiff";
            if (ext == "htm") return "html";
            if (ext == "markdown") return "md";
            if (ext == "aif") return "aiff";
            if (ext == "tgz") return "gz";
            return ext;
        }

        public static List<OpItem> Build(WheelKind kind, List<string> files)
        {
            var list = new List<OpItem>();
            if (files == null || files.Count == 0) return list;
            var cats = files.Select(CatOf).Distinct().ToList();
            Cat c = cats.Count == 1 ? cats[0] : Cat.Other;
            var exts = files.Select(f => Norm(Ext(f))).Distinct().ToList();
            string same = exts.Count == 1 ? exts[0] : null;

            if (kind == WheelKind.Convert) AddConversions(list, c, same, files);
            else if (kind == WheelKind.Tools) AddTools(list, c, files);
            else AddActions(list, c, files);
            return list;
        }

        // ---------------- Convert ----------------

        static void Fmt(List<OpItem> list, string same, string label, string ext, string need, Func<string, string, List<string>> perFile, bool slow = false)
        {
            if (same != null && Norm(ext) == same) return;
            list.Add(new OpItem
            {
                Label = label, IsFormat = true, Need = need, Slow = slow,
                Run = files => Ops.Each(files, f => perFile(f, ext)),
            });
        }

        static void AddConversions(List<OpItem> l, Cat c, string same, List<string> files)
        {
            switch (c)
            {
                case Cat.Image:
                    foreach (var e in new[] { "png", "jpg", "webp", "avif", "heic", "bmp", "gif", "tiff", "ico" })
                    {
                        if (e == "heic" && !Engines.Heic) continue; // only where Windows can write HEIF
                        Fmt(l, same, e.ToUpperInvariant(), e, e == "webp" || e == "avif" ? "ffmpeg" : null, ImageOps.ConvertTo, e == "avif");
                    }
                    Fmt(l, same, "PDF", "pdf", null, ImageOps.ConvertTo);
                    break;
                case Cat.Video:
                    foreach (var e in new[] { "mp4", "mov", "mkv", "webm", "gif", "mp3", "m4a", "avi", "wmv" })
                        Fmt(l, same, e.ToUpperInvariant(), e, "ffmpeg", MediaOps.Convert, true);
                    break;
                case Cat.Audio:
                    foreach (var e in new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma" })
                        Fmt(l, same, e.ToUpperInvariant(), e, "ffmpeg", MediaOps.Convert, true);
                    break;
                case Cat.Pdf:
                    Fmt(l, same, "DOCX", "docx", null, PdfOps.ToText, true);                  // exact copy
                    Fmt(l, same, "PPTX", "pptx", null, PdfOps.ToPptx, true);                 // editable slides, no PowerPoint needed
                    Fmt(l, same, "XLSX", "xlsx", null, Formats.PdfTo, true);                 // no Excel needed
                    Fmt(l, same, "EPUB", "epub", null, Formats.PdfTo, true);
                    Fmt(l, same, "MD", "md", null, Formats.PdfTo, true);
                    Fmt(l, same, "TXT", "txt", null, PdfOps.ToText, true);
                    Fmt(l, same, "PNG", "png", null, PdfOps.ToImages, true);
                    Fmt(l, same, "JPG", "jpg", null, PdfOps.ToImages, true);
                    break;
                case Cat.Doc:
                    foreach (var e in new[] { "pdf", "docx", "odt", "rtf", "txt", "html" })
                        Fmt(l, same, e.ToUpperInvariant(), e, "word", OfficeOps.Word, true);
                    Fmt(l, same, "EPUB", "epub", "word", Formats.DocTo, true);
                    Fmt(l, same, "MD", "md", "word", Formats.DocTo, true);
                    break;
                case Cat.Text:
                    if (same != "html") Fmt(l, same, "HTML", "html", null, TextOps.ToHtml);
                    Fmt(l, same, "PDF", "pdf", null, TextOps.ToPdf, true);
                    Fmt(l, same, "DOCX", "docx", "word", TextOps.ViaWord, true);
                    if (same == "html") Fmt(l, same, "TXT", "txt", "word", TextOps.ViaWord, true);
                    if (same != "md") Fmt(l, same, "MD", "md", null, Formats.DocTo);
                    Fmt(l, same, "EPUB", "epub", null, Formats.DocTo);
                    break;
                case Cat.Slides:
                    foreach (var e in new[] { "pdf", "png", "jpg", "pptx" })
                        Fmt(l, same, e.ToUpperInvariant(), e, "ppt", OfficeOps.PowerPoint, true);
                    break;
                case Cat.Sheet:
                    foreach (var e in new[] { "xlsx", "csv", "tsv", "pdf", "ods", "html" })
                        Fmt(l, same, e.ToUpperInvariant(), e, "excel", OfficeOps.Excel, true);
                    break;
                case Cat.Csv:
                    if (same != "tsv") Fmt(l, same, "TSV", "tsv", null, TextOps.CsvTsv);
                    if (same != "csv") Fmt(l, same, "CSV", "csv", null, TextOps.CsvTsv);
                    Fmt(l, same, "XLSX", "xlsx", null, TextOps.CsvToXlsx);                    // no Excel needed
                    Fmt(l, same, "PDF", "pdf", null, TextOps.CsvToPdf, true);
                    Fmt(l, same, "ODS", "ods", "excel", OfficeOps.Excel, true);
                    Fmt(l, same, "HTML", "html", null, TextOps.CsvToHtml);
                    break;
                case Cat.Ebook:
                    Fmt(l, same, "PDF", "pdf", null, Formats.EpubTo, true);
                    Fmt(l, same, "DOCX", "docx", "word", Formats.EpubTo, true);
                    Fmt(l, same, "MD", "md", null, Formats.EpubTo, true);
                    Fmt(l, same, "TXT", "txt", null, Formats.EpubTo, true);
                    Fmt(l, same, "HTML", "html", null, Formats.EpubTo, true);
                    break;
                case Cat.Subtitle:
                    foreach (var e in new[] { "srt", "vtt", "txt" })
                        Fmt(l, same, e.ToUpperInvariant(), e, null, TextOps.Subtitles);
                    break;
                case Cat.Archive:
                    Fmt(l, same, "ZIP", "zip", null, ArchiveOps.Repack, true);
                    Fmt(l, same, "TAR", "tar", null, ArchiveOps.Repack, true);
                    Fmt(l, same, "TAR.GZ", "gz", null, ArchiveOps.Repack, true);
                    break;
                case Cat.Folder:
                    Fmt(l, null, "ZIP", "zip", null, ArchiveOps.PackFolder, true);
                    Fmt(l, null, "TAR.GZ", "gz", null, ArchiveOps.PackFolder, true);
                    break;
                default:
                    l.Add(new OpItem { Label = "ZIP", IsFormat = true, Slow = true, Run = ArchiveOps.ZipAll });
                    break;
            }
        }

        // ---------------- Tools ----------------

        static OpItem Tool(string key, string glyph, Func<List<string>, OpResult> run, string need = null, bool slow = false, bool ui = false)
        {
            return new OpItem { Label = S.T(key), Glyph = glyph, Run = run, Need = need, Slow = slow, OnUi = ui };
        }

        static OpItem PerFile(string key, string glyph, Func<string, string> fn, string need = null, bool slow = false)
        {
            return Tool(key, glyph, files => Ops.Each(files, f => new List<string> { fn(f) }), need, slow);
        }

        static void AddTools(List<OpItem> l, Cat c, List<string> files)
        {
            bool single = files.Count == 1;
            bool many = files.Count > 1;
            switch (c)
            {
                case Cat.Pdf:
                    l.Add(Tool("compress", Glyphs.Compress, Ui.Compress, "qpdf", ui: true));
                    if (many) l.Add(Tool("merge", Glyphs.Merge, PdfTools.Merge, "qpdf", true));
                    if (single) l.Add(Tool("split", Glyphs.Split, Ui.SplitPdf, "qpdf", ui: true));
                    else l.Add(PerFile("split", Glyphs.Split, PdfTools.Split, "qpdf", true));
                    if (single) l.Add(Tool("reorganize", Glyphs.Collage, f => Ui.Open(new PdfOrganizer(f[0])), "qpdf", ui: true));
                    if (single) l.Add(Tool("extract_pages", Glyphs.Pages, Ui.ExtractPages, "qpdf", ui: true));
                    l.Add(PerFile("rotate", Glyphs.Rotate, PdfTools.Rotate, "qpdf", true));
                    l.Add(PerFile("strip", Glyphs.Strip, PdfTools.StripInfo, "qpdf", true));
                    break;
                case Cat.Image:
                    l.Add(Tool("compress", Glyphs.Compress, Ui.Compress, ui: true));
                    if (single) l.Add(Tool("edit_photo", Glyphs.Tune, f => Ui.Open(new PhotoEditor(f[0])), ui: true));
                    if (single) l.Add(Tool("crop", Glyphs.Crop, f => Ui.Open(new CropEditor(f[0])), ui: true));
                    if (single) l.Add(Tool("annotate", Glyphs.Edit, f => Ui.Open(new MarkupEditor(f[0], false)), ui: true));
                    if (single) l.Add(Tool("redact", Glyphs.Redact, f => Ui.Open(new MarkupEditor(f[0], true)), ui: true));
                    if (single) l.Add(Tool("add_bg", Glyphs.Picture, f => Ui.Open(new BackgroundEditor(f[0])), ui: true));
                    if (many) l.Add(Tool("collage", Glyphs.Collage, ImageOps.Collage));
                    l.Add(Tool("combine_pdf", Glyphs.Pdf, ImageOps.CombinePdf));
                    l.Add(Tool("read_qr", Glyphs.Qr, ImageOps.ReadCodes));
                    l.Add(PerFile("resize", Glyphs.Resize, ImageOps.Half));
                    l.Add(PerFile("rotate", Glyphs.Rotate, ImageOps.Rotate));
                    l.Add(PerFile("flip", Glyphs.Flip, ImageOps.Flip));
                    l.Add(PerFile("gray", Glyphs.Gray, ImageOps.Grayscale));
                    if (single) l.Add(Tool("metadata", Glyphs.Info, f => Ui.Open(new MetadataWindow(f[0])), ui: true));
                    l.Add(PerFile("strip", Glyphs.Strip, ImageOps.Strip));
                    if (single) l.Add(Tool("copy_image", Glyphs.Copy, ImageOps.CopyToClipboard, ui: true));
                    break;
                case Cat.Video:
                    l.Add(Tool("compress", Glyphs.Compress, Ui.Compress, "ffmpeg", ui: true));
                    if (single) l.Add(Tool("trim", Glyphs.Trim, f => Ui.Open(new MediaToolWindow(f[0], MediaToolWindow.Mode.Trim)), "ffmpeg", ui: true));
                    l.Add(Tool("speed", Glyphs.Speed, Ui.Speed, "ffmpeg", ui: true));
                    if (many) l.Add(Tool("join", Glyphs.Merge, MediaOps.Join, "ffmpeg", true));
                    if (single) l.Add(Tool("split", Glyphs.Split, Ui.SplitMedia, "ffmpeg", ui: true));
                    l.Add(PerFile("frame", Glyphs.Picture, MediaOps.Frame, "ffmpeg", true));
                    l.Add(PerFile("p720", Glyphs.Resize, MediaOps.To720, "ffmpeg", true));
                    l.Add(PerFile("mute", Glyphs.Mute, MediaOps.Mute, "ffmpeg", true));
                    l.Add(PerFile("rotate", Glyphs.Rotate, MediaOps.RotateVideo, "ffmpeg", true));
                    if (single) l.Add(Tool("crop_video", Glyphs.Crop, VideoCropEditor.Open, "ffmpeg", ui: true));
                    if (single) l.Add(Tool("bleep", Glyphs.Bleep, f => Ui.Open(new MediaToolWindow(f[0], MediaToolWindow.Mode.Bleep)), "ffmpeg", ui: true));
                    if (single) l.Add(Tool("channels", Glyphs.Mono, f => Ui.Open(new MediaToolWindow(f[0], MediaToolWindow.Mode.Channels)), "ffmpeg", ui: true));
                    l.Add(PerFile("strip", Glyphs.Strip, MediaOps.StripMetadata, "ffmpeg", true));
                    break;
                case Cat.Audio:
                    l.Add(Tool("compress", Glyphs.Compress, Ui.Compress, "ffmpeg", ui: true));
                    if (single) l.Add(Tool("trim", Glyphs.Trim, f => Ui.Open(new MediaToolWindow(f[0], MediaToolWindow.Mode.Trim)), "ffmpeg", ui: true));
                    l.Add(PerFile("normalize", Glyphs.Volume, MediaOps.Normalize, "ffmpeg", true));
                    if (single) l.Add(Tool("bleep", Glyphs.Bleep, f => Ui.Open(new MediaToolWindow(f[0], MediaToolWindow.Mode.Bleep)), "ffmpeg", ui: true));
                    if (single) l.Add(Tool("channels", Glyphs.Mono, f => Ui.Open(new MediaToolWindow(f[0], MediaToolWindow.Mode.Channels)), "ffmpeg", ui: true));
                    else l.Add(PerFile("mono", Glyphs.Mono, MediaOps.Mono, "ffmpeg", true));
                    l.Add(Tool("speed", Glyphs.Speed, Ui.Speed, "ffmpeg", ui: true));
                    if (many) l.Add(Tool("join", Glyphs.Merge, MediaOps.Join, "ffmpeg", true));
                    if (single) l.Add(Tool("split", Glyphs.Split, Ui.SplitMedia, "ffmpeg", ui: true));
                    l.Add(PerFile("strip", Glyphs.Strip, MediaOps.StripMetadata, "ffmpeg", true));
                    break;
                case Cat.Archive:
                    l.Add(Tool("extract", Glyphs.Extract, files2 => Ops.Each(files2, f => new List<string> { ArchiveOps.Extract(f) }), null, true));
                    break;
            }
            if (single && (c == Cat.Video || c == Cat.Audio || c == Cat.Pdf))
                l.Add(Tool("metadata", Glyphs.Info, f => Ui.Open(new MetadataWindow(f[0])), ui: true));
            if (c != Cat.Folder || files.Count > 0) l.Add(Tool("zip", Glyphs.Zip, ArchiveOps.ZipAll, null, true));
            if (files.All(File.Exists)) l.Add(Tool("hash", Glyphs.Hash, MiscOps.Hash, null, true));
            if (single && c != Cat.Image) l.Add(Tool("props", Glyphs.Info, Ui.Properties, ui: true));
        }

        // ---------------- Actions ----------------

        static void AddActions(List<OpItem> l, Cat c, List<string> files)
        {
            bool single = files.Count == 1;
            l.Add(Tool("copy", Glyphs.Copy, Ui.CopyFiles, ui: true));
            l.Add(Tool("copy_path", Glyphs.Link, Ui.CopyPath, ui: true));
            l.Add(Tool("move_to", Glyphs.MoveTo, Ui.MoveTo, ui: true));
            l.Add(Tool("copy_to", Glyphs.CopyTo, Ui.CopyTo, ui: true));
            l.Add(Tool("terminal", Glyphs.Terminal, Ui.Terminal, ui: true));
            l.Add(Tool("float", Glyphs.Pin, Ui.Float, ui: true));
            if (files.All(File.Exists)) l.Add(Tool("share", Glyphs.Share, ShareOps.Share, ui: true));
            l.Add(Tool("reveal", Glyphs.Folder, Ui.Reveal, ui: true));
            if (single && c != Cat.Folder) l.Add(Tool("open_with", Glyphs.OpenWith, Ui.OpenWith, ui: true));
        }
    }

    /// Segoe Fluent Icons / Segoe MDL2 Assets code points.
    static class Glyphs
    {
        public const string
            Compress = "", Resize = "", Rotate = "", Flip = "", Gray = "",
            Strip = "", Pdf = "", Copy = "", Edit = "", Mute = "", Trim = "",
            Picture = "", Volume = "", Mono = "", Extract = "", Zip = "",
            Hash = "", Info = "", Link = "", MoveTo = "", CopyTo = "",
            Terminal = "", Pin = "", Folder = "", OpenWith = "",
            Target = "", Merge = "", Split = "", Pages = "", Speed = "", Collage = "",
            Qr = "", Share = "", Tune = "", Crop = "", Redact = "", Bleep = "";
    }
}
