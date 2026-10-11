using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Quiz.Services;
using TsuruLauncher.Quiz.Theme;

namespace TsuruLauncher.Quiz.Views
{
    public partial class StatsView : UserControl, IRefreshable
    {
        private const double TrackWidth = 260.0;

        public StatsView()
        {
            InitializeComponent();
            Refresh();
        }

        public void Refresh()
        {
            StatDone.Text = ProgressService.DoneCount.ToString();
            StatAcc.Text = ProgressService.Accuracy.ToString("0.0") + "%";
            StatWrong.Text = ProgressService.WrongCount.ToString();
            StatBank.Text = (BankService.Choices.Count + BankService.Codes.Count).ToString();
            StorePathText.Text = "进度文件：" + ProgressService.StorePath;

            Bars.Children.Clear();
            var groups = new Dictionary<string, (int total, int wrong)>();

            foreach (var q in BankService.Choices)
            {
                if (!ProgressService.Current.Stats.TryGetValue(q.Id, out var s)) continue;
                if (!groups.TryGetValue(q.Category, out var g)) g = (0, 0);
                groups[q.Category] = (g.total + s.Attempts, g.wrong + s.WrongCount);
            }

            if (groups.Count == 0)
            {
                Bars.Children.Add(new TextBlock
                {
                    Text = "还没有答题记录，去刷几道题再回来看。",
                    Style = (Style)FindResource("Muted"),
                });
                return;
            }

            int i = 0;
            foreach (var kv in groups.OrderByDescending(g => g.Value.total))
            {
                Bars.Children.Add(BuildRow(kv.Key, kv.Value.total, kv.Value.wrong, i));
                i++;
            }
        }

        private Grid BuildRow(string category, int total, int wrong, int index)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrackWidth + 12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var name = new TextBlock
            {
                Text = category,
                Style = (Style)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(name, 0);

            double acc = total == 0 ? 0 : (total - wrong) * 100.0 / total;

            var track = new Border
            {
                Width = TrackWidth,
                Height = 8,
                Background = (Brush)FindResource("SurfaceAltBrush"),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var fill = new Border
            {
                Width = 0,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)FindResource(acc >= 70 ? "AccentBrush" : acc >= 50 ? "WarnBrush" : "DangerBrush"),
            };
            track.Child = fill;
            Grid.SetColumn(track, 1);

            var pct = new TextBlock
            {
                Text = $"{acc:0.0}%（{total} 次作答）",
                Style = (Style)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(pct, 2);

            grid.Children.Add(name);
            grid.Children.Add(track);
            grid.Children.Add(pct);

            var delay = TimeSpan.FromMilliseconds(Math.Min(index, 10) * 40);
            fill.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(0, TrackWidth * acc / 100.0, Motion.Stagger)
                {
                    EasingFunction = Motion.Ease,
                    BeginTime = delay,
                });
            grid.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Stagger) { EasingFunction = Motion.Ease, BeginTime = delay });

            return grid;
        }

        private void ResetClicked(object sender, RoutedEventArgs e)
        {
            if (!QuizHost.Confirm("清空答题进度",
                    "这会清空全部答题记录和错题本，之后无法用程序恢复。我会先自动备份一份，确定要清空吗？",
                    "清空", "取消"))
                return;

            var bak = ProgressService.Backup();
            ProgressService.Reset();
            Refresh();
            BackupText.Text = bak == null
                ? "已清空（原本没有进度文件，无需备份）"
                : "已清空。备份在：" + bak;
        }
    }
}
