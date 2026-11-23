using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Klementina
{
    static class Settings
    {
        // First run: Arabic when Windows is in Arabic or an Arabic keyboard is installed (many Arabic speakers run
        // English Windows), English otherwise. Afterwards whatever the user picked in Settings.
        public static string Lang = DefaultLang();

        static string DefaultLang()
        {
            try
            {
                if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar") return "ar";
                foreach (System.Windows.Forms.InputLanguage il in System.Windows.Forms.InputLanguage.InstalledInputLanguages)
                    if (il.Culture.TwoLetterISOLanguageName == "ar") return "ar";
            }
            catch { }
            return "en";
        }
        public static bool DragEnabled = true;
        public static bool HotkeyEnabled = true;
        public static string HotkeyLetter = "Z";
        public static readonly string[] HotkeyLetters = { "Z", "Q", "W", "A", "S", "X", "K" };
        public static string KeyConvert = "shift", KeyTools = "ctrl", KeyActions = "alt";
        public static int JpegQuality = 85;
        public static string FfmpegPath = "";
        public static bool FirstRun = true;

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Klementina"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                string v;
                if (d.TryGetValue("lang", out v)) Lang = v == "en" ? "en" : "ar";
                if (d.TryGetValue("drag", out v)) DragEnabled = v == "1";
                if (d.TryGetValue("hotkey", out v)) HotkeyEnabled = v == "1";
                if (d.TryGetValue("hotkey_letter", out v) && System.Array.IndexOf(HotkeyLetters, v) >= 0) HotkeyLetter = v;
                if (d.TryGetValue("key_convert", out v)) KeyConvert = v;
                if (d.TryGetValue("key_tools", out v)) KeyTools = v;
                if (d.TryGetValue("key_actions", out v)) KeyActions = v;
                if (d.TryGetValue("jpeg_quality", out v)) { int q; if (int.TryParse(v, out q)) JpegQuality = Math.Max(30, Math.Min(100, q)); }
                if (d.TryGetValue("ffmpeg", out v)) FfmpegPath = v;
                if (d.TryGetValue("first_run", out v)) FirstRun = v == "1";
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(FilePath, new[] {
                    "lang=" + Lang,
                    "drag=" + (DragEnabled ? "1" : "0"),
                    "hotkey=" + (HotkeyEnabled ? "1" : "0"),
                    "hotkey_letter=" + HotkeyLetter,
                    "key_convert=" + KeyConvert,
                    "key_tools=" + KeyTools,
                    "key_actions=" + KeyActions,
                    "jpeg_quality=" + JpegQuality,
                    "ffmpeg=" + FfmpegPath,
                    "first_run=" + (FirstRun ? "1" : "0"),
                });
            }
            catch { }
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("Klementina") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("Klementina", "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else k.DeleteValue("Klementina", false);
                }
            }
        }

        public static bool DarkMode
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                        return v is int && (int)v == 0;
                    }
                }
                catch { return false; }
            }
        }

        public static WheelKind? KindForKey(string key)
        {
            if (key == KeyConvert) return WheelKind.Convert;
            if (key == KeyTools) return WheelKind.Tools;
            if (key == KeyActions) return WheelKind.Actions;
            return null;
        }

        public static string KeyFor(WheelKind k)
        {
            return k == WheelKind.Convert ? KeyConvert : k == WheelKind.Tools ? KeyTools : KeyActions;
        }

        public static string KeyLabel(string key)
        {
            return key == "shift" ? "Shift" : key == "ctrl" ? "Ctrl" : "Alt";
        }
    }
}
