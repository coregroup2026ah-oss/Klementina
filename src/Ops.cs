using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Klementina
{
    static class Engines
    {
        public static string Ffmpeg, Edge, Qpdf;
        public static bool Word, Excel, PowerPoint, Heic;

        public static void Refresh()
        {
            Ffmpeg = FindFfmpeg();
            string app = AppDomain.CurrentDomain.BaseDirectory;
            Qpdf = FirstExisting(Path.Combine(app, @"qpdf\qpdf.exe"), Path.GetFullPath(Path.Combine(app, @"..\vendor\qpdf\qpdf.exe")));
            Heic = ImageOps.CanWriteHeic();
            Word = Type.GetTypeFromProgID("Word.Application") != null;
            Excel = Type.GetTypeFromProgID("Excel.Application") != null;
            PowerPoint = Type.GetTypeFromProgID("PowerPoint.Application") != null;
            Edge = FirstExisting(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"));
        }

        public static bool Has(string need)
        {
            switch (need)
            {
                case "ffmpeg": return Ffmpeg != null;
                case "qpdf": return Qpdf != null;
                case "heic": return Heic;
                case "word": return Word;
                case "excel": return Excel;
                case "ppt": return PowerPoint;
            }
            return true;
        }

        static string FirstExisting(params string[] paths)
        {
            foreach (var p in paths) if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            return null;
        }

        static string FindFfmpeg()
        {
            string app = AppDomain.CurrentDomain.BaseDirectory;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new List<string> {
                Settings.FfmpegPath,
                Path.Combine(app, "ffmpeg.exe"),
                Path.Combine(app, @"ffmpeg\bin\ffmpeg.exe"),
                Path.Combine(app, @"ffmpeg\ffmpeg.exe"),
                Path.Combine(local, @"Microsoft\WinGet\Links\ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
            };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Trim().Length > 0) candidates.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe")); } catch { }
            }
            return FirstExisting(candidates.ToArray());
        }
    }

    static class Ops
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Runs fn on every file, collecting outputs and per-file errors.
        public static OpResult Each(List<string> files, Func<string, List<string>> fn)
        {
            var r = new OpResult();
            foreach (var f in files)
            {
                try { r.Outputs.AddRange(fn(f)); }
                catch (Exception ex) { r.Errors.Add(Path.GetFileName(f) + ": " + Clean(ex)); }
            }
            return r;
        }

        public static OpResult Each(List<string> files, Func<string, string> fn)
        {
            return Each(files, f => new List<string> { fn(f) });
        }

        /// Runs a compression step per file and keeps an output only if it is actually smaller than the original.
        public static OpResult Compress(List<string> files, Func<string, string> fn)
        {
            var r = new OpResult();
            var kept = new List<string>();
            foreach (var src in files)
            {
                try
                {
                    string dest = fn(src);
                    if (SizeOf(dest) >= SizeOf(src)) { TryDelete(dest); continue; } // no gain: keep nothing
                    r.Outputs.Add(dest);
                    kept.Add(src);
                }
                catch (Exception ex) { r.Errors.Add(Path.GetFileName(src) + ": " + Clean(ex)); }
            }
            if (r.Outputs.Count > 0) r.Message = Savings(kept, r.Outputs);
            else if (r.Errors.Count == 0) r.Message = S.T("already_small");
            return r;
        }

        public static string Clean(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message.Trim();
        }

        public static string Q(string p) { return "\"" + p + "\""; }

        /// Base name without extension, also stripping ".tar" from ".tar.gz".
        public static string BaseName(string path)
        {
            string n = Path.GetFileNameWithoutExtension(path.TrimEnd('\\', '/'));
            if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            if (Directory.Exists(path)) n = Path.GetFileName(path.TrimEnd('\\', '/'));
            return n;
        }

        public static string DirOf(string path)
        {
            return Path.GetDirectoryName(path.TrimEnd('\\', '/'));
        }

        /// A free path beside src: "name.ext", "name (2).ext", …
        public static string Out(string src, string ext, string suffix = null)
        {
            string b = BaseName(src) + (suffix != null ? "-" + Suffix(suffix) : "");
            return Unique(DirOf(src), b, ext);
        }

        static readonly Dictionary<string, string> arSuffix = new Dictionary<string, string>
        {
            { "compressed", "مضغوط" }, { "small", "مصغر" }, { "rotated", "مدور" }, { "flipped", "مقلوب" },
            { "gray", "رمادي" }, { "clean", "نظيف" }, { "muted", "بلا صوت" }, { "trim", "مقصوص" },
            { "frame", "لقطة" }, { "normalized", "موحد" }, { "mono", "أحادي" }, { "pages", "صفحات" },
            { "slides", "شرائح" }, { "parts", "أجزاء" }, { "collage", "كولاج" },
            { "bleeped", "مطنن" }, { "channels", "قنوات" }, { "cropped", "مقصوص الأبعاد" }, { "tagged", "معدل البيانات" },
            { "page", "صفحة" }, { "part", "جزء" }, { "Archive", "ملفات" },
            { "edited", "معدل" }, { "annotated", "مع رسم" }, { "redacted", "مخفي" }, { "bg", "بخلفية" }, { "organized", "مرتب" },
        };

        /// Names added to output files ("photo-compressed.jpg" / "photo-مضغوط.jpg") follow the app language:
        /// Arabic from the table above, other languages from "sfx.<key>" in their language file, else English.
        public static string Suffix(string key)
        {
            string ar;
            if (S.Ar) return arSuffix.TryGetValue(key, out ar) ? ar : key;
            if (Settings.Lang == "en") return key;
            string t = S.TryT("sfx." + key);
            if (t == null) return key;
            foreach (char c in Path.GetInvalidFileNameChars()) t = t.Replace(c, ' ');
            return t.Trim().Length > 0 ? t.Trim() : key;
        }

        public static string Unique(string dir, string baseName, string ext)
        {
            string dot = string.IsNullOrEmpty(ext) ? "" : "." + ext;
            string p = Path.Combine(dir, baseName + dot);
            for (int i = 2; File.Exists(p) || Directory.Exists(p); i++)
                p = Path.Combine(dir, baseName + " (" + i + ")" + dot);
            return p;
        }

        public static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "Klementina", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        public static int Run(string exe, string args, out string output, string workDir = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8,
            };
            if (workDir != null) psi.WorkingDirectory = workDir;
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler h = (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.OutputDataReceived += h;
                p.ErrorDataReceived += h;
                p.Start();
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();
                p.WaitForExit();
                p.WaitForExit(); // flush async readers
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        public static void Ffmpeg(string args, string expectedOutput)
        {
            if (Engines.Ffmpeg == null) throw new Exception(S.T("need_ffmpeg"));
            string log;
            int code = Run(Engines.Ffmpeg, "-hide_banner -loglevel error -nostdin -y " + args, out log);
            if (code != 0 || (expectedOutput != null && !File.Exists(expectedOutput)))
            {
                TryDelete(expectedOutput);
                var lines = log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                throw new Exception(lines.Length > 0 ? lines[lines.Length - 1] : "ffmpeg exit code " + code);
            }
        }

        public static string Size(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0", Inv) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("0.0", Inv) + " MB";
            return (bytes / 1073741824.0).ToString("0.00", Inv) + " GB";
        }

        public static long SizeOf(string path)
        {
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                if (Directory.Exists(path)) return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { }
            return 0;
        }

        /// "2.2 MB → 640 KB (−71%)"
        public static string Savings(List<string> inputs, List<string> outputs)
        {
            long a = inputs.Sum(f => SizeOf(f)), b = outputs.Sum(f => SizeOf(f));
            if (a <= 0) return null;
            int pct = (int)Math.Round((1 - (double)b / a) * 100);
            return Size(a) + " → " + Size(b) + (pct > 0 ? "  (−" + pct + "%)" : "");
        }
    }
}
