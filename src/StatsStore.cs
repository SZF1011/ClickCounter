using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeyRank
{
    internal sealed class KeyStat
    {
        public string Id;
        public string Label;
        public HitKind Kind;
        public long Count;
    }

    internal sealed class StatsStore
    {
        readonly object gate = new object();
        readonly Dictionary<string, long> counts = new Dictionary<string, long>();
        readonly string dataDir;
        readonly string filePath;

        long total;
        long today;
        DateTime day;
        bool dirty;
        string loadWarning;

        public string DataDir { get { return dataDir; } }
        public string FilePath { get { return filePath; } }
        public string LoadWarning { get { return loadWarning; } }
        public string LastError { get; private set; }

        public bool IsDirty
        {
            get { lock (gate) return dirty; }
        }

        public StatsStore(string dir)
        {
            dataDir = dir;
            Directory.CreateDirectory(dataDir);
            filePath = Path.Combine(dataDir, "stats.tsv");
            day = DateTime.Today;
            Load();
        }

        public static string DefaultDataDir()
        {
            string over = Environment.GetEnvironmentVariable("KEYRANK_DATA");
            if (!string.IsNullOrEmpty(over)) return over;

            string baseDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string portable = Path.Combine(baseDir, "KeyClickRank-data");
            if (Directory.Exists(portable)) return portable;

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string temp = Path.GetTempPath();

            string[] candidates = new string[]
            {
                Path.Combine(roaming, "KeyClickRank"),
                Path.Combine(local, "KeyClickRank"),
                Path.Combine(baseDir, "KeyClickRank-data"),
                Path.Combine(temp, "KeyClickRank")
            };

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                try
                {
                    Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return candidate;
                }
                catch
                {
                }
            }

            return Path.Combine(temp, "KeyClickRank");
        }

        void RollDayLocked()
        {
            DateTime now = DateTime.Today;
            if (day != now)
            {
                day = now;
                today = 0;
                dirty = true;
            }
        }

        public void Record(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (gate)
            {
                RollDayLocked();
                long c;
                if (counts.TryGetValue(id, out c)) counts[id] = c + 1;
                else counts[id] = 1;
                total++;
                today++;
                dirty = true;
            }
        }

        public long CountFor(string id)
        {
            lock (gate)
            {
                long c;
                return counts.TryGetValue(id, out c) ? c : 0;
            }
        }

        public void GetSummary(out long totalCount, out long todayCount, out long mouseCount)
        {
            lock (gate)
            {
                RollDayLocked();
                totalCount = total;
                todayCount = today;
                long m = 0;
                foreach (KeyValuePair<string, long> kv in counts)
                {
                    if (kv.Key == "M_L" || kv.Key == "M_R") m += kv.Value;
                }
                mouseCount = m;
            }
        }

        public List<KeyStat> BuildRows(bool includeKeyboard, bool includeMouse, bool includeWheel, string query)
        {
            lock (gate)
            {
                RollDayLocked();
                List<KeyStat> rows = new List<KeyStat>(counts.Count);
                foreach (KeyValuePair<string, long> kv in counts)
                {
                    if (kv.Value <= 0) continue;
                    HitKind kind = KeyCatalog.KindOf(kv.Key);
                    if (kind == HitKind.Mouse && !includeMouse) continue;
                    if (kind == HitKind.Wheel && !includeWheel) continue;
                    if (kind == HitKind.Keyboard && !includeKeyboard) continue;

                    string label = KeyCatalog.Label(kv.Key);
                    if (!string.IsNullOrEmpty(query))
                    {
                        bool match = label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                        if (!match) match = kv.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!match) continue;
                    }

                    KeyStat s = new KeyStat();
                    s.Id = kv.Key;
                    s.Label = label;
                    s.Kind = kind;
                    s.Count = kv.Value;
                    rows.Add(s);
                }
                rows.Sort(CompareRows);
                return rows;
            }
        }

        static int CompareRows(KeyStat a, KeyStat b)
        {
            int c = b.Count.CompareTo(a.Count);
            if (c != 0) return c;
            c = ((int)a.Kind).CompareTo((int)b.Kind);
            if (c != 0) return c;
            return string.Compare(a.Label, b.Label, StringComparison.CurrentCulture);
        }

        public void Reset()
        {
            lock (gate)
            {
                counts.Clear();
                total = 0;
                today = 0;
                day = DateTime.Today;
                dirty = true;
            }
        }

        public bool Save()
        {
            List<KeyValuePair<string, long>> snap;
            long tot;
            long td;
            string dayText;
            lock (gate)
            {
                RollDayLocked();
                snap = new List<KeyValuePair<string, long>>(counts);
                tot = total;
                td = today;
                dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                dirty = false;
            }

            snap.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                return string.CompareOrdinal(a.Key, b.Key);
            });

            StringBuilder sb = new StringBuilder(snap.Count * 12 + 64);
            sb.AppendLine("# KeyRank v1");
            sb.Append("day\t").Append(dayText).AppendLine();
            sb.Append("daycount\t").Append(td.ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("total\t").Append(tot.ToString(CultureInfo.InvariantCulture)).AppendLine();
            foreach (KeyValuePair<string, long> kv in snap)
            {
                sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }

            string tmp = filePath + ".tmp";
            try
            {
                Directory.CreateDirectory(dataDir);
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(filePath)) File.Replace(tmp, filePath, null);
                else File.Move(tmp, filePath);
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                lock (gate) dirty = true;
                LastError = ex.Message;
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                return false;
            }
        }

        public bool ExportCsv(string path, out string error)
        {
            error = null;
            try
            {
                List<KeyStat> rows = BuildRows(true, true, true, null);
                long tot;
                long td;
                long mouse;
                GetSummary(out tot, out td, out mouse);

                StringBuilder sb = new StringBuilder(rows.Count * 32 + 96);
                sb.AppendLine("名次,按键,类型,次数,占比");
                int rank = 1;
                foreach (KeyStat s in rows)
                {
                    double pct = tot > 0 ? (s.Count * 100.0 / tot) : 0.0;
                    sb.Append(rank.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(CsvField(s.Label)).Append(',');
                    string kindText = s.Kind == HitKind.Mouse ? "鼠标" : (s.Kind == HitKind.Wheel ? "滚轮" : "键盘");
                    sb.Append(kindText).Append(',');
                    sb.Append(s.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(pct.ToString("0.0", CultureInfo.InvariantCulture)).AppendLine("%");
                    rank++;
                }
                return FileExport.WriteTextFile(path, sb.ToString(), dataDir, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string CsvField(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        void Load()
        {
            if (!File.Exists(filePath)) return;

            try
            {
                string[] source = File.ReadAllLines(filePath, Encoding.UTF8);
                Dictionary<string, long> loaded = new Dictionary<string, long>();
                DateTime loadedDay = DateTime.Today;
                long loadedToday = 0;
                long loadedTotal = -1;

                foreach (string raw in source)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string key = line.Substring(0, tab).Trim();
                    string value = line.Substring(tab + 1).Trim();

                    if (key == "day")
                    {
                        DateTime parsed;
                        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out parsed)) loadedDay = parsed;
                    }
                    else if (key == "daycount")
                    {
                        long n;
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) loadedToday = n;
                    }
                    else if (key == "total")
                    {
                        long n;
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) loadedTotal = n;
                    }
                    else if (key.StartsWith("K_", StringComparison.Ordinal) || key.StartsWith("M_", StringComparison.Ordinal))
                    {
                        long n;
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                            loaded[key] = n;
                    }
                }

                long sum = 0;
                foreach (KeyValuePair<string, long> kv in loaded) sum += kv.Value;
                if (loadedTotal < 0) loadedTotal = sum;

                lock (gate)
                {
                    counts.Clear();
                    foreach (KeyValuePair<string, long> kv in loaded) counts[kv.Key] = kv.Value;
                    total = loadedTotal;
                    today = loadedToday;
                    day = loadedDay;
                    RollDayLocked();
                }
            }
            catch (Exception ex)
            {
                loadWarning = "统计数据读取失败，已备份并从零开始：" + ex.Message;
                BackupCorrupt();
                lock (gate)
                {
                    counts.Clear();
                    total = 0;
                    today = 0;
                    day = DateTime.Today;
                }
            }
        }

        void BackupCorrupt()
        {
            try
            {
                if (!File.Exists(filePath)) return;
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string target = Path.Combine(dataDir, "stats.bad-" + stamp + ".tsv");
                File.Move(filePath, target);
            }
            catch
            {
            }
        }
    }
}
