using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TsuruLauncher.Quiz.Models;

namespace TsuruLauncher.Quiz.Services
{
    /// <summary>
    /// 答题进度持久化。存在用户目录下的 TsuruLauncher.Quiz\progress.json。
    /// </summary>
    public static class ProgressService
    {
        public static AppProgress Current { get; private set; } = new();

        private static string FilePath
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
                var dir = Path.Combine(root, "TsuruLauncher.Quiz");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "progress.json");
            }
        }

        public static void Load()
        {
            try
            {
                var p = FilePath;
                if (File.Exists(p))
                    Current = JsonSerializer.Deserialize<AppProgress>(File.ReadAllText(p)) ?? new AppProgress();
            }
            catch { Current = new AppProgress(); }
            Current.Stats ??= new Dictionary<string, QuestionStat>();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* 存不下就算了，不能因此崩掉 */ }
        }

        /// <summary>清空进度前先备份，返回备份文件路径</summary>
        public static string? Backup()
        {
            try
            {
                var p = FilePath;
                if (!File.Exists(p)) return null;
                var bak = p.Replace(".json", "") + ".backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.Copy(p, bak, true);
                return bak;
            }
            catch { return null; }
        }

        public static string StorePath => FilePath;

        public static void Reset()
        {
            Current = new AppProgress();
            Save();
        }

        public static QuestionStat For(string id)
        {
            if (!Current.Stats.TryGetValue(id, out var s))
            {
                s = new QuestionStat { Id = id };
                Current.Stats[id] = s;
            }
            return s;
        }

        public static void Record(string id, bool correct)
        {
            var s = For(id);
            s.Attempts++;
            if (!correct) s.WrongCount++;
            s.LastCorrect = correct;
            s.LastAt = DateTime.Now;
            Save();
        }

        public static bool IsWrong(string id) =>
            Current.Stats.TryGetValue(id, out var s) && !s.LastCorrect;

        public static int WrongCount
        {
            get
            {
                int n = 0;
                foreach (var s in Current.Stats.Values) if (!s.LastCorrect) n++;
                return n;
            }
        }

        public static int DoneCount => Current.Stats.Count;

        public static double Accuracy
        {
            get
            {
                int total = 0, right = 0;
                foreach (var s in Current.Stats.Values) { total += s.Attempts; right += s.Attempts - s.WrongCount; }
                return total == 0 ? 0 : right * 100.0 / total;
            }
        }
    }
}
