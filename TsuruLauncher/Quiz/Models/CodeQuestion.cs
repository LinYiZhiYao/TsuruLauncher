namespace TsuruLauncher.Quiz.Models
{
    public class CodeQuestion
    {
        public string Id { get; set; } = "";
        /// <summary>基本操作 / 简单应用 / 综合应用</summary>
        public string Category { get; set; } = "未分类";
        public string Stem { get; set; } = "";
        /// <summary>题目给出的代码框架，含填空标记</summary>
        public string Template { get; set; } = "";
        public string Reference { get; set; } = "";
        public string ExpectedOutput { get; set; } = "";
        public string Source { get; set; } = "";
    }
}
