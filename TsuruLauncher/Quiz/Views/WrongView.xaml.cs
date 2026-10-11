using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Quiz.Models;
using TsuruLauncher.Quiz.Services;
using TsuruLauncher.Quiz.Theme;

namespace TsuruLauncher.Quiz.Views
{
    public partial class WrongView : UserControl, IRefreshable
    {
        public WrongView()
        {
            InitializeComponent();
            Refresh();
        }

        public void Refresh()
        {
            List.Children.Clear();

            var wrongIds = ProgressService.Current.Stats.Values
                .Where(s => !s.LastCorrect)
                .Select(s => s.Id)
                .ToHashSet();

            var items = new List<(string Id, string Kind, string Category, string Stem, string AnswerText, string Explain)>();

            foreach (var q in BankService.Choices)
            {
                if (!wrongIds.Contains(q.Id)) continue;
                items.Add((q.Id, "选择题", q.Category, q.Stem, "正确答案：" + q.Answer, q.Explanation));
            }
            foreach (var q in BankService.Codes)
            {
                if (!wrongIds.Contains(q.Id)) continue;
                items.Add((q.Id, "操作题", q.Category, q.Stem, "参考答案见原题", ""));
            }

            SummaryText.Text = items.Count == 0
                ? "还没有错题。去做几道题，答错的会自动进到这里。"
                : $"共 {items.Count} 道待复习";

            for (int i = 0; i < items.Count; i++)
                List.Children.Add(BuildCard(items[i], i));
        }

        private Border BuildCard((string Id, string Kind, string Category, string Stem, string AnswerText, string Explain) it, int index)
        {
            var card = new Border
            {
                Style = (Style)FindResource("CardStyle"),
                Margin = new Thickness(0, 0, 0, 12),
                Opacity = 0,
            };
            card.RenderTransform = new TranslateTransform(0, 10);

            var root = new StackPanel();

            var tags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            tags.Children.Add(MakeTag(it.Kind, "AccentSoftBrush", "AccentBrush"));
            tags.Children.Add(MakeTag(it.Category, "SurfaceAltBrush", "TextMutedBrush"));
            root.Children.Add(tags);

            var stem = new TextBlock
            {
                Text = Trim(it.Stem, 160),
                Style = (Style)FindResource("Body"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 48,
            };
            root.Children.Add(stem);

            root.Children.Add(new TextBlock
            {
                Text = it.AnswerText,
                Style = (Style)FindResource("Body"),
                Foreground = (Brush)FindResource("SuccessBrush"),
                Margin = new Thickness(0, 8, 0, 0),
            });

            if (!string.IsNullOrWhiteSpace(it.Explain))
            {
                var box = new Border
                {
                    Background = (Brush)FindResource("WhiteBrush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 10, 0, 0),
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock { Text = "解析", Style = (Style)FindResource("Muted") });
                sp.Children.Add(new TextBlock
                {
                    Text = it.Explain,
                    Style = (Style)FindResource("Body"),
                    Margin = new Thickness(0, 6, 0, 0),
                });
                box.Child = sp;
                root.Children.Add(box);
            }

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var btn = new Button
            {
                Content = "标记已掌握",
                Style = (Style)FindResource("SoftButton"),
                Padding = new Thickness(14, 6, 14, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            btn.Click += (_, _) =>
            {
                ProgressService.Record(it.Id, true);
                var fade = new DoubleAnimation(1, 0, Motion.Feedback) { EasingFunction = Motion.Ease };
                fade.Completed += (_, _) => Refresh();
                card.BeginAnimation(UIElement.OpacityProperty, fade);
            };
            btnRow.Children.Add(btn);
            root.Children.Add(btnRow);

            card.Child = root;

            // 列表错峰入场
            var delay = TimeSpan.FromMilliseconds(Math.Min(index, 12) * 35);
            var fadeIn = new DoubleAnimation(0, 1, Motion.Stagger)
            {
                EasingFunction = Motion.Ease,
                BeginTime = delay,
            };
            var moveIn = new DoubleAnimation(10, 0, Motion.Stagger)
            {
                EasingFunction = Motion.Ease,
                BeginTime = delay,
            };
            card.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, moveIn);

            return card;
        }

        private Border MakeTag(string text, string bgKey, string fgKey)
        {
            var b = new Border
            {
                Background = (Brush)FindResource(bgKey),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 8, 0),
            };
            b.Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = (Brush)FindResource(fgKey),
            };
            return b;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= max ? s : s[..max] + "…";
        }

        private void ClearAllClicked(object sender, RoutedEventArgs e)
        {
            var count = ProgressService.WrongCount;
            if (count == 0) return;

            // 会改动用户的复习记录，先问一句
            if (!QuizHost.Confirm("清空错题本",
                    $"将把 {count} 道错题全部标记为「已掌握」，它们会从错题本里移除。答题统计不会被删除，只清空待复习列表。",
                    "确定", "取消"))
                return;

            foreach (var id in ProgressService.Current.Stats.Values.Where(s => !s.LastCorrect)
                         .Select(s => s.Id).ToList())
            {
                ProgressService.For(id).LastCorrect = true;
            }
            ProgressService.Save();
            Refresh();
        }
    }
}
