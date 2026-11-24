using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace KeyRank.Tests
{
    internal static class TestMain
    {
        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        static string resultPath;
        static StatsStore store;
        static InputHook hook;
        static Form win;
        static Timer timer;
        static int phase;
        static bool skipInput;
        static readonly List<string> lines = new List<string>();

        [STAThread]
        public static int Main(string[] args)
        {
            resultPath = args.Length > 0 ? args[0] : "hooktest-result.txt";
            string dataDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "keyrank_test_data");

            try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch { }

            store = new StatsStore(dataDir);
            hook = new InputHook();
            hook.Hit += delegate(KeyHit h) { store.Record(h.Id); };

            bool started = hook.Start();
            lines.Add("hook_start=" + started + (started ? "" : " err=" + hook.LastError));
            if (!started) return Finish(false);

            win = new Form();
            win.Text = "KeyRank 自动测试";
            win.FormBorderStyle = FormBorderStyle.FixedSingle;
            win.StartPosition = FormStartPosition.CenterScreen;
            win.ClientSize = new Size(380, 120);
            win.ShowInTaskbar = false;
            win.TopMost = true;
            Label lab = new Label();
            lab.Text = "按键/鼠标自动测试运行中，请稍候…";
            lab.Dock = DockStyle.Fill;
            lab.TextAlign = ContentAlignment.MiddleCenter;
            win.Controls.Add(lab);

            timer = new Timer();
            timer.Interval = 450;
            timer.Tick += OnTick;
            timer.Start();

            Application.Run(win);
            return Environment.ExitCode;
        }

        static void OnTick(object sender, EventArgs e)
        {
            phase++;
            switch (phase)
            {
                case 1:
                    ActivateAndType();
                    break;
                case 2:
                    ClickMouse();
                    break;
                case 3:
                    SendRepeat();
                    break;
                case 4:
                    Verify();
                    break;
                default:
                    break;
            }
        }

        static void ActivateAndType()
        {
            SetForegroundWindow(win.Handle);
            win.Activate();
            Application.DoEvents();

            if (GetForegroundWindow() != win.Handle)
            {
                skipInput = true;
                lines.Add("foreground=failed (skip synthetic input)");
                Finish(false);
                return;
            }

            SendKey(0x41, false); SendKey(0x41, true);   // A
            SendKey(0x42, false); SendKey(0x42, true);   // B
            SendKey(0x20, false); SendKey(0x20, true);   // Space
            SendKey(0x0D, false); SendKey(0x0D, true);   // Enter
            lines.Add("typed=AB+space+enter");
        }

        static void ClickMouse()
        {
            POINT before;
            GetCursorPos(out before);

            Point center = win.PointToScreen(new Point(win.Width / 2, win.Height / 2));
            SetCursorPos(center.X, center.Y);

            SendMouse(MOUSEEVENTF_LEFTDOWN);
            SendMouse(MOUSEEVENTF_LEFTUP);
            SendMouse(MOUSEEVENTF_RIGHTDOWN);
            SendMouse(MOUSEEVENTF_RIGHTUP);

            SetCursorPos(before.X, before.Y);
            lines.Add("mouse=left+right");
        }

        static void SendRepeat()
        {
            SendKey(0x41, false);
            SendKey(0x41, false);
            SendKey(0x41, false);
            SendKey(0x41, true);
            lines.Add("repeat=A x3 down, 1 up");
        }

        static void Verify()
        {
            long total;
            long today;
            long mouse;
            store.GetSummary(out total, out today, out mouse);

            string report = string.Format(
                "A={0} B={1} Space={2} Enter={3} MouseL={4} MouseR={5} total={6} today={7}",
                store.CountFor("K_A"), store.CountFor("K_B"), store.CountFor("K_SPACE"),
                store.CountFor("K_ENTER"), store.CountFor("M_L"), store.CountFor("M_R"), total, today);
            lines.Add("counts: " + report);

            bool pass = store.CountFor("K_A") == 2
                && store.CountFor("K_B") == 1
                && store.CountFor("K_SPACE") == 1
                && store.CountFor("K_ENTER") == 1
                && store.CountFor("M_L") == 1
                && store.CountFor("M_R") == 1
                && total == 7;

            bool saved = store.Save();
            lines.Add("save=" + saved + " file=" + store.FilePath + " exists=" + File.Exists(store.FilePath));
            Finish(pass);
        }

        static void SendKey(ushort vk, bool up)
        {
            INPUT[] data = new INPUT[1];
            data[0].type = INPUT_KEYBOARD;
            data[0].u.ki.wVk = vk;
            data[0].u.ki.wScan = 0;
            data[0].u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            data[0].u.ki.time = 0;
            data[0].u.ki.dwExtraInfo = IntPtr.Zero;
            SendInput(1, data, Marshal.SizeOf(typeof(INPUT)));
        }

        static void SendMouse(uint flag)
        {
            INPUT[] data = new INPUT[1];
            data[0].type = INPUT_MOUSE;
            data[0].u.mi.dwFlags = flag;
            data[0].u.mi.dx = 0;
            data[0].u.mi.dy = 0;
            data[0].u.mi.mouseData = 0;
            data[0].u.mi.time = 0;
            data[0].u.mi.dwExtraInfo = IntPtr.Zero;
            SendInput(1, data, Marshal.SizeOf(typeof(INPUT)));
        }

        static int Finish(bool pass)
        {
            lines.Add(pass ? "RESULT=PASS" : "RESULT=FAIL");
            try { File.WriteAllText(resultPath, string.Join("\r\n", lines.ToArray()), new UTF8Encoding(false)); }
            catch { }

            try { if (hook != null) hook.Dispose(); } catch { }
            try { if (timer != null) { timer.Stop(); timer.Dispose(); } } catch { }
            try { if (win != null) win.Close(); } catch { }

            Environment.ExitCode = pass ? 0 : 1;
            Application.Exit();
            return Environment.ExitCode;
        }
    }
}
