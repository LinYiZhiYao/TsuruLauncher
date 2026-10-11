using System;
using System.Linq;

namespace TsuruLauncher.Quiz.Models
{
    public class ExamQuestion
    {
        /// <summary>choice = 选择题，code = 操作题</summary>
        public string Kind { get; set; } = "choice";
        /// <summary>公共基础 / Python 选择题 / 基本操作 / 简单应用 / 综合应用</summary>
        public string Section { get; set; } = "";
        public double Score { get; set; } = 1;

        public ChoiceQuestion? Choice { get; set; }
        public CodeQuestion? Code { get; set; }

        public int? ChosenIndex { get; set; }
        public string CodeText { get; set; } = "";
        /// <summary>操作题自评：0 不会 / 1 会一半 / 2 会做</summary>
        public int SelfRating { get; set; }
        /// <summary>跑了代码且输出与答案一致</summary>
        public bool? RunPassed { get; set; }

        public bool IsAnswered =>
            Kind == "choice" ? ChosenIndex != null : SelfRating > 0 || RunPassed == true;

        public int CorrectIndex => Choice?.AnswerIndex() ?? -1;

        public double Earned
        {
            get
            {
                if (Kind == "choice")
                {
                    if (ChosenIndex == null) return 0;
                    int ci = CorrectIndex;
                    return ci >= 0 && ChosenIndex == ci ? Score : 0;
                }
                if (RunPassed == true) return Score;
                return SelfRating switch
                {
                    2 => Score,
                    1 => Score / 2,
                    _ => 0,
                };
            }
        }
    }

    public class ExamPaper
    {
        public System.Collections.Generic.List<ExamQuestion> Questions { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(120);
        public bool Submitted { get; set; }

        public double MaxScore => Questions.Sum(q => q.Score);
        public double TotalScore => Questions.Sum(q => q.Earned);

        public double SectionScore(string section) =>
            Questions.Where(q => q.Section == section).Sum(q => q.Earned);

        public double SectionMax(string section) =>
            Questions.Where(q => q.Section == section).Sum(q => q.Score);

        public TimeSpan Remaining
        {
            get
            {
                var left = Duration - (DateTime.Now - StartedAt);
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }
    }
}
