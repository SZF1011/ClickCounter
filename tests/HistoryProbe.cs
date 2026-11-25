using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace KeyRank
{
    internal static class HistoryProbe
    {
        static int failures;

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail == null || detail.Length == 0 ? "" : " :: " + detail));
            if (!ok) failures++;
        }

        static int Main(string[] args)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-data", "history");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            StatsStore store = new StatsStore(dir);
            for (int i = 0; i < 3; i++) store.Record("K_A");
            for (int i = 0; i < 2; i++) store.Record("M_L");
            Check("save", store.Save(), store.LastError);

            string todayFile = Path.Combine(dir, "history", DateTime.Today.ToString("yyyy-MM-dd") + ".tsv");
            Check("today file exists", File.Exists(todayFile), todayFile);

            long total = store.GetDayTotal(DateTime.Today);
            Check("today total=5", total == 5, "total=" + total);

            List<KeyStat> rows = store.BuildDayRows(DateTime.Today, true, true, false);
            Check("today rows=2", rows.Count == 2, "rows=" + rows.Count);
            Check("top K_A=3", rows.Count == 2 && rows[0].Id == "K_A" && rows[0].Count == 3,
                rows.Count == 2 ? rows[0].Id + "=" + rows[0].Count : "n/a");

            List<DateTime> days = store.GetHistoryDays();
            Check("history days has today", days.Contains(DateTime.Today), "count=" + days.Count);

            FieldInfo dayField = typeof(StatsStore).GetField("day", BindingFlags.NonPublic | BindingFlags.Instance);
            dayField.SetValue(store, DateTime.Today.AddDays(-1));
            store.Save();

            string yesterdayFile = Path.Combine(dir, "history", DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") + ".tsv");
            Check("yesterday flushed", File.Exists(yesterdayFile), yesterdayFile);
            Check("yesterday total=5", store.GetDayTotal(DateTime.Today.AddDays(-1)) == 5, "");

            List<KeyStat> yRows = store.BuildDayRows(DateTime.Today.AddDays(-1), true, true, false);
            Check("yesterday rows=2", yRows.Count == 2, "rows=" + yRows.Count);

            StatsStore reopened = new StatsStore(dir);
            Check("reopen reads yesterday", reopened.GetDayTotal(DateTime.Today.AddDays(-1)) == 5, "");

            reopened.Reset();
            reopened.Save();
            Check("reset clears today file", !File.Exists(todayFile), todayFile);

            Console.WriteLine(failures == 0 ? "RESULT=PASS" : "RESULT=FAIL(" + failures + ")");
            try { Directory.Delete(dir, true); } catch { }
            return failures == 0 ? 0 : 1;
        }
    }
}
