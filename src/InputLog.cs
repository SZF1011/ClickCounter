using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeyRank
{
    internal sealed class LogSegment
    {
        public DateTime Start;
        public DateTime End;
        public readonly List<string> Labels = new List<string>();

        public int Count { get { return Labels.Count; } }
    }

    internal sealed class InputLog
    {
        const int BucketMinutes = 5;

        readonly object gate = new object();
        readonly string dir;
        readonly bool readOnly;
        readonly StringBuilder pending = new StringBuilder();
        DateTime fileDay;
        StreamWriter writer;
        int pendingCount;
        string lastError;
        long totalRecorded;

        public string Dir { get { return dir; } }

        public string LastError
        {
            get { lock (gate) return lastError; }
        }

        public long TotalRecorded
        {
            get { lock (gate) return totalRecorded; }
        }

        public InputLog(string dataDir) : this(dataDir, false)
        {
        }

        public InputLog(string dataDir, bool readOnlyMode)
        {
            dir = Path.Combine(dataDir, "logs");
            Directory.CreateDirectory(dir);
            readOnly = readOnlyMode;
            lock (gate)
            {
                fileDay = DateTime.Today;
                if (!readOnly) OpenLocked(true);
            }
        }

        public void Record(string keyId, DateTime time)
        {
            if (readOnly) return;
            if (string.IsNullOrEmpty(keyId)) return;
            lock (gate)
            {
                if (time.Date != fileDay)
                {
                    FlushLocked();
                    CloseLocked();
                    fileDay = time.Date;
                    OpenLocked(true);
                }

                pending.Append(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
                pending.Append('\t').Append(keyId).Append('\n');
                pendingCount++;
                totalRecorded++;

                if (pendingCount >= 200) FlushLocked();
            }
        }

        public void Flush()
        {
            if (readOnly) return;
            lock (gate) FlushLocked();
        }

        public void Close()
        {
            if (readOnly) return;
            lock (gate)
            {
                FlushLocked();
                CloseLocked();
            }
        }

        void FlushLocked()
        {
            if (pending.Length == 0) return;
            try
            {
                if (writer == null) OpenLocked(true);
                writer.Write(pending.ToString());
                writer.Flush();
                pending.Length = 0;
                pendingCount = 0;
                lastError = null;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        void CloseLocked()
        {
            if (writer == null) return;
            try { writer.Flush(); } catch { }
            try { writer.Close(); } catch { }
            writer = null;
        }

        void OpenLocked(bool append)
        {
            string path = PathFor(fileDay);
            FileStream stream = new FileStream(path, append ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.AutoFlush = false;
            if (stream.Length == 0)
            {
                writer.WriteLine("# KeyRank event log v1");
                writer.WriteLine("# time\tkeyId");
                writer.Flush();
            }
        }

        string PathFor(DateTime day)
        {
            return Path.Combine(dir, "events-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tsv");
        }

        public long CountKind(DateTime day, HitKind kind)
        {
            long count = 0;
            try
            {
                Flush();
                string path = PathFor(day.Date);
                if (!File.Exists(path)) return 0;

                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        int tab = line.IndexOf('\t');
                        if (tab <= 0 || tab + 1 >= line.Length) continue;
                        string id = line.Substring(tab + 1).Trim();
                        if (id.Length == 0) continue;
                        if (KeyCatalog.KindOf(id) == kind) count++;
                    }
                }
            }
            catch
            {
            }
            return count;
        }

        public bool ExportBuckets(string path, out string error, out int rows)
        {
            error = null;
            rows = 0;
            try
            {
                Flush();

                string[] files;
                lock (gate) files = Directory.GetFiles(dir, "events-*.tsv");
                Array.Sort(files, StringComparer.Ordinal);

                SortedDictionary<DateTime, Dictionary<string, int>> buckets =
                    new SortedDictionary<DateTime, Dictionary<string, int>>();

                foreach (string file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!name.StartsWith("events-", StringComparison.Ordinal)) continue;
                    DateTime day;
                    if (!DateTime.TryParseExact(name.Substring(7), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out day)) continue;

                    List<string> lines = new List<string>();
                    try
                    {
                        using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null) lines.Add(line);
                        }
                    }
                    catch { continue; }

                    foreach (string raw in lines)
                    {
                        if (raw.Length == 0 || raw[0] == '#') continue;
                        int tab = raw.IndexOf('\t');
                        if (tab <= 0) continue;
                        DateTime clock;
                        if (!DateTime.TryParseExact(raw.Substring(0, tab), "HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out clock)) continue;
                        string id = raw.Substring(tab + 1).Trim();
                        if (id.Length == 0) continue;

                        DateTime stamp = day.Date.Add(clock.TimeOfDay);
                        DateTime bucket = new DateTime(stamp.Year, stamp.Month, stamp.Day,
                            stamp.Hour, stamp.Minute / BucketMinutes * BucketMinutes, 0);

                        Dictionary<string, int> map;
                        if (!buckets.TryGetValue(bucket, out map))
                        {
                            map = new Dictionary<string, int>();
                            buckets[bucket] = map;
                        }
                        int n;
                        if (map.TryGetValue(id, out n)) map[id] = n + 1;
                        else map[id] = 1;
                    }
                }

                StringBuilder sb = new StringBuilder(8192);
                sb.AppendLine("日期,开始时间,结束时间,按键,类型,次数");
                foreach (KeyValuePair<DateTime, Dictionary<string, int>> bucket in buckets)
                {
                    List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(bucket.Value);
                    list.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                    {
                        int c = b.Value.CompareTo(a.Value);
                        if (c != 0) return c;
                        return string.Compare(KeyCatalog.Label(a.Key), KeyCatalog.Label(b.Key), StringComparison.CurrentCulture);
                    });

                    foreach (KeyValuePair<string, int> item in list)
                    {
                        sb.Append(bucket.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(bucket.Key.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(bucket.Key.AddMinutes(BucketMinutes).ToString("HH:mm", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(Csv(KeyCatalog.Label(item.Key))).Append(',');
                        sb.Append(KeyCatalog.KindOf(item.Key) == HitKind.Mouse ? "鼠标" : "键盘").Append(',');
                        sb.Append(item.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
                        rows++;
                    }
                }

                return FileExport.WriteTextFile(path, sb.ToString(), dir, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public List<DateTime> GetAvailableDays()
        {
            List<DateTime> days = new List<DateTime>();
            string[] files;
            try { files = Directory.GetFiles(dir, "events-*.tsv"); }
            catch { return days; }

            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!name.StartsWith("events-", StringComparison.Ordinal)) continue;
                DateTime day;
                if (DateTime.TryParseExact(name.Substring(7), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day)) days.Add(day.Date);
            }
            days.Sort();
            return days;
        }

        public List<LogSegment> BuildSegments(DateTime day)
        {
            List<LogSegment> result = new List<LogSegment>();
            Flush();

            string path = PathFor(day.Date);
            if (!File.Exists(path)) return result;

            List<string> lines = new List<string>();
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                }
            }
            catch
            {
                return result;
            }

            SortedDictionary<DateTime, LogSegment> map = new SortedDictionary<DateTime, LogSegment>();
            foreach (string raw in lines)
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0) continue;

                DateTime clock;
                if (!DateTime.TryParseExact(raw.Substring(0, tab), "HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out clock)) continue;
                string id = raw.Substring(tab + 1).Trim();
                if (id.Length == 0) continue;

                DateTime stamp = day.Date.Add(clock.TimeOfDay);
                DateTime bucket = new DateTime(stamp.Year, stamp.Month, stamp.Day,
                    stamp.Hour, stamp.Minute / BucketMinutes * BucketMinutes, 0);
                LogSegment segment;
                if (!map.TryGetValue(bucket, out segment))
                {
                    segment = new LogSegment();
                    segment.Start = bucket;
                    segment.End = bucket.AddMinutes(BucketMinutes);
                    map[bucket] = segment;
                }
                segment.Labels.Add(KeyCatalog.Label(id));
            }

            result.AddRange(map.Values);
            return result;
        }

        public string BuildDayText(DateTime day)
        {
            List<LogSegment> segments = BuildSegments(day.Date);
            StringBuilder sb = new StringBuilder(8192);

            if (segments.Count == 0)
            {
                sb.AppendLine("这一天还没有按键记录");
                return sb.ToString();
            }

            sb.Append(day.Date.ToString("yyyy-MM-dd")).AppendLine();
            sb.AppendLine();
            foreach (LogSegment segment in segments)
            {
                sb.Append(segment.Start.ToString("HH:mm")).Append(" - ")
                  .Append(segment.End.ToString("HH:mm")).Append("　共 ")
                  .Append(segment.Count.ToString("N0")).AppendLine(" 次");
                sb.AppendLine(JoinLabels(segment.Labels));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        static string JoinLabels(List<string> labels)
        {
            StringBuilder sb = new StringBuilder(labels.Count * 3);
            foreach (string label in labels)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(label);
                if (sb.Length > 4000) break;
            }
            return sb.ToString();
        }

        public void Clear()
        {
            if (readOnly) return;
            lock (gate)
            {
                FlushLocked();
                CloseLocked();
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "events-*.tsv")) File.Delete(file);
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                pending.Length = 0;
                pendingCount = 0;
                totalRecorded = 0;
                fileDay = DateTime.Today;
                OpenLocked(true);
            }
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
