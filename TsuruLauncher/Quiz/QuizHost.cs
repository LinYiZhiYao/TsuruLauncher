using System;

namespace TsuruLauncher.Quiz
{
    /// <summary>
    /// 刷题模块和宿主页面之间的桥。
    ///
    /// 模块内部的页面（HomeView / ExamView 等）原本依赖刷题程序自己的 MainWindow，
    /// 嵌进启动器之后不能再依赖具体宿主，所以「切子页」「弹确认框」都改成走这里。
    /// 由 <c>Views.PythonQuizPage</c> 在构造时挂上实现。
    /// </summary>
    public static class QuizHost
    {
        internal static Func<string, string, string, string, bool>? ConfirmImpl;
        internal static Action<int>? NavigateImpl;

        /// <summary>确认框。没挂实现时（比如单测）直接放行，不要卡住。</summary>
        public static bool Confirm(string title, string message,
            string yesText = "确定", string noText = "取消")
            => ConfirmImpl?.Invoke(title, message, yesText, noText) ?? true;

        /// <summary>切到某个子页（0 首页 / 1 选择题 / 2 操作题 / 3 模拟考试 / 4 错题本 / 5 统计）。</summary>
        public static void Navigate(int index) => NavigateImpl?.Invoke(index);
    }
}
