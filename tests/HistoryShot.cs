using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace KeyRank
{
    internal static class HistoryShot
    {
        [STAThread]
        static int Main(string[] args)
        {
            string dir = (args != null && args.Length > 0) ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shotapp", "KeyClickRank-data");
            StatsStore store = new StatsStore(dir);
            HistoryForm form = new HistoryForm(store, DateTime.Today.AddDays(-1));

            string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "historyshot.log");
            try
            {
                File.WriteAllText(log, "start " + dir + " args=" + (args == null ? "null" : string.Join(",", args)) + Environment.NewLine);
                if (args != null && args.Length > 1 && args[1] == "total")
                {
                    MethodInfo m = typeof(HistoryForm).GetMethod("ShowTotal", BindingFlags.NonPublic | BindingFlags.Instance);
                    File.AppendAllText(log, "method=" + (m == null ? "null" : "found") + Environment.NewLine);
                    if (m != null) m.Invoke(form, null);
                    File.AppendAllText(log, "invoked ok" + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, "EX " + ex.GetType().FullName + ": " + ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine);
            }

            Timer timer = new Timer();
            timer.Interval = 120000;
            timer.Tick += delegate { timer.Stop(); form.Close(); };
            timer.Start();
            Application.Run(form);
            return 0;
        }
    }
}
