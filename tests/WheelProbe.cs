using System;
using System.Collections.Generic;
using System.IO;

namespace KeyRank
{
    internal static class WheelProbe
    {
        static int failures;

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail == null || detail.Length == 0 ? "" : " :: " + detail));
            if (!ok) failures++;
        }

        static int Main(string[] args)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-data", "wheel");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            StatsStore store = new StatsStore(dir);
            for (int i = 0; i < 4; i++) store.Record("K_A");
            for (int i = 0; i < 3; i++) store.Record("M_L");
            for (int i = 0; i < 5; i++) store.Record("M_WU");

            long total, today, mouse, wheel, todayWheel;
            store.GetSummary(out total, out today, out mouse, out wheel, out todayWheel);

            Check("total excludes wheel (7)", total == 7, "total=" + total);
            Check("today excludes wheel (7)", today == 7, "today=" + today);
            Check("mouse=3", mouse == 3, "mouse=" + mouse);
            Check("wheel=5", wheel == 5, "wheel=" + wheel);
            Check("todayWheel=5", todayWheel == 5, "todayWheel=" + todayWheel);

            List<KeyStat> all = store.BuildRows(true, true, false, null);
            Check("all page rows=2 no wheel", all.Count == 2, "rows=" + all.Count);
            List<KeyStat> wheels = store.BuildRows(false, false, true, null);
            Check("wheel page rows=1", wheels.Count == 1 && wheels[0].Id == "M_WU", "rows=" + wheels.Count);

            Check("day total excludes wheel (7)", store.GetDayTotal(DateTime.Today) == 7, "");
            Check("day total incl wheel (12)", store.GetDayTotal(DateTime.Today, true) == 12, "");

            store.Save();
            StatsStore reopened = new StatsStore(dir);
            long t2, td2, m2, w2, tw2;
            reopened.GetSummary(out t2, out td2, out m2, out w2, out tw2);
            Check("persisted wheel=5", w2 == 5, "wheel=" + w2);
            Check("persisted total=7", t2 == 7, "total=" + t2);

            Console.WriteLine(failures == 0 ? "RESULT=PASS" : "RESULT=FAIL(" + failures + ")");
            try { Directory.Delete(dir, true); } catch { }
            return failures == 0 ? 0 : 1;
        }
    }
}
