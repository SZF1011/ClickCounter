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
        readonly string historyDir;
        readonly Dictionary<string, long> todayByKey = new Dictionary<string, long>();

        long total;
        long today;
        long todayWheel;
        DateTime day;
        bool dirty;
        string loadWarning;

        public string DataDir { get { return dataDir; } }
        public string FilePath { get { return filePath; } }
        public string LoadWarning { get { return loadWarning; } }
        public string LastError { get; private set; }
        public bool HasDayWheelField { get; private set; }

        public void ApplyTodayWheelBaseline(long count)
        {
            lock (gate)
            {
                if (HasDayWheelField) return;
                if (count < 0) count = 0;
                if (count > today) count = today;
                todayWheel = count;
                HasDayWheelField = true;
                dirty = true;
            }
        }

        public bool IsDirty
        {
            get { lock (gate) return dirty; }
        }

        public StatsStore(string dir)
        {
            dataDir = dir;
            Directory.CreateDirectory(dataDir);
            filePath = Path.Combine(dataDir, "stats.tsv");
            historyDir = Path.Combine(dataDir, "history");
            day = DateTime.Today;
            HasDayWheelField = true;
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
                FlushDayLocked(day);
                day = now;
                today = 0;
                todayWheel = 0;
                dirty = true;
            }
        }

        void FlushDayLocked(DateTime finishedDay)
        {
            if (finishedDay.Date == DateTime.Today) return;
            if (todayByKey.Count == 0) return;
            try
            {
                WriteDayFile(finishedDay, todayByKey, today);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
            todayByKey.Clear();
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
                long t;
                if (todayByKey.TryGetValue(id, out t)) todayByKey[id] = t + 1;
                else todayByKey[id] = 1;
                if (KeyCatalog.KindOf(id) == HitKind.Wheel) todayWheel++;
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

        public void GetSummary(out long totalCount, out long todayCount, out long mouseCount,
            out long wheelCount, out long todayWheelCount)
        {
            lock (gate)
            {
                RollDayLocked();

                long wheel = 0;
                long mouse = 0;
                foreach (KeyValuePair<string, long> kv in counts)
                {
                    HitKind kind = KeyCatalog.KindOf(kv.Key);
                    if (kind == HitKind.Wheel) { wheel += kv.Value; continue; }
                    if (kv.Key == "M_L" || kv.Key == "M_R") mouse += kv.Value;
                }

                totalCount = total - wheel;
                todayCount = today - todayWheel;
                mouseCount = mouse;
                wheelCount = wheel;
                todayWheelCount = todayWheel;
            }
        }

        public void GetTodaySummary(out long todayCount, out long todayMouseCount, out long todayWheelCount)
        {
            lock (gate)
            {
                RollDayLocked();

                long mouse = 0;
                foreach (KeyValuePair<string, long> kv in todayByKey)
                {
                    if (kv.Key == "M_L" || kv.Key == "M_R") mouse += kv.Value;
                }

                todayCount = today - todayWheel;
                todayMouseCount = mouse;
                todayWheelCount = todayWheel;
            }
        }

        public List<KeyStat> BuildTodayRows(bool includeKeyboard, bool includeMouse, bool includeWheel, string query)
        {
            lock (gate)
            {
                RollDayLocked();

                List<KeyStat> rows = new List<KeyStat>(todayByKey.Count);
                foreach (KeyValuePair<string, long> kv in todayByKey)
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

        public bool ExportTodayCsv(string path, out string error)
        {
            error = null;
            try
            {
                List<KeyStat> rows = BuildTodayRows(true, true, true, null);
                long todayCount;
                long todayMouse;
                long todayWheel;
                GetTodaySummary(out todayCount, out todayMouse, out todayWheel);

                StringBuilder sb = new StringBuilder(rows.Count * 32 + 96);
                sb.AppendLine("名次,按键,类型,次数,占比");
                int rank = 1;
                foreach (KeyStat s in rows)
                {
                    long baseTotal = s.Kind == HitKind.Wheel ? todayWheel : todayCount;
                    double pct = baseTotal > 0 ? (s.Count * 100.0 / baseTotal) : 0.0;
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
                todayByKey.Clear();
                total = 0;
                today = 0;
                todayWheel = 0;
                day = DateTime.Today;
                dirty = true;
                TryDeleteDayFile(DateTime.Today);
            }
        }

        public bool Save()
        {
            List<KeyValuePair<string, long>> snap;
            Dictionary<string, long> daySnapshot;
            DateTime snapshotDay;
            long tot;
            long td;
            long tdWheel;
            string dayText;
            lock (gate)
            {
                RollDayLocked();
                snap = new List<KeyValuePair<string, long>>(counts);
                daySnapshot = new Dictionary<string, long>(todayByKey);
                snapshotDay = day;
                tot = total;
                td = today;
                tdWheel = todayWheel;
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
            sb.Append("daywheel\t").Append(tdWheel.ToString(CultureInfo.InvariantCulture)).AppendLine();
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
                try
                {
                    WriteDayFile(snapshotDay, daySnapshot, td);
                }
                catch (Exception dayEx)
                {
                    LastError = dayEx.Message;
                }
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
                long wheel;
                long todayWheel;
                GetSummary(out tot, out td, out mouse, out wheel, out todayWheel);

                StringBuilder sb = new StringBuilder(rows.Count * 32 + 96);
                sb.AppendLine("名次,按键,类型,次数,占比");
                int rank = 1;
                foreach (KeyStat s in rows)
                {
                    long baseTotal = s.Kind == HitKind.Wheel ? wheel : tot;
                    double pct = baseTotal > 0 ? (s.Count * 100.0 / baseTotal) : 0.0;
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

        public string HistoryDir { get { return historyDir; } }

        string DayFilePath(DateTime target)
        {
            return Path.Combine(historyDir, target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tsv");
        }

        void TryDeleteDayFile(DateTime target)
        {
            try
            {
                string path = DayFilePath(target);
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        void WriteDayFile(DateTime target, Dictionary<string, long> map, long dayTotal)
        {
            if (map == null || map.Count == 0) return;
            if (dayTotal <= 0)
            {
                dayTotal = 0;
                foreach (KeyValuePair<string, long> kv in map) dayTotal += kv.Value;
            }

            string path = DayFilePath(target);
            if (target.Date != DateTime.Today && File.Exists(path)) return;

            List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>(map);
            list.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
            {
                return string.CompareOrdinal(a.Key, b.Key);
            });

            StringBuilder sb = new StringBuilder(list.Count * 10 + 48);
            sb.AppendLine("# KeyRank day v1");
            sb.Append("day\t").Append(target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("total\t").Append(dayTotal.ToString(CultureInfo.InvariantCulture)).AppendLine();
            foreach (KeyValuePair<string, long> kv in list)
            {
                if (kv.Value <= 0) continue;
                sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
            }

            Directory.CreateDirectory(historyDir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        Dictionary<string, long> ReadDayFile(DateTime target)
        {
            Dictionary<string, long> map = new Dictionary<string, long>();
            try
            {
                string path = DayFilePath(target);
                if (!File.Exists(path)) return map;

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    string key = line.Substring(0, tab).Trim();
                    string value = line.Substring(tab + 1).Trim();
                    if (key == "day" || key == "total") continue;
                    long n;
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n > 0)
                        map[key] = n;
                }
            }
            catch
            {
            }
            return map;
        }

        Dictionary<string, long> SnapshotFor(DateTime target, out long dayTotal)
        {
            lock (gate)
            {
                RollDayLocked();
                if (target.Date == day.Date)
                {
                    dayTotal = today;
                    return new Dictionary<string, long>(todayByKey);
                }
            }

            Dictionary<string, long> map = ReadDayFile(target);
            long sum = 0;
            foreach (KeyValuePair<string, long> kv in map) sum += kv.Value;
            dayTotal = sum;
            return map;
        }

        public List<DateTime> GetHistoryDays()
        {
            List<DateTime> days = new List<DateTime>();
            HashSet<DateTime> seen = new HashSet<DateTime>();
            try
            {
                if (Directory.Exists(historyDir))
                {
                    foreach (string file in Directory.GetFiles(historyDir, "*.tsv"))
                    {
                        DateTime parsed;
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out parsed))
                        {
                            if (seen.Add(parsed.Date)) days.Add(parsed.Date);
                        }
                    }
                }
            }
            catch
            {
            }
            if (seen.Add(DateTime.Today)) days.Add(DateTime.Today);
            days.Sort();
            return days;
        }

        public List<KeyStat> BuildDayRows(DateTime target, bool includeKeyboard, bool includeMouse, bool includeWheel)
        {
            long dayTotal;
            Dictionary<string, long> map = SnapshotFor(target, out dayTotal);

            List<KeyStat> rows = new List<KeyStat>(map.Count);
            foreach (KeyValuePair<string, long> kv in map)
            {
                if (kv.Value <= 0) continue;
                HitKind kind = KeyCatalog.KindOf(kv.Key);
                if (kind == HitKind.Mouse && !includeMouse) continue;
                if (kind == HitKind.Wheel && !includeWheel) continue;
                if (kind == HitKind.Keyboard && !includeKeyboard) continue;

                KeyStat s = new KeyStat();
                s.Id = kv.Key;
                s.Label = KeyCatalog.Label(kv.Key);
                s.Kind = kind;
                s.Count = kv.Value;
                rows.Add(s);
            }
            rows.Sort(CompareRows);
            return rows;
        }

        public long GetDayTotal(DateTime target)
        {
            return GetDayTotal(target, false);
        }

        public long GetDayTotal(DateTime target, bool includeWheel)
        {
            long dayTotal;
            Dictionary<string, long> map = SnapshotFor(target, out dayTotal);
            if (includeWheel) return dayTotal;

            long sum = 0;
            foreach (KeyValuePair<string, long> kv in map)
            {
                if (KeyCatalog.KindOf(kv.Key) == HitKind.Wheel) continue;
                sum += kv.Value;
            }
            return sum;
        }

        public bool DayHasData(DateTime target)
        {
            long dayTotal;
            Dictionary<string, long> map = SnapshotFor(target, out dayTotal);
            return map.Count > 0;
        }

        public bool ExportDayCsv(DateTime target, string path, out string error)
        {
            error = null;
            try
            {
                long dayTotal = GetDayTotal(target);
                long dayWheel = GetDayTotal(target, true) - dayTotal;
                List<KeyStat> rows = BuildDayRows(target, true, true, true);
                StringBuilder sb = new StringBuilder(rows.Count * 32 + 96);
                sb.AppendLine("名次,按键,类型,次数,占比");
                int rank = 1;
                foreach (KeyStat s in rows)
                {
                    long baseTotal = s.Kind == HitKind.Wheel ? dayWheel : dayTotal;
                    double pct = baseTotal > 0 ? (s.Count * 100.0 / baseTotal) : 0.0;
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

        void Load()
        {
            if (!File.Exists(filePath)) return;
            HasDayWheelField = false;

            try
            {
                string[] source = File.ReadAllLines(filePath, Encoding.UTF8);
                Dictionary<string, long> loaded = new Dictionary<string, long>();
                DateTime loadedDay = DateTime.Today;
                long loadedToday = 0;
                long loadedTodayWheel = 0;
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
                    else if (key == "daywheel")
                    {
                        HasDayWheelField = true;
                        long n;
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) loadedTodayWheel = n;
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

                Dictionary<string, long> restoredToday = null;
                if (loadedDay.Date == DateTime.Today && loadedToday > 0)
                    restoredToday = ReadDayFile(loadedDay);

                lock (gate)
                {
                    counts.Clear();
                    foreach (KeyValuePair<string, long> kv in loaded) counts[kv.Key] = kv.Value;
                    total = loadedTotal;
                    today = loadedToday;
                    todayWheel = loadedTodayWheel;
                    day = loadedDay;
                    todayByKey.Clear();
                    if (restoredToday != null)
                    {
                        foreach (KeyValuePair<string, long> kv in restoredToday) todayByKey[kv.Key] = kv.Value;
                    }
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
                    todayWheel = 0;
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
