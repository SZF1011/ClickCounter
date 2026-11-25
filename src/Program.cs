using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeyRank
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        static readonly IntPtr PER_MONITOR_AWARE_V2 = new IntPtr(-4);
        const string MutexName = "KeyRank_SingleInstance_v1";
        const string WindowTitle = "按键排行榜";

        static readonly object logGate = new object();
        static Mutex appMutex;

        public static readonly int ShowMessage = RegisterWindowMessage("KeyRank_ShowWindow_v1");

        internal static string BootLogPath
        {
            get { return Path.Combine(Path.GetTempPath(), "ClickCounter-boot.log"); }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                RunApp(args);
            }
            catch (Exception ex)
            {
                TryLog("fatal: " + ex);
                try
                {
                    MessageBox.Show(
                        "程序启动失败：\n" + ex.Message +
                        "\n\n详细信息已写入：\n" + BootLogPath +
                        "\n（若不可写则在程序目录的 ClickCounter-boot.log）" +
                        "\n\n如果反复失败，请把该日志反馈给开发者。",
                        "ClickCounter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }

        static void RunApp(string[] args)
        {
            TryLog("start args=" + (args == null ? "" : string.Join(" ", args)));

            string dataDirArg = ValueAfter(args, "--data-dir");
            if (!string.IsNullOrEmpty(dataDirArg))
            {
                try { Environment.SetEnvironmentVariable("KEYRANK_DATA", Path.GetFullPath(dataDirArg)); }
                catch { }
            }

            if (HasArg(args, "--selftest"))
            {
                RunSelfTest();
                return;
            }

            if (HasArg(args, "--export-csv"))
            {
                RunExportCsv(args);
                return;
            }

            if (HasArg(args, "--export-record"))
            {
                RunExportRecord(args);
                return;
            }

            bool elevated = IsAdministrator();
            if (elevated)
            {
                EnsureRunAsAdminLayer();
            }
            else if (!HasArg(args, "--elevated") && !HasArg(args, "--no-elevate"))
            {
                if (RelaunchElevated(args)) return;
            }

            // 无论是否由提权子进程启动，都占用同名互斥体：
            // 防止“旧实例未退出时又双击一次”导致两个钩子同时计数、数据翻倍。
            bool created = true;
            try
            {
                appMutex = new Mutex(true, MutexName, out created);
            }
            catch (Exception ex)
            {
                TryLog("mutex unavailable: " + ex.Message);
                created = true;
            }

            if (!created)
            {
                TryLog("already running; requesting restore");
                PostMessage(HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                IntPtr existing = FindWindow(null, WindowTitle);
                if (existing != IntPtr.Zero)
                {
                    ShowWindow(existing, 9);
                    SetForegroundWindow(existing);
                }
                return;
            }

            // 兼容更早的构建（它们启动时不占用互斥体）：只要已存在「按键排行榜」窗口，
            // 就不再启动第二个钩子，否则两个实例会同时计数、数字翻倍。
            IntPtr runningWindow = FindWindow(null, WindowTitle);
            if (runningWindow != IntPtr.Zero)
            {
                TryLog("window exists; requesting restore");
                PostMessage(HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                ShowWindow(runningWindow, 9);
                SetForegroundWindow(runningWindow);
                return;
            }

            try { SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2); }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            bool startHidden = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase)) startHidden = true;
                }
            }

            TryLog("creating main form");
            Application.Run(new MainForm(startHidden));
            TryLog("exited");
        }

        static void RunExportLog(string[] args)
        {
            string path = ValueAfter(args, "--export-log");
            if (string.IsNullOrEmpty(path))
            {
                Environment.ExitCode = 2;
                return;
            }

            InputLog log = null;
            string error = null;
            int rows = 0;
            bool ok = false;
            try
            {
                log = new InputLog(StatsStore.DefaultDataDir(), true);
                ok = log.ExportBuckets(path, out error, out rows);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                if (log != null)
                {
                    try { log.Close(); } catch { }
                }
            }

            if (!ok)
            {
                TryLog("export-log failed: " + error);
                Environment.ExitCode = 1;
            }
            else
            {
                Environment.ExitCode = 0;
            }
        }

        static void RunExportRecord(string[] args)
        {
            string path = ValueAfter(args, "--export-record");
            string dayText = ValueAfter(args, "--day");
            DateTime day;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(dayText) || !DateTime.TryParse(dayText, out day))
            {
                Environment.ExitCode = 2;
                return;
            }

            InputLog log = null;
            string error = null;
            bool ok = false;
            try
            {
                log = new InputLog(StatsStore.DefaultDataDir(), true);
                string text = log.BuildDayText(day);
                ok = FileExport.WriteTextFile(path, text, log.Dir, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                if (log != null)
                {
                    try { log.Close(); } catch { }
                }
            }

            if (!ok)
            {
                TryLog("export-record failed: " + error);
                Environment.ExitCode = 1;
            }
            else
            {
                Environment.ExitCode = 0;
            }
        }

        static void RunExportCsv(string[] args)
        {
            string path = ValueAfter(args, "--export-csv");
            if (string.IsNullOrEmpty(path))
            {
                Environment.ExitCode = 2;
                return;
            }

            string error = null;
            bool ok = false;
            try
            {
                StatsStore store = new StatsStore(StatsStore.DefaultDataDir());
                ok = HasArg(args, "--today") ? store.ExportTodayCsv(path, out error) : store.ExportCsv(path, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (!ok)
            {
                TryLog("export-csv failed: " + error);
                Environment.ExitCode = 1;
            }
            else
            {
                Environment.ExitCode = 0;
            }
        }

        internal static string BootLogPathAlt
        {
            get { return Path.Combine(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory), "ClickCounter-boot.log"); }
        }

        internal static void TryLog(string text)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + text + Environment.NewLine;
            lock (logGate)
            {
                try { File.AppendAllText(BootLogPath, line); return; }
                catch { }
                try { File.AppendAllText(BootLogPathAlt, line); }
                catch { }
            }
        }

        static void RunSelfTest()
        {
            InputHook probe = new InputHook();
            bool installed = probe.Start();
            string text = "installed=" + installed + "\r\nerror=" + (probe.LastError == null ? "" : probe.LastError) +
                "\r\ntime=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt");
            try { File.WriteAllText(path, text, new System.Text.UTF8Encoding(false)); }
            catch { }
            probe.Dispose();
            Thread.Sleep(2000);
        }

        static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            TryLog("thread exception: " + e.Exception);
            MessageBox.Show("程序遇到错误：" + e.Exception.Message, "KeyRank",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null) TryLog("domain exception: " + ex);
        }

        static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        static bool RelaunchElevated(string[] args)
        {
            try
            {
                string extra = "--elevated";
                if (HasArg(args, "--tray")) extra += " --tray";
                string dataDir = ValueAfter(args, "--data-dir");
                if (string.IsNullOrEmpty(dataDir)) dataDir = StatsStore.DefaultDataDir();
                if (!string.IsNullOrEmpty(dataDir)) extra += " --data-dir \"" + dataDir + "\"";

                ProcessStartInfo info = new ProcessStartInfo(Application.ExecutablePath, extra);
                info.UseShellExecute = true;
                info.Verb = "runas";
                Process.Start(info);
                TryLog("relaunched elevated");
                return true;
            }
            catch (Exception ex)
            {
                TryLog("elevate failed: " + ex.Message);
                return false;
            }
        }

        const string LayersKeyPath = "Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers";

        internal static bool IsRunAsAdminLayer()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LayersKeyPath))
                {
                    if (key == null) return false;
                    object value = key.GetValue(Application.ExecutablePath);
                    return value != null && value.ToString().IndexOf("RUNASADMIN", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void SetRunAsAdminLayer(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(LayersKeyPath))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(Application.ExecutablePath, "~ RUNASADMIN", RegistryValueKind.String);
                    else key.DeleteValue(Application.ExecutablePath, false);
                }
            }
            catch (Exception ex)
            {
                TryLog("layers write failed: " + ex.Message);
            }
        }

        static void EnsureRunAsAdminLayer()
        {
            if (!IsRunAsAdminLayer()) SetRunAsAdminLayer(true);
        }

        static string ValueAfter(string[] args, string name)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return (i + 1 < args.Length) ? args[i + 1] : null;
            }
            return null;
        }

        static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
