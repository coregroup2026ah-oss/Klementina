using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace Klementina
{
    /// Crop for video: pick the area on a full-size frame, then FFmpeg crops every frame.
    class VideoCropEditor : CropEditor
    {
        VideoCropEditor(string path, Bitmap frame) : base(path, frame, false) { Text = S.T("crop_video") + " — " + Path.GetFileName(path); }

        public static OpResult Open(List<string> files)
        {
            string src = files[0];
            var info = MediaOps.Probe(src);
            string tmp = Ops.TempDir();
            try
            {
                string frame = Path.Combine(tmp, "frame.png");
                double t = info.Duration > 2 ? 1 : 0;
                Ops.Ffmpeg("-ss " + t.ToString("0.###", Ops.Inv) + " -i " + Ops.Q(src) + " -frames:v 1 " + Ops.Q(frame), frame);
                Bitmap bmp;
                using (var ms = new MemoryStream(File.ReadAllBytes(frame))) using (var b = new Bitmap(ms)) bmp = new Bitmap(b);
                new VideoCropEditor(src, bmp).Show();
            }
            finally { Ops.TryDelete(tmp); }
            return new OpResult { Quiet = true };
        }

        protected override void Save()
        {
            BeforeSave();
            var rect = cropAtSave;
            string src = SrcPath;
            Program.RunWorker(() => Ops.Each(new List<string> { src }, f => MediaOps.CropVideo(f, rect)), true);
            Close();
        }
    }
}
