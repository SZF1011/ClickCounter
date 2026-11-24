using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyRank
{
    internal sealed class InputHook : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;

        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;

        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MOUSEWHEEL = 0x020A;
        const int WM_MOUSEHWHEEL = 0x020E;

        delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        readonly LowLevelProc keyboardProc;
        readonly LowLevelProc mouseProc;
        readonly HashSet<string> pressed = new HashSet<string>();

        IntPtr keyboardHook = IntPtr.Zero;
        IntPtr mouseHook = IntPtr.Zero;
        bool disposed;

        public event Action<KeyHit> Hit;

        public string LastError { get; private set; }

        public bool IsRunning
        {
            get { return keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero; }
        }

        public InputHook()
        {
            keyboardProc = OnKeyboard;
            mouseProc = OnMouse;
        }

        public bool Start()
        {
            if (disposed) throw new ObjectDisposedException("InputHook");
            if (IsRunning) return true;

            LastError = null;
            IntPtr module = GetModuleHandle(null);

            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
            if (keyboardHook == IntPtr.Zero)
            {
                LastError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            if (mouseHook == IntPtr.Zero)
            {
                LastError = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
                return false;
            }

            lock (pressed) pressed.Clear();
            return true;
        }

        public void Stop()
        {
            if (mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(mouseHook);
                mouseHook = IntPtr.Zero;
            }
            if (keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(keyboardHook);
                keyboardHook = IntPtr.Zero;
            }
            lock (pressed) pressed.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
        }

        void Raise(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            Action<KeyHit> handler = Hit;
            if (handler == null) return;
            try
            {
                handler(new KeyHit(id, KeyCatalog.KindOf(id)));
            }
            catch
            {
            }
        }

        IntPtr OnKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && lParam != IntPtr.Zero)
                {
                    int msg = wParam.ToInt32();
                    int vk = Marshal.ReadInt32(lParam, 0);
                    int scan = Marshal.ReadInt32(lParam, 4);
                    int flags = Marshal.ReadInt32(lParam, 8);

                    if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        string upId = KeyCatalog.FromKeyboard(vk, scan, flags);
                        if (upId != null)
                        {
                            lock (pressed) pressed.Remove(upId);
                        }
                    }
                    else if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        string id = KeyCatalog.FromKeyboard(vk, scan, flags);
                        if (id != null)
                        {
                            bool first;
                            lock (pressed) first = pressed.Add(id);
                            if (first) Raise(id);
                        }
                    }
                }
            }
            catch
            {
            }
            return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_LBUTTONDOWN) Raise("M_L");
                    else if (msg == WM_RBUTTONDOWN) Raise("M_R");
                    else if (msg == WM_MOUSEWHEEL)
                    {
                        int data = Marshal.ReadInt32(lParam, 8);
                        short delta = (short)((data >> 16) & 0xFFFF);
                        if (delta > 0) Raise("M_WU");
                        else if (delta < 0) Raise("M_WD");
                    }
                    else if (msg == WM_MOUSEHWHEEL)
                    {
                        int data = Marshal.ReadInt32(lParam, 8);
                        short delta = (short)((data >> 16) & 0xFFFF);
                        if (delta > 0) Raise("M_WR");
                        else if (delta < 0) Raise("M_WL");
                    }
                }
            }
            catch
            {
            }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }
    }
}
