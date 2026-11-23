using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Klementina
{
    /// Global keyboard + mouse hooks. Detects "modifier tapped while the left button is dragging",
    /// and routes navigation keys to the wheel while it is open in keyboard mode.
    class InputHooks : IDisposable
    {
        public event Action<string, Point, Point> ModifierTapWhileDragging; // key, cursor, drag start
        public event Action<string> ModifierTap;                            // any tap (used to switch wheels)
        public event Action MouseReleased;
        public event Action<Point> MouseDownAt;
        public event Action<Keys> NavKey;
        public Func<bool> KeyboardWheelOpen = () => false;

        readonly SynchronizationContext ui;
        readonly Native.LowLevelProc kproc, mproc;
        IntPtr khook, mhook;

        bool lDown, dragging;
        Point downPt;
        int pendingVk, pendingTick;

        public InputHooks()
        {
            ui = SynchronizationContext.Current;
            kproc = KeyProc;
            mproc = MouseProc;
            IntPtr mod = Native.GetModuleHandle(null);
            khook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, kproc, mod, 0);
            mhook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mproc, mod, 0);
        }

        public bool Dragging { get { return dragging; } }

        static string ModName(int vk)
        {
            switch (vk)
            {
                case 0xA0: case 0xA1: case 0x10: return "shift";
                case 0xA2: case 0xA3: case 0x11: return "ctrl";
                case 0xA4: case 0xA5: case 0x12: return "alt";
            }
            return null;
        }

        void Post(Action a) { ui.Post(_ => a(), null); }

        IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vk = Marshal.ReadInt32(lParam);
                int msg = wParam.ToInt32();
                bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                string mod = ModName(vk);

                {
                    if (down)
                    {
                        if (mod != null) { if (pendingVk != vk) { pendingVk = vk; pendingTick = Environment.TickCount; } }
                        else pendingVk = 0;
                    }
                    else if (up && mod != null && pendingVk == vk)
                    {
                        pendingVk = 0;
                        if (Environment.TickCount - pendingTick < 450)
                        {
                            Point cur = Cursor.Position;
                            Point start = downPt;
                            bool wasDragging = dragging;
                            Post(() =>
                            {
                                if (wasDragging && ModifierTapWhileDragging != null) ModifierTapWhileDragging(mod, cur, start);
                                else if (ModifierTap != null) ModifierTap(mod);
                            });
                        }
                    }
                }

                if (mod == null && KeyboardWheelOpen())
                {
                    Keys k = (Keys)vk;
                    bool nav = k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || k == Keys.Return ||
                               k == Keys.Escape || k == Keys.Tab || k == Keys.PageDown || k == Keys.PageUp || k == Keys.Space;
                    if (nav)
                    {
                        if (down) Post(() => { if (NavKey != null) NavKey(k); });
                        return new IntPtr(1); // swallow while the wheel has the keyboard
                    }
                }
            }
            return Native.CallNextHookEx(khook, nCode, wParam, lParam);
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var pt = new Point(Marshal.ReadInt32(lParam), Marshal.ReadInt32(lParam, 4));
                if (msg == Native.WM_LBUTTONDOWN)
                {
                    lDown = true; dragging = false; downPt = pt;
                    Post(() => { if (MouseDownAt != null) MouseDownAt(pt); });
                }
                else if (msg == Native.WM_MOUSEMOVE)
                {
                    if (lDown && !dragging && (Math.Abs(pt.X - downPt.X) > 6 || Math.Abs(pt.Y - downPt.Y) > 6))
                        dragging = true;
                }
                else if (msg == Native.WM_LBUTTONUP)
                {
                    lDown = false; dragging = false;
                    Post(() => { if (MouseReleased != null) MouseReleased(); });
                }
            }
            return Native.CallNextHookEx(mhook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (khook != IntPtr.Zero) Native.UnhookWindowsHookEx(khook);
            if (mhook != IntPtr.Zero) Native.UnhookWindowsHookEx(mhook);
            khook = mhook = IntPtr.Zero;
        }
    }
}
