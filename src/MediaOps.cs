using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;

namespace Klementina
{
    /// Audio/video work, all through FFmpeg.
    static class MediaOps
    {
        const string Even = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

        static string Codecs(string ext)
        {
            switch (ext)
            {
                case "mp4":
                case "mov":
                case "m4v": return "-vf " + Even + " -c:v libx264 -preset fast -crf 21 -pix_fmt yuv420p -c:a aac -b:a 160k -movflags +faststart";
                case "mkv": return "-vf " + Even + " -c:v libx264 -preset fast -crf 21 -pix_fmt yuv420p -c:a aac -b:a 192k";
                case "webm": return "-c:v libvpx-vp9 -crf 32 -b:v 0 -row-mt 1 -c:a libopus -b:a 128k";
                case "avi": return "-c:v mpeg4 -q:v 3 -c:a libmp3lame -q:a 3";
                case "wmv": return "-c:v wmv2 -q:v 3 -c:a wmav2 -b:a 192k";
                case "mp3": return "-vn -c:a libmp3lame -q:a 2";
                case "m4a": return "-vn -c:a aac -b:a 192k";
                case "aac": return "-vn -c:a aac -b:a 192k";
                case "wav": return "-vn -c:a pcm_s16le";
                case "flac": return "-vn -c:a flac";
                case "ogg": return "-vn -c:a libvorbis -q:a 5";
                case "opus": return "-vn -c:a libopus -b:a 128k";
                case "aiff":
                case "aif": return "-vn -c:a pcm_s16be";
                case "wma": return "-vn -c:a wmav2 -b:a 192k";
            }
            return ""; // let FFmpeg pick defaults for the container
        }

        /// Audio encoder settings that suit the given container (video stream is handled separately).
        public static string AudioCodecFor(string ext)
        {
            switch (ext)
            {
                case "mp4": case "mov": case "m4v": case "m4a": case "mkv": case "aac": return "-c:a aac -b:a 192k";
                case "webm": case "opus": return "-c:a libopus -b:a 128k";
                case "ogg": return "-c:a libvorbis -q:a 5";
                case "mp3": case "avi": return "-c:a libmp3lame -q:a 2";
                case "wav": return "-c:a pcm_s16le";
                case "flac": return "-c:a flac";
                case "aiff": case "aif": return "-c:a pcm_s16be";
                case "wma": case "wmv": return "-c:a wmav2 -b:a 192k";
            }
            return "";
        }

        /// Crops the video frame to the given rectangle (in source pixels; rounded to even numbers).
        public static string CropVideo(string src, Rectangle r)
        {
            string ext = VideoExt(src);
            string dest = Ops.Out(src, ext, "cropped");
            int w = r.Width - r.Width % 2, h = r.Height - r.Height % 2;
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -vf crop=" + w + ":" + h + ":" + r.X + ":" + r.Y + " -c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p -c:a copy " + Ops.Q(dest), dest);
            return dest;
        }

        public static List<string> Convert(string src, string ext)
        {
            string dest = Ops.Out(src, ext);
            if (ext == "gif")
                Ops.Ffmpeg("-i " + Ops.Q(src) + " -vf \"fps=12,scale='min(720,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse\" -loop 0 " + Ops.Q(dest), dest);
            else
                Ops.Ffmpeg("-i " + Ops.Q(src) + " " + Codecs(ext) + " " + Ops.Q(dest), dest);
            return new List<string> { dest };
        }

        static string VideoExt(string src)
        {
            string e = Catalog.Ext(src);
            return e == "mov" || e == "mkv" ? e : "mp4";
        }

        public static OpResult CompressVideo(List<string> files)
        {
            return Ops.Compress(files, src =>
            {
                string dest = Ops.Out(src, "mp4", "compressed");
                Ops.Ffmpeg("-i " + Ops.Q(src) + " -vf \"scale='min(1920,iw)':-2," + Even + "\" -c:v libx264 -preset slow -crf 28 -pix_fmt yuv420p -c:a aac -b:a 128k -movflags +faststart " + Ops.Q(dest), dest);
                return dest;
            });
        }

        // level: 0 light, 1 balanced, 2 strong, 3 maximum
        public static string CompressVideoLevel(string src, int level)
        {
            int[] crf = { 23, 27, 31, 35 }, maxW = { 3840, 1920, 1280, 854 }, aac = { 160, 128, 96, 64 };
            string dest = Ops.Out(src, "mp4", "compressed");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -vf \"scale='min(" + maxW[level] + ",iw)':-2," + Even + "\" -c:v libx264 -preset slow -crf " + crf[level] +
                       " -pix_fmt yuv420p -c:a aac -b:a " + aac[level] + "k -movflags +faststart " + Ops.Q(dest), dest);
            return dest;
        }

