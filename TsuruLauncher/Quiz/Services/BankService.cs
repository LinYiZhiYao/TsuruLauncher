using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TsuruLauncher.Quiz.Models;

namespace TsuruLauncher.Quiz.Services
{
    /// <summary>
    /// 题库加载。题库放在程序目录下的 Data 文件夹，用户可以直接替换 JSON 换题。
    /// </summary>
    public static class BankService
    {
        public static List<ChoiceQuestion> Choices { get; private set; } = new();
        public static List<CodeQuestion> Codes { get; private set; } = new();
        public static string StatusMessage { get; private set; } = "";

        private static readonly JsonSerializerOptions Opts = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        public static string DataDir
        {
            get
            {
                var baseDir = AppContext.BaseDirectory;

                // 页内模块的题库在 Quiz\Data（csproj 按 Content 复制过去）；
                // 独立 exe 那份在 Data\。两个都探一下，先命中先用。
                var local = Path.Combine(baseDir, "Quiz", "Data");
                if (Directory.Exists(local)) return local;

                var d = Path.Combine(baseDir, "Data");
                if (Directory.Exists(d)) return d;

                // 开发期：从 bin 往上找项目根
                var cur = new DirectoryInfo(baseDir);
                for (int i = 0; i < 6 && cur != null; i++)
                {
                    var cand = Path.Combine(cur.FullName, "Quiz", "Data");
                    if (Directory.Exists(cand)) return cand;
                    var cand2 = Path.Combine(cur.FullName, "Data");
                    if (Directory.Exists(cand2)) return cand2;
                    cur = cur.Parent;
                }
                return local;
            }
        }

        public static void Load()
        {
            var dir = DataDir;
            var choiceFile = Path.Combine(dir, "questions_choice.json");
            var codeFile = Path.Combine(dir, "questions_code.json");

            var msgs = new List<string>();
            if (File.Exists(choiceFile))
            {
                try { Choices = JsonSerializer.Deserialize<List<ChoiceQuestion>>(File.ReadAllText(choiceFile), Opts) ?? new(); }
                catch (Exception ex) { msgs.Add("选择题题库解析失败：" + ex.Message); }
            }
            else msgs.Add("未找到 questions_choice.json");

            if (File.Exists(codeFile))
            {
                try { Codes = JsonSerializer.Deserialize<List<CodeQuestion>>(File.ReadAllText(codeFile), Opts) ?? new(); }
                catch (Exception ex) { msgs.Add("操作题题库解析失败：" + ex.Message); }
            }
            else msgs.Add("未找到 questions_code.json");

            StatusMessage = msgs.Count == 0
                ? $"已载入 {Choices.Count} 道选择题 / {Codes.Count} 道操作题"
                : string.Join("；", msgs);
        }

        public static IEnumerable<string> ChoiceCategories()
        {
            var seen = new List<string>();
            foreach (var q in Choices)
                if (!seen.Contains(q.Category)) seen.Add(q.Category);
            seen.Sort();
            return seen;
        }

        public static IEnumerable<string> CodeCategories()
        {
            var seen = new List<string>();
            foreach (var q in Codes)
                if (!seen.Contains(q.Category)) seen.Add(q.Category);
            seen.Sort();
            return seen;
        }

        /// <summary>
        /// 是不是真题/官方样题。BUZAI946 是个人精编指南、内置示例题是占位，
        /// 这两类在「仅真题」模式下会被排除掉。
        /// </summary>
        public static bool IsRealSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            if (source.Contains("BUZAI946", StringComparison.OrdinalIgnoreCase)) return false;
            if (source.Contains("内置示例", StringComparison.Ordinal)) return false;
            return true;
        }

        public static int RealChoiceCount => Choices.Count(q => IsRealSource(q.Source));
        public static int RealCodeCount => Codes.Count(q => IsRealSource(q.Source));
    }
}
