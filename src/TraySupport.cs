using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace KeyRank
{
    internal static class TraySupport
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

        const uint NIM_ADD = 0;
        const uint NIM_DELETE = 2;
        const uint NIF_MESSAGE = 0x01;
        const uint NIF_ICON = 0x02;
        const uint NIF_TIP = 0x04;

        public static bool Probe(IntPtr windowHandle, Icon icon)
        {
            if (windowHandle == IntPtr.Zero || icon == null) return false;

            NOTIFYICONDATA data = new NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = windowHandle;
            data.uID = 0x4B52;
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = 0x0400 + 0x51;
            data.hIcon = icon.Handle;
            data.szTip = "ClickCounter";

            bool added = false;
            try { added = Shell_NotifyIcon(NIM_ADD, ref data); }
            catch { return false; }

            if (added)
            {
                try { Shell_NotifyIcon(NIM_DELETE, ref data); } catch { }
            }
            return added;
        }
    }
}
