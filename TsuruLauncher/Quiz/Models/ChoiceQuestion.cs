using System.Collections.Generic;

namespace TsuruLauncher.Quiz.Models
{
    public class ChoiceQuestion
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "未分类";
        public string Stem { get; set; } = "";
        public List<string> Options { get; set; } = new();
        /// <summary>正确答案，A/B/C/D</summary>
        public string Answer { get; set; } = "";
        public string Explanation { get; set; } = "";
        /// <summary>题目来源网址，用于溯源</summary>
        public string Source { get; set; } = "";

        /// <summary>
        /// 正确答案的选项下标。答案字段可能是字母（A/B/C/D），也可能被直接
        /// 写成了选项内容，两种都兼容；判断不出来返回 -1。
        /// </summary>
        public int AnswerIndex()
        {
            var a = (Answer ?? "").Trim();
            if (a.Length >= 1)
            {
                int i = "ABCD".IndexOf(char.ToUpperInvariant(a[0]));
                if (i >= 0) return i;
            }
            for (int i = 0; i < Options.Count; i++)
                if (Options[i].Contains(a, System.StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
