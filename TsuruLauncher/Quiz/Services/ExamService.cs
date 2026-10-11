using System;
using System.Collections.Generic;
using System.Linq;
using TsuruLauncher.Quiz.Models;

namespace TsuruLauncher.Quiz.Services
{
    /// <summary>
    /// 按真实考试的结构组卷：选择题 40 分（公共基础 10 + Python 30），
    /// 操作题 60 分（基本操作 3 题 15 分 + 简单应用 2 题 25 分 + 综合应用 1 题 20 分）。
    /// </summary>
    public static class ExamService
    {
        public static ExamPaper Generate()
        {
            var rnd = new Random();
            var paper = new ExamPaper { StartedAt = DateTime.Now };

            var pub = BankService.Choices.Where(q => q.Category == "公共基础").ToList();
            var py = BankService.Choices.Where(q => q.Category != "公共基础").ToList();

            foreach (var q in Take(pub, 10, rnd))
                paper.Questions.Add(Choice(q, "公共基础", 1));

            foreach (var q in Take(py, 30, rnd))
                paper.Questions.Add(Choice(q, "Python 选择题", 1));

            var basic = BankService.Codes.Where(q => q.Category == "基本操作").ToList();
            var simple = BankService.Codes.Where(q => q.Category == "简单应用").ToList();
            var comp = BankService.Codes.Where(q => q.Category == "综合应用").ToList();

            foreach (var q in Take(basic, 3, rnd))
                paper.Questions.Add(Code(q, "基本操作", 5));
            foreach (var q in Take(simple, 2, rnd))
                paper.Questions.Add(Code(q, "简单应用", 12.5));
            foreach (var q in Take(comp, 1, rnd))
                paper.Questions.Add(Code(q, "综合应用", 20));

            return paper;
        }

        private static ExamQuestion Choice(ChoiceQuestion q, string section, double score) =>
            new() { Kind = "choice", Section = section, Score = score, Choice = q };

        private static ExamQuestion Code(CodeQuestion q, string section, double score) =>
            new()
            {
                Kind = "code",
                Section = section,
                Score = score,
                Code = q,
                CodeText = string.IsNullOrWhiteSpace(q.Template) ? "# 在这里写代码\n" : q.Template,
            };

        private static List<T> Take<T>(List<T> src, int n, Random rnd) =>
            src.OrderBy(_ => rnd.Next()).Take(Math.Min(n, src.Count)).ToList();

        public static bool IsPass(ExamPaper p) => p.TotalScore >= 60;
    }
}
