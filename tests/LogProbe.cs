using System;
using System.IO;

namespace KeyRank.Tests
{
    internal static class LogProbe
    {
        static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : "tests/logprobe";
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            InputLog log = new InputLog(dir);
            DateTime t0 = new DateTime(2026, 11, 24, 12, 3, 10);
            log.Record("K_A", t0);
            log.Record("K_A", t0.AddMinutes(1));
            log.Record("K_A", t0.AddMinutes(6));
            log.Record("M_L", t0.AddMinutes(7));
            log.Flush();

            Console.WriteLine("logdir=" + log.Dir);
            foreach (string file in Directory.GetFiles(log.Dir))
                Console.WriteLine("file=" + Path.GetFileName(file) + " size=" + new FileInfo(file).Length);

            string[] found = Directory.GetFiles(log.Dir, "events-*.tsv");
            Console.WriteLine("found_files=" + found.Length);
            foreach (string f in found) Console.WriteLine("found=" + Path.GetFileName(f));
            DateTime probeDay;
            bool dayOk = DateTime.TryParseExact("2026-11-24", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out probeDay);
            DateTime probeClock;
            bool clockOk = DateTime.TryParseExact("12:03:10", "HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out probeClock);
            Console.WriteLine("parse day=" + dayOk + " clock=" + clockOk + " clockTime=" + probeClock.TimeOfDay);

            string csv = Path.Combine(dir, "export.csv");
            string error;
            int rows;
            bool ok = log.ExportBuckets(csv, out error, out rows);
            Console.WriteLine("export=" + ok + " rows=" + rows + " err=" + (error == null ? "" : error));
            if (ok)
            {
                foreach (string line in File.ReadAllLines(csv)) Console.WriteLine("csv: " + line);
            }
            log.Close();
            return (ok && rows == 3) ? 0 : 1;
        }
    }
}
