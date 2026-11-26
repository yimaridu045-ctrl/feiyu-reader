// 肥鱼阅读器 v2 · 阅读历史
// 记录最近打开的文件及其阅读位置，下次直接点开接着读。
// 存储格式：每行 TSV——时间戳 \t 位置 \t 文件大小 \t 类型 \t 路径
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TxtReader
{
    internal sealed class HistoryEntry
    {
        public string Path;
        public long Size;
        public string Kind;        // text / image
        public long Position;      // 文本为字符索引，图片恒为 0
        public DateTime OpenedAt;

        public string DisplayName
        {
            get
            {
                string name = System.IO.Path.GetFileName(Path);
                return string.IsNullOrEmpty(name) ? Path : name;
            }
        }
    }

    internal static class HistoryStore
    {
        private const int MaxEntries = 60;

        /// <summary>测试用：覆盖存储目录（正常运行时为 null，写到 %APPDATA%\肥鱼阅读器）。</summary>
        internal static string OverrideDir;

        private static string FilePath
        {
            get
            {
                string dir = Environment.GetEnvironmentVariable("FY_HISTORY_DIR");
                if (string.IsNullOrEmpty(dir)) dir = OverrideDir;
                if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "肥鱼阅读器");
                Directory.CreateDirectory(dir);
                return System.IO.Path.Combine(dir, "history.tsv");
            }
        }

        public static List<HistoryEntry> Load()
        {
            var list = new List<HistoryEntry>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var parts = raw.Split('\t');
                    if (parts.Length < 5) continue;

                    long ts, size, pos;
                    long.TryParse(parts[0], out ts);
                    long.TryParse(parts[2], out size);
                    long.TryParse(parts[1], out pos);

                    string path = parts[4];
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!File.Exists(path)) continue;    // 文件已删除的不再展示

                    list.Add(new HistoryEntry
                    {
                        OpenedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ts).ToLocalTime(),
                        Position = pos,
                        Size = size,
                        Kind = parts[3],
                        Path = path
                    });
                }
            }
            catch { }

            list.Sort((a, b) => b.OpenedAt.CompareTo(a.OpenedAt));
            return list;
        }

        public static void Save(List<HistoryEntry> entries)
        {
            try
            {
                var sb = new StringBuilder();
                int count = 0;
                foreach (var e in entries)
                {
                    if (count++ >= MaxEntries) break;
                    var ts = (long)(e.OpenedAt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                    sb.Append(ts).Append('\t')
                      .Append(e.Position).Append('\t')
                      .Append(e.Size).Append('\t')
                      .Append(e.Kind ?? "text").Append('\t')
                      .Append(e.Path).Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>打开文件时调用：已存在则更新位置与时间，并置顶。</summary>
        public static void Touch(List<HistoryEntry> entries, string path, long size, string kind, long position)
        {
            if (string.IsNullOrEmpty(path)) return;
            HistoryEntry found = null;
            foreach (var e in entries)
            {
                if (string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)) { found = e; break; }
            }
            if (found == null)
            {
                found = new HistoryEntry { Path = path };
                entries.Add(found);
            }
            found.Size = size;
            found.Kind = kind;
            found.Position = position < 0 ? 0 : position;
            found.OpenedAt = DateTime.Now;

            entries.Sort((a, b) => b.OpenedAt.CompareTo(a.OpenedAt));
        }

        public static void Forget(List<HistoryEntry> entries, string path)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (string.Equals(entries[i].Path, path, StringComparison.OrdinalIgnoreCase))
                    entries.RemoveAt(i);
        }
    }
}
