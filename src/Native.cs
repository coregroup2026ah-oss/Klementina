using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Klementina
{
    static class Native
    {
        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        public const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202;
        public const int WM_HOTKEY = 0x312;
        public const int WS_EX_LAYERED = 0x80000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_NOACTIVATE = 0x8000000;
        public const int ULW_ALPHA = 2;
        public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
        public const int GA_ROOT = 2;

        public delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);

        delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc fn, IntPtr l);

        public static void EnumWindowsAll(Action<IntPtr> each)
        {
            EnumWindows((h, l) => { each(h); return true; }, IntPtr.Zero);
        }

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);

        public static void MakeDpiAware()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static float ScaleAt(Point p)
        {
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT(p.X, p.Y), 2);
                uint x, y;
                if (GetDpiForMonitor(mon, 0, out x, out y) == 0 && x > 0) return x / 96f;
            }
            catch { }
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize; public uint fMask; public IntPtr hwnd;
            public string lpVerb, lpFile, lpParameters, lpDirectory;
            public int nShow; public IntPtr hInstApp, lpIDList; public string lpClass;
            public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        public static void ShellVerb(string file, string verb)
        {
            var info = new SHELLEXECUTEINFO();
            info.cbSize = Marshal.SizeOf(info);
            info.lpVerb = verb; info.lpFile = file; info.nShow = 1;
            info.fMask = 0x0000000C; // SEE_MASK_INVOKEIDLIST
            ShellExecuteEx(ref info);
        }

        // ---- shell thumbnails ----
        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        public static Bitmap Thumbnail(string path, int size, bool iconOnly)
        {
            IShellItemImageFactory f = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                int flags = iconOnly ? 0x4 : 0x1; // SIIGBF_ICONONLY : SIIGBF_BIGGERSIZEOK
                if (f.GetImage(new SIZE(size, size), flags, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                return FromHBitmapAlpha(hbm);
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (f != null) Marshal.ReleaseComObject(f);
            }
        }

        static Bitmap FromHBitmapAlpha(IntPtr hbm)
        {
            using (var raw = Image.FromHbitmap(hbm))
            {
                if (Image.GetPixelFormatSize(raw.PixelFormat) < 32) return new Bitmap(raw);
                var rect = new Rectangle(0, 0, raw.Width, raw.Height);
                var bd = raw.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, raw.PixelFormat);
                try
                {
                    // Detect a fully transparent result (some thumbnails have no alpha) and treat it as opaque.
                    // Row by row: the stride can be negative for bottom-up bitmaps.
                    int row = raw.Width * 4, len = row * raw.Height;
                    byte[] px = new byte[len];
                    for (int y = 0; y < raw.Height; y++)
                        Marshal.Copy(bd.Scan0 + y * bd.Stride, px, y * row, row);
                    bool anyAlpha = false;
                    for (int i = 3; i < len; i += 4) if (px[i] != 0) { anyAlpha = true; break; }
                    if (!anyAlpha) for (int i = 3; i < len; i += 4) px[i] = 255;
                    var outBmp = new Bitmap(raw.Width, raw.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    var od = outBmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, outBmp.PixelFormat);
                    for (int y = 0; y < raw.Height; y++)
                        Marshal.Copy(px, y * row, od.Scan0 + y * od.Stride, row);
                    outBmp.UnlockBits(od);
                    return outBmp;
                }
                finally { raw.UnlockBits(bd); }
            }
        }

        // ---- Explorer tab detection ----
        [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IServiceProvider { [PreserveSig] int QueryService(ref Guid sid, ref Guid iid, out IntPtr obj); }

        [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOleWindow { [PreserveSig] int GetWindow(out IntPtr hwnd); }

        public static IntPtr TabWindowOf(object explorerWindow)
        {
            try
            {
                var sp = explorerWindow as IServiceProvider;
                if (sp == null) return IntPtr.Zero;
                Guid sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
                Guid iid = new Guid("000214E2-0000-0000-C000-000000000046"); // IID_IShellBrowser
                IntPtr p;
                if (sp.QueryService(ref sid, ref iid, out p) != 0 || p == IntPtr.Zero) return IntPtr.Zero;
                try
                {
                    var ow = (IOleWindow)Marshal.GetObjectForIUnknown(p);
                    IntPtr h;
                    return ow.GetWindow(out h) == 0 ? h : IntPtr.Zero;
                }
                finally { Marshal.Release(p); }
            }
            catch { return IntPtr.Zero; }
        }

        // ---- modern folder picker ----
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(); void SetFileTypeIndex(); void GetFileTypeIndex(); void Advise(); void Unadvise();
            void SetOptions(uint fos);
            void GetOptions(out uint fos);
            void SetDefaultFolder(); void SetFolder(); void GetFolder(); void GetCurrentSelection(); void SetFileName();
            void GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel(); void SetFileNameLabel();
            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(); void GetParent();
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }

        public static string PickFolder(IntPtr owner, string title)
        {
            var dlg = (IFileDialog)new FileOpenDialogRCW();
            try
            {
                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | 0x20 | 0x40); // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                dlg.SetTitle(title);
                if (dlg.Show(owner) != 0) return null;
                IShellItem item;
                dlg.GetResult(out item);
                string path;
                item.GetDisplayName(0x80058000, out path); // SIGDN_FILESYSPATH
                return path;
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(dlg); }
        }
    }
}
