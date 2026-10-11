using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Quiz.Models;
using TsuruLauncher.Quiz.Theme;

namespace TsuruLauncher.Quiz.Views
{
    public partial class ChoiceView : UserControl, IRefreshable
    {
        private readonly Border[] _cards;
        private readonly Border[] _hovers;
        private readonly Border[] _states;
        private readonly TextBlock[] _texts;

        private List<ChoiceQuestion> _list = new();
        private int _pos;
        private bool _answered;

        public ChoiceView()
        {
            InitializeComponent();

            _cards = new[] { Opt0, Opt1, Opt2, Opt3 };
            _hovers = new[] { OptHover0, OptHover1, OptHover2, OptHover3 };
            _states = new[] { OptState0, OptState1, OptState2, OptState3 };
            _texts = new[] { OptText0, OptText1, OptText2, OptText3 };

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _cards[i].MouseEnter += (_, _) => Fade(_hovers[idx], 1, Motion.Feedback);
                _cards[i].MouseLeave += (_, _) => Fade(_hovers[idx], 0, Motion.Feedback);
            }

            CategoryBox.Items.Add("全部章节");
            foreach (var c in Services.BankService.ChoiceCategories()) CategoryBox.Items.Add(c);
            CategoryBox.SelectedIndex = 0;

            BuildList();
        }

        public void Refresh() { }

        private void CategoryChanged(object sender, SelectionChangedEventArgs e) => BuildList();
        private void FilterChanged(object sender, RoutedEventArgs e) => BuildList();

        private void BuildList()
        {
            var cat = CategoryBox.SelectedItem as string;
            IEnumerable<ChoiceQuestion> src = Services.BankService.Choices;
            if (!string.IsNullOrEmpty(cat) && cat != "全部章节")
                src = src.Where(q => q.Category == cat);
            if (OnlyWrongCheck.IsChecked == true)
                src = src.Where(q => Services.ProgressService.IsWrong(q.Id));
            if (RealOnlyCheck.IsChecked == true)
                src = src.Where(q => Services.BankService.IsRealSource(q.Source));

            _list = src.ToList();
            if (ShuffleCheck.IsChecked == true) Shuffle(_list);
            _pos = 0;
            ShowQuestion();
        }

        private static void Shuffle<T>(IList<T> list)
        {
            var rnd = new Random();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private void ShowQuestion()
        {
            _answered = false;
            foreach (var s in _states)
            {
                s.Opacity = 0;
                s.Background = Brushes.Transparent;
            }
            ResultPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Opacity = 0;

            if (_list.Count == 0)
            {
                EmptyHint.Visibility = Visibility.Visible;
                StemCard.Visibility = Visibility.Collapsed;
                OptionsPanel.Visibility = Visibility.Collapsed;
                EmptyText.Text = OnlyWrongCheck.IsChecked == true
                    ? "当前筛选下没有题目。勾了「只看错题」的话，说明你还没有做错的题。"
                    : "这个章节还没有题目。";
                ProgressText.Text = "0 / 0";
                return;
            }

            EmptyHint.Visibility = Visibility.Collapsed;
            StemCard.Visibility = Visibility.Visible;
            OptionsPanel.Visibility = Visibility.Visible;

            var q = _list[_pos];
            CategoryTag.Text = q.Category;
            StemText.Text = q.Stem;
            if (string.IsNullOrWhiteSpace(q.Source))
            {
                SourceText.Visibility = Visibility.Collapsed;
            }
            else
            {
                SourceText.Visibility = Visibility.Visible;
                SourceText.Text = "来源：" + q.Source;
            }
            for (int i = 0; i < 4; i++)
            {
                bool has = i < q.Options.Count;
                _cards[i].Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                _texts[i].Text = has ? q.Options[i] : "";
            }
            ProgressText.Text = $"第 {_pos + 1} / {_list.Count} 题";
        }

        private void OptionClicked(object sender, MouseButtonEventArgs e)
        {
            if (_answered || _list.Count == 0) return;
            if (sender is Border b && int.TryParse(b.Tag?.ToString(), out int idx))
                Answer(idx);
        }

        private void Answer(int idx)
        {
            _answered = true;
            var q = _list[_pos];
            int correctIdx = ResolveAnswerIndex(q);
            bool ok = idx == correctIdx;

            if (correctIdx >= 0)
            {
                _states[correctIdx].Background = (Brush)FindResource("SuccessSoftBrush");
                Fade(_states[correctIdx], 1, Motion.Feedback);
            }
            if (!ok && idx >= 0)
            {
                _states[idx].Background = (Brush)FindResource("DangerSoftBrush");
                Fade(_states[idx], 1, Motion.Feedback);
            }

            ResultTitle.Text = ok ? "答对了" : "答错了";
            ResultTitle.Foreground = (Brush)FindResource(ok ? "SuccessBrush" : "DangerBrush");
            ResultAnswer.Text = "正确答案：" +
                (correctIdx >= 0 ? "ABCD"[correctIdx].ToString() : "未知（题库这题答案缺失）");
            ExplainText.Text = string.IsNullOrWhiteSpace(q.Explanation)
                ? "（这道题题库里没有收录解析）" : q.Explanation;

            ResultPanel.Visibility = Visibility.Visible;
            ResultPanel.RenderTransform = new TranslateTransform(0, 8);
            Fade(ResultPanel, 1, Motion.Page);
            ((TranslateTransform)ResultPanel.RenderTransform).BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, Motion.Page) { EasingFunction = Motion.Ease });

            Services.ProgressService.Record(q.Id, ok);
        }

        /// <summary>答案可能是字母，也可能直接写成了选项内容，两种都兼容</summary>
        private static int ResolveAnswerIndex(ChoiceQuestion q)
        {
            var a = (q.Answer ?? "").Trim();
            if (a.Length >= 1)
            {
                int i = "ABCD".IndexOf(char.ToUpperInvariant(a[0]));
                if (i >= 0) return i;
            }
            for (int i = 0; i < q.Options.Count; i++)
                if (q.Options[i].Contains(a, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private void PrevClicked(object sender, RoutedEventArgs e)
        {
            if (_pos > 0) { _pos--; ShowQuestion(); }
        }

        private void NextClicked(object sender, RoutedEventArgs e)
        {
            if (_list.Count == 0) return;
            if (_pos < _list.Count - 1) { _pos++; ShowQuestion(); }
            else BuildList();
        }

        private void RestartClicked(object sender, RoutedEventArgs e) => BuildList();

        private static void Fade(UIElement el, double to, Duration d) =>
            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, d) { EasingFunction = Motion.Ease });
    }
}
