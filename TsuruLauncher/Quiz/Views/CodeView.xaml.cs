using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Quiz.Models;
using TsuruLauncher.Quiz.Services;
using TsuruLauncher.Quiz.Theme;

namespace TsuruLauncher.Quiz.Views
{
    public partial class CodeView : UserControl, IRefreshable
    {
        private List<CodeQuestion> _list = new();
        private int _pos;
        private bool _refShown;

        public CodeView()
        {
            InitializeComponent();

            CategoryBox.Items.Add("全部题型");
            foreach (var c in BankService.CodeCategories()) CategoryBox.Items.Add(c);
            CategoryBox.SelectedIndex = 0;

            var exe = PythonRunner.Detect();
            PythonHint.Text = exe == null
                ? "没找到 Python 解释器，运行功能不可用（可在设置里指定）"
                : "解释器：" + exe;

            BuildList();
        }

        public void Refresh() { }

        private void CategoryChanged(object sender, SelectionChangedEventArgs e) => BuildList();

        private void FilterChanged(object sender, RoutedEventArgs e) => BuildList();

        private void BuildList()
        {
            var cat = CategoryBox.SelectedItem as string;
            IEnumerable<CodeQuestion> src = BankService.Codes;
            if (!string.IsNullOrEmpty(cat) && cat != "全部题型")
                src = src.Where(q => q.Category == cat);
            if (RealOnlyCheck.IsChecked == true)
                src = src.Where(q => BankService.IsRealSource(q.Source));
            _list = src.ToList();
            _pos = 0;
            ShowQuestion();
        }

        private void ShowQuestion()
        {
            _refShown = false;
            ReferencePanel.Visibility = Visibility.Collapsed;
            ReferencePanel.Opacity = 0;
            OutputBox.Text = "";
            JudgeText.Text = "";

            if (_list.Count == 0)
            {
                StemText.Text = "这个题型还没有题目。";
                CategoryTag.Text = "无题";
                CodeBox.Text = "";
                ReferenceBox.Text = "";
                ProgressText.Text = "0 / 0";
                return;
            }

            var q = _list[_pos];
            CategoryTag.Text = q.Category;
            StemText.Text = q.Stem;
            CodeBox.Text = string.IsNullOrWhiteSpace(q.Template)
                ? "# 在这里写你的代码\n" : q.Template;
            ReferenceBox.Text = string.IsNullOrWhiteSpace(q.Reference)
                ? "（题库里没有收录这题的参考答案）" : q.Reference;
            ProgressText.Text = $"第 {_pos + 1} / {_list.Count} 题";
        }

        private async void RunClicked(object sender, RoutedEventArgs e)
        {
            if (_list.Count == 0) return;

            var code = CodeBox.Text;
            var stdin = StdinBox.Text;

            // 要读键盘又没给输入的话，直接提示，别让它干等到超时
            if (code.Contains("input(") && string.IsNullOrWhiteSpace(stdin))
            {
                JudgeText.Text = "这题要从键盘读数据，先在下面的「程序输入」里填上再运行";
                JudgeText.Foreground = (Brush)FindResource("WarnBrush");
                return;
            }

            RunBtn.IsEnabled = false;
            JudgeText.Text = "运行中…";

            var result = await Task.Run(() =>
                PythonRunner.Run(code, string.IsNullOrWhiteSpace(stdin) ? null : stdin));

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(result.Stdout)) sb.AppendLine(result.Stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.Stderr)) sb.AppendLine("[stderr] " + result.Stderr.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.Error)) sb.AppendLine("[错误] " + result.Error);
            OutputBox.Text = sb.Length == 0 ? "（没有输出）" : sb.ToString().TrimEnd();

            var q = _list[_pos];
            if (result.TimedOut || !string.IsNullOrWhiteSpace(result.Error))
            {
                JudgeText.Text = "运行失败";
                JudgeText.Foreground = (Brush)FindResource("DangerBrush");
            }
            else if (!string.IsNullOrWhiteSpace(q.ExpectedOutput))
            {
                bool ok = PythonRunner.OutputMatches(result.Stdout, q.ExpectedOutput);
                JudgeText.Text = ok ? "输出与答案一致" : "输出与答案不一致";
                JudgeText.Foreground = (Brush)FindResource(ok ? "SuccessBrush" : "DangerBrush");
                if (!ok)
                    OutputBox.Text += "\n\n--- 期望输出 ---\n" + q.ExpectedOutput.TrimEnd();
            }
            else
            {
                JudgeText.Text = result.Success ? "运行完成（对照参考答案自查）" : "有异常";
                JudgeText.Foreground = (Brush)FindResource(result.Success ? "TextMutedBrush" : "DangerBrush");
            }

            RunBtn.IsEnabled = true;
        }

        private void ToggleReference(object sender, RoutedEventArgs e)
        {
            _refShown = !_refShown;
            if (_refShown)
            {
                ReferencePanel.Visibility = Visibility.Visible;
                ReferencePanel.RenderTransform = new TranslateTransform(0, 8);
                ReferencePanel.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, Motion.Page) { EasingFunction = Motion.Ease });
                ((TranslateTransform)ReferencePanel.RenderTransform).BeginAnimation(
                    TranslateTransform.YProperty,
                    new DoubleAnimation(8, 0, Motion.Page) { EasingFunction = Motion.Ease });
            }
            else
            {
                var fade = new DoubleAnimation(1, 0, Motion.Feedback) { EasingFunction = Motion.Ease };
                fade.Completed += (_, _) => ReferencePanel.Visibility = Visibility.Collapsed;
                ReferencePanel.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private void MarkMastered(object sender, RoutedEventArgs e)
        {
            if (_list.Count == 0) return;
            ProgressService.Record(_list[_pos].Id, true);
            JudgeText.Text = "已标记为掌握";
            JudgeText.Foreground = (Brush)FindResource("SuccessBrush");
        }

        private void PrevClicked(object sender, RoutedEventArgs e)
        {
            if (_pos > 0) { _pos--; ShowQuestion(); }
        }

        private void NextClicked(object sender, RoutedEventArgs e)
        {
            if (_list.Count == 0) return;
            if (_pos < _list.Count - 1) { _pos++; ShowQuestion(); }
        }
    }
}
