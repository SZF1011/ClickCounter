using System;
using System.IO;

namespace KeyRank.Tests
{
    internal static class StoreProbe
    {
        static int Main(string[] args)
        {
            string dir = args.Length > 0 ? args[0] : "tests/demo";
            StatsStore store = new StatsStore(dir);
            long total;
            long today;
            long mouse;
            store.GetSummary(out total, out today, out mouse);
            Console.WriteLine("total=" + total + " today=" + today + " mouse=" + mouse + " A=" + store.CountFor("K_A"));

            string csv = Path.Combine(dir, "probe.csv");
            string err;
            bool ok = store.ExportCsv(csv, out err);
            Console.WriteLine("export=" + ok + " err=" + (err == null ? "" : err));
            if (File.Exists(csv))
            {
                byte[] head = File.ReadAllBytes(csv);
                bool bom = head.Length > 2 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
                Console.WriteLine("bom=" + bom + " bytes=" + head.Length);
            }

            StatsStore again = new StatsStore(dir);
            long t2;
            long d2;
            long m2;
            again.GetSummary(out t2, out d2, out m2);
            Console.WriteLine("reload_total=" + t2 + " reload_A=" + again.CountFor("K_A") + " reload_mouse=" + m2);
            return (t2 == total && again.CountFor("K_A") == store.CountFor("K_A")) ? 0 : 1;
        }
    }
}
