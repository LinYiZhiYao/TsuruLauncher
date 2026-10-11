using System;
using System.Collections.Generic;

namespace TsuruLauncher.Quiz.Models
{
    public class QuestionStat
    {
        public string Id { get; set; } = "";
        public int Attempts { get; set; }
        public int WrongCount { get; set; }
        public bool LastCorrect { get; set; }
        public DateTime LastAt { get; set; }
    }

    public class AppProgress
    {
        public Dictionary<string, QuestionStat> Stats { get; set; } = new();
        /// <summary>选择题当前练习到第几题（按当前筛选下的索引记不牢，记题号更稳）</summary>
        public string? LastChoiceId { get; set; }
        public string? LastCodeId { get; set; }
    }
}