        public static string CompressAudioLevel(string src, int level)
        {
            int[] kbps = { 192, 128, 96, 64 };
            string dest = Ops.Out(src, "mp3", "compressed");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -vn -c:a libmp3lame -b:a " + kbps[level] + "k " + Ops.Q(dest), dest);
            return dest;
        }

        public static string To720(string src)
        {
            string ext = VideoExt(src);
            string dest = Ops.Out(src, ext, "720p");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -vf \"scale=-2:'min(720,ih)'\" -c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 160k " + Ops.Q(dest), dest);
            return dest;
        }

        public static string Mute(string src)
        {
            string dest = Ops.Out(src, Catalog.Ext(src), "muted");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -map 0:v -c copy -an " + Ops.Q(dest), dest);
            return dest;
        }

        public static string RotateVideo(string src)
        {
            string ext = VideoExt(src);
            string dest = Ops.Out(src, ext, "rotated");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -vf transpose=1 -c:v libx264 -preset fast -crf 20 -pix_fmt yuv420p -c:a copy " + Ops.Q(dest), dest);
            return dest;
        }

        public static string Frame(string src)
        {
            string dest = Ops.Out(src, "jpg", "frame");
            try { Ops.Ffmpeg("-ss 1 -i " + Ops.Q(src) + " -frames:v 1 -q:v 2 " + Ops.Q(dest), dest); }
            catch { Ops.Ffmpeg("-i " + Ops.Q(src) + " -frames:v 1 -q:v 2 " + Ops.Q(dest), dest); }
            return dest;
        }

        public static string Trim(string src, string start, string end)
        {
            string ext = Catalog.Ext(src);
            string dest = Ops.Out(src, ext, "trim");
            // seek on the input (fast: no decoding the part before the cut; still frame-exact because the clip is
            // re-encoded), then keep the selection's length
            double s0 = Seconds(start), e0 = string.IsNullOrEmpty(end) ? -1 : Seconds(end);
            string range = s0 >= 0 && (e0 < 0 || e0 > s0)
                ? "-ss " + s0.ToString("0.###", Ops.Inv) + " -i " + Ops.Q(src) + (e0 > 0 ? " -t " + (e0 - s0).ToString("0.###", Ops.Inv) : "")
                : "-i " + Ops.Q(src) + " -ss " + start + (string.IsNullOrEmpty(end) ? "" : " -to " + end);
            Ops.Ffmpeg(range + " " + Codecs(ext) + " " + Ops.Q(dest), dest);
            return dest;
        }

        /// "75", "1:15", "0:01:15.5" → seconds; -1 when it isn't a time.
        static double Seconds(string t)
        {
            if (string.IsNullOrEmpty(t)) return -1;
            double total = 0;
            foreach (var part in t.Trim().Split(':'))
            {
                double v;
                if (!double.TryParse(part, System.Globalization.NumberStyles.Float, Ops.Inv, out v)) return -1;
                total = total * 60 + v;
            }
            return total;
        }

        public static OpResult CompressAudio(List<string> files)
        {
            return Ops.Compress(files, src =>
            {
                string dest = Ops.Out(src, "mp3", "compressed");
                Ops.Ffmpeg("-i " + Ops.Q(src) + " -vn -c:a libmp3lame -b:a 128k " + Ops.Q(dest), dest);
                return dest;
            });
        }

        public static string Normalize(string src)
        {
            string ext = Catalog.Ext(src);
            string dest = Ops.Out(src, ext, "normalized");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -af loudnorm=I=-16:TP=-1.5:LRA=11 -ar 48000 " + Codecs(ext) + " " + Ops.Q(dest), dest);
            return dest;
        }

        public static string Mono(string src)
        {
            string ext = Catalog.Ext(src);
            string dest = Ops.Out(src, ext, "mono");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -ac 1 " + Codecs(ext) + " " + Ops.Q(dest), dest);
            return dest;
        }

        // ---------------- probing ----------------

        public class Info { public double Duration; public int Width, Height; public bool HasAudio, HasVideo; public string Raw; }

        public static Info Probe(string src)
        {
            string log;
            Ops.Run(Engines.Ffmpeg, "-hide_banner -nostdin -i " + Ops.Q(src), out log);
            var info = new Info { Raw = log };
            var d = System.Text.RegularExpressions.Regex.Match(log, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
            if (d.Success)
                info.Duration = int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60 + double.Parse(d.Groups[3].Value, Ops.Inv);
            var v = System.Text.RegularExpressions.Regex.Match(log, @"Stream #.*?Video:.*?(\d{2,5})x(\d{2,5})");
            if (v.Success) { info.HasVideo = true; info.Width = int.Parse(v.Groups[1].Value); info.Height = int.Parse(v.Groups[2].Value); }
            info.HasAudio = System.Text.RegularExpressions.Regex.IsMatch(log, @"Stream #.*?Audio:");
            return info;
        }

        // ---------------- compress to a size ----------------

        /// Video to roughly targetBytes: average bitrate from the duration, two-pass for accuracy.
        public static string VideoToSize(string src, long targetBytes)
        {
            var info = Probe(src);
            if (info.Duration <= 0) throw new Exception("Unknown duration");
            double audioKbps = info.HasAudio ? 96 : 0;
            double totalKbps = targetBytes * 8 / 1000.0 / info.Duration * 0.96; // ~4% container overhead
            double videoKbps = Math.Max(60, totalKbps - audioKbps);
            // shrink the frame when the budget is tiny so it doesn't turn to mush
            string scale = videoKbps < 400 ? "scale='min(854,iw)':-2," : videoKbps < 1200 ? "scale='min(1280,iw)':-2," : "";
            string dest = Ops.Out(src, "mp4", "compressed");
            string tmp = Ops.TempDir();
            try
            {
                string common = "-vf \"" + scale + Even + "\" -c:v libx264 -preset medium -b:v " + ((int)videoKbps) + "k -pix_fmt yuv420p -passlogfile " + Ops.Q(Path.Combine(tmp, "pass"));
                Ops.Ffmpeg("-i " + Ops.Q(src) + " " + common + " -pass 1 -an -f mp4 NUL", null);
                Ops.Ffmpeg("-i " + Ops.Q(src) + " " + common + " -pass 2 " + (info.HasAudio ? "-c:a aac -b:a 96k" : "-an") + " -movflags +faststart " + Ops.Q(dest), dest);
            }
            finally { Ops.TryDelete(tmp); }
            return dest;
        }

        public static string AudioToSize(string src, long targetBytes)
        {
            var info = Probe(src);
            if (info.Duration <= 0) throw new Exception("Unknown duration");
            int kbps = (int)Math.Max(24, Math.Min(320, targetBytes * 8 / 1000.0 / info.Duration * 0.97));
            string dest = Ops.Out(src, "mp3", "compressed");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -vn -c:a libmp3lame -b:a " + kbps + "k " + Ops.Q(dest), dest);
            return dest;
        }

        // ---------------- speed / join / split ----------------

        public static string Speed(string src, double factor)
        {
            var info = Probe(src);
            string ext = info.HasVideo ? VideoExt(src) : Catalog.Ext(src);
            string dest = Ops.Out(src, ext, factor.ToString("0.##", Ops.Inv) + "x");
            // atempo accepts 0.5..2 per stage, so chain stages for larger changes
            var tempo = new List<string>();
            double f = factor;
            while (f > 2.0) { tempo.Add("atempo=2.0"); f /= 2.0; }
            while (f < 0.5) { tempo.Add("atempo=0.5"); f /= 0.5; }
            tempo.Add("atempo=" + f.ToString("0.#####", Ops.Inv));
            string a = string.Join(",", tempo);
            string args = "-i " + Ops.Q(src) + " ";
            if (info.HasVideo)
            {
                args += "-filter:v \"setpts=PTS/" + factor.ToString("0.#####", Ops.Inv) + "\" ";
                args += info.HasAudio ? "-filter:a \"" + a + "\" " : "-an ";
                args += Codecs(ext);
            }
            else args += "-filter:a \"" + a + "\" " + Codecs(ext);
            Ops.Ffmpeg(args + " " + Ops.Q(dest), dest);
            return dest;
        }

        /// Joins videos (or audio files) in the given order. Videos are scaled/padded to the first one's frame.
        public static OpResult Join(List<string> files)
        {
            var infos = files.Select(Probe).ToList();
            bool video = infos.All(i => i.HasVideo);
            string ext = video ? "mp4" : (Catalog.Ext(files[0]) == "wav" ? "wav" : "m4a");
            string dest = Ops.Unique(Ops.DirOf(files[0]), Ops.BaseName(files[0]) + " +" + (files.Count - 1), ext);
            var inputs = new System.Text.StringBuilder();
            var filter = new System.Text.StringBuilder();
            var concat = new System.Text.StringBuilder();
            int w = infos[0].Width - infos[0].Width % 2, h = infos[0].Height - infos[0].Height % 2;
            for (int i = 0; i < files.Count; i++)
            {
                inputs.Append("-i ").Append(Ops.Q(files[i])).Append(' ');
                if (video)
                {
                    filter.AppendFormat(Ops.Inv, "[{0}:v]scale={1}:{2}:force_original_aspect_ratio=decrease,pad={1}:{2}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{0}];", i, w, h);
                    if (infos[i].HasAudio) filter.AppendFormat("[{0}:a]aresample=48000,aformat=channel_layouts=stereo[a{0}];", i);
                    else filter.AppendFormat(Ops.Inv, "anullsrc=r=48000:cl=stereo,atrim=0:{1}[a{0}];", i, Math.Max(0.1, infos[i].Duration).ToString("0.###", Ops.Inv));
                    concat.AppendFormat("[v{0}][a{0}]", i);
                }
                else
                {
                    filter.AppendFormat("[{0}:a]aresample=48000,aformat=channel_layouts=stereo[a{0}];", i);
                    concat.AppendFormat("[a{0}]", i);
                }
            }
            filter.Append(concat).AppendFormat(video ? "concat=n={0}:v=1:a=1[v][a]" : "concat=n={0}:v=0:a=1[a]", files.Count);
            string maps = video ? "-map \"[v]\" -map \"[a]\" -c:v libx264 -preset fast -crf 21 -c:a aac -b:a 160k -movflags +faststart"
                                : "-map \"[a]\" " + (ext == "wav" ? "-c:a pcm_s16le" : "-c:a aac -b:a 192k");
            Ops.Ffmpeg(inputs + "-filter_complex \"" + filter + "\" " + maps + " " + Ops.Q(dest), dest);
            var r = new OpResult();
            r.Outputs.Add(dest);
            return r;
        }

        /// Splits into equal parts (cut on keyframes, no re-encoding).
        public static string Split(string src, int parts)
        {
            var info = Probe(src);
            if (info.Duration <= 0) throw new Exception("Unknown duration");
            string ext = Catalog.Ext(src);
            string folder = Ops.Unique(Ops.DirOf(src), Ops.BaseName(src) + " (" + Ops.Suffix("parts") + ")", null);
            Directory.CreateDirectory(folder);
            double seg = info.Duration / parts;
            string pattern = Path.Combine(folder, Ops.Suffix("part") + "-%02d." + ext);
            try
            {
                Ops.Ffmpeg("-i " + Ops.Q(src) + " -map 0 -c copy -f segment -segment_time " + seg.ToString("0.###", Ops.Inv) +
                           " -reset_timestamps 1 " + Ops.Q(pattern), null);
                if (Directory.GetFiles(folder).Length == 0) throw new Exception("No parts were written");
            }
            catch { Ops.TryDelete(folder); throw; }
            return folder;
        }

        public static string StripMetadata(string src)
        {
            string ext = Catalog.Ext(src);
            string dest = Ops.Out(src, ext, "clean");
            Ops.Ffmpeg("-i " + Ops.Q(src) + " -map 0 -map_metadata -1 -map_chapters -1 -c copy " + Ops.Q(dest), dest);
            return dest;
        }

        /// Accepts "90", "1:30", "01:02:03" or "1:30.5"; returns an FFmpeg time or throws.
        public static string ParseTime(string s)
        {
            s = (s ?? "").Trim();
            // accept Arabic-Indic digits too
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s)
            {
                if (ch >= '٠' && ch <= '٩') sb.Append((char)('0' + (ch - '٠')));
                else if (ch >= '۰' && ch <= '۹') sb.Append((char)('0' + (ch - '۰')));
                else if (ch == '٫' || ch == ',') sb.Append('.');
                else sb.Append(ch);
            }
            s = sb.ToString();
            if (s.Length == 0) return "";
            double total = 0;
            foreach (var part in s.Split(':'))
            {
                double v;
                if (!double.TryParse(part, System.Globalization.NumberStyles.Float, Ops.Inv, out v) || v < 0) throw new FormatException(s);
                total = total * 60 + v;
            }
            return total.ToString("0.###", Ops.Inv);
        }
    }
}
