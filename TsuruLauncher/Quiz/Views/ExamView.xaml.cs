using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Quiz.Models;
using TsuruLauncher.Quiz.Services;
using TsuruLauncher.Quiz.Theme;

namespace TsuruLauncher.Quiz.Views
{
    public partial class ExamView : UserControl, IRefreshable
    {
        private static readonly string[] SectionOrder =
            { "公共基础", "Python 选择题", "基本操作", "简单应用", "综合应用" };

        private ExamPaper? _paper;
        private int _cur;
        private readonly DispatcherTimer _timer = new();

        private TextBox? _codeBox;
        private TextBox? _outputBox;
        private TextBox? _stdinBox;
        private TextBlock? _judgeText;

        private readonly List<Button> _navButtons = new();
        private readonly List<SolidColorBrush> _navBrushes = new();
        private readonly List<SolidColorBrush> _navBorderBrushes = new();
        private readonly List<Border> _optionStates = new();
        private readonly List<Button> _ratingButtons = new();
        private readonly List<SolidColorBrush> _ratingBrushes = new();

        public ExamView()
        {
            InitializeComponent();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => Tick();
            WriteStructure();
        }

        public void Refresh() => WriteStructure();

        private void WriteStructure()
        {
            int pub = BankService.Choices.Count(q => q.Category == "公共基础");
            int py = BankService.Choices.Count(q => q.Category != "公共基础");
            int basic = BankService.Codes.Count(q => q.Category == "基本操作");
            int simple = BankService.Codes.Count(q => q.Category == "简单应用");
            int comp = BankService.Codes.Count(q => q.Category == "综合应用");

            StructureText.Text =
                $"选择题 40 题共 40 分：公共基础 10 题（题库 {pub} 道）+ Python 语言 30 题（题库 {py} 道）\n" +
                $"操作题 6 题共 60 分：基本操作 3 题 15 分（题库 {basic} 道）+ 简单应用 2 题 25 分（题库 {simple} 道）+ 综合应用 1 题 20 分（题库 {comp} 道）\n" +
                "满分 100 分，60 分及格，从题库随机抽题。";
        }

        private void StartClicked(object sender, RoutedEventArgs e)
        {
            _paper = ExamService.Generate();
            _cur = 0;
            BuildNav();
            ShowQuestion(0);
            SwitchTo(BriefPanel, RunPanel);
            _timer.Start();
            Tick();
        }

        private void Tick()
        {
            if (_paper == null) return;
            var r = _paper.Remaining;
            ClockText.Text = $"{(int)r.TotalMinutes:00}:{r.Seconds:00}";
            ClockText.Foreground = (Brush)FindResource(
                r.TotalMinutes < 10 ? "DangerBrush" : "TextBrush");
            ExamProgressText.Text = $"已答 {_paper.Questions.Count(q => q.IsAnswered)} / {_paper.Questions.Count}";
            if (r <= TimeSpan.Zero)
            {
                _timer.Stop();
                Submit(false);
            }
        }

        private void BuildNav()
        {
            QNavGrid.Children.Clear();
            _navButtons.Clear();
            _navBrushes.Clear();
            _navBorderBrushes.Clear();
            if (_paper == null) return;

            for (int i = 0; i < _paper.Questions.Count; i++)
            {
                int idx = i;
                var brush = new SolidColorBrush(Colors.Transparent);
                var b = new Button
                {
                    Content = (i + 1).ToString(),
                    Style = (Style)FindResource("SoftButton"),
                    Width = 38, Height = 30, Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 6), FontSize = 12,
                    Background = brush,
                    BorderBrush = new SolidColorBrush(((SolidColorBrush)FindResource("BorderBrush")).Color),
                };
                _navBrushes.Add(brush);
                _navBorderBrushes.Add((SolidColorBrush)b.BorderBrush);
                b.Click += (_, _) => { SaveCurrent(); _cur = idx; ShowQuestion(idx); };
                _navButtons.Add(b);
                QNavGrid.Children.Add(b);
            }
            UpdateNav();
        }

        private void UpdateNav()
        {
            if (_paper == null) return;
            var soft = ((SolidColorBrush)FindResource("AccentSoftBrush")).Color;
            for (int i = 0; i < _navButtons.Count; i++)
            {
                var q = _paper.Questions[i];
                var target = q.IsAnswered ? soft : Colors.Transparent;
                if (i == _cur)
                    target = q.IsAnswered ? soft : ((SolidColorBrush)FindResource("HoverBrush")).Color;
                AnimateBrush(_navBrushes[i], target);
                AnimateBrush(_navBorderBrushes[i], i == _cur
                    ? ((SolidColorBrush)FindResource("AccentBrush")).Color
                    : ((SolidColorBrush)FindResource("BorderBrush")).Color);
            }
            ExamProgressText.Text = $"已答 {_paper.Questions.Count(q => q.IsAnswered)} / {_paper.Questions.Count}";
        }

        private void ShowQuestion(int index)
        {
            if (_paper == null) return;
            QuestionArea.Children.Clear();
            _optionStates.Clear();
            _ratingButtons.Clear();
            _ratingBrushes.Clear();
            _codeBox = null;
            _outputBox = null;
            _stdinBox = null;
            _judgeText = null;

            var q = _paper.Questions[index];

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(MakeTag(q.Section, "AccentSoftBrush", "AccentBrush"));
            head.Children.Add(MakeTag($"第 {index + 1} 题", "SurfaceAltBrush", "TextMutedBrush"));
            head.Children.Add(MakeTag($"{q.Score:0.#} 分", "SurfaceAltBrush", "TextMutedBrush"));
            QuestionArea.Children.Add(head);

            var stemCard = new Border { Style = (Style)FindResource("CardStyle") };
            var stemText = new TextBlock
            {
                Text = q.Kind == "choice" ? (q.Choice?.Stem ?? "") : (q.Code?.Stem ?? ""),
                Style = (Style)FindResource("Body"),
                FontSize = 14,
                LineHeight = 24,
            };
            stemCard.Child = stemText;
            QuestionArea.Children.Add(stemCard);

            if (q.Kind == "choice") BuildChoiceBody(q);
            else BuildCodeBody(q);

            UpdateNav();
            FadeIn(QuestionArea, index);
        }

        private void BuildChoiceBody(ExamQuestion q)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var options = q.Choice?.Options ?? new List<string>();
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                var card = new Border { Style = (Style)FindResource("OptionCard") };
                var grid = new Grid();
                var hover = new Border
                {
                    Background = (Brush)FindResource("HoverBrush"),
                    CornerRadius = new CornerRadius(10), Opacity = 0,
                };
                var state = new Border { CornerRadius = new CornerRadius(10), Opacity = 0 };
                var inner = new Grid { Margin = new Thickness(14, 12, 14, 12) };
                inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                inner.ColumnDefinitions.Add(new ColumnDefinition());
                var circle = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                    Background = (Brush)FindResource("SurfaceAltBrush"),
                    BorderBrush = (Brush)FindResource("BorderStrongBrush"), BorderThickness = new Thickness(1),
                };
                circle.Child = new TextBlock
                {
                    Text = "ABCD"[i].ToString(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12, Foreground = (Brush)FindResource("TextMutedBrush"),
                };
                var text = new TextBlock
                {
                    Text = options[i],
                    Style = (Style)FindResource("Body"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                };
                Grid.SetColumn(circle, 0);
                Grid.SetColumn(text, 1);
                inner.Children.Add(circle);
                inner.Children.Add(text);
                grid.Children.Add(hover);
                grid.Children.Add(state);
                grid.Children.Add(inner);
                card.Child = grid;

                card.MouseEnter += (_, _) => Fade(hover, 1, Motion.Feedback);
                card.MouseLeave += (_, _) => Fade(hover, 0, Motion.Feedback);
                card.MouseLeftButtonUp += (_, _) =>
                {
                    q.ChosenIndex = idx;
                    foreach (var s in _optionStates) Fade(s, 0, Motion.Feedback);
                    state.Background = (Brush)FindResource("AccentSoftBrush");
                    Fade(state, 1, Motion.Feedback);
                    UpdateNav();
                };

                _optionStates.Add(state);
                panel.Children.Add(card);

                if (q.ChosenIndex == i)
                {
                    state.Background = (Brush)FindResource("AccentSoftBrush");
                    state.Opacity = 1;
                }
            }
            QuestionArea.Children.Add(panel);
        }

        private void BuildCodeBody(ExamQuestion q)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };

            var codeBox = new TextBox
            {
                Style = (Style)FindResource("MonoBox"),
                Text = q.CodeText,
                AcceptsReturn = true,
                AcceptsTab = true,
                MinHeight = 220,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            _codeBox = codeBox;
            panel.Children.Add(codeBox);

            panel.Children.Add(new TextBlock
            {
                Text = "程序输入（题目要求从键盘读数据时填这里）",
                Style = (Style)FindResource("Muted"),
                Margin = new Thickness(0, 10, 0, 0),
            });
            var stdinBox = new TextBox
            {
                Style = (Style)FindResource("MonoBox"),
                MinHeight = 30,
                Margin = new Thickness(0, 6, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _stdinBox = stdinBox;
            panel.Children.Add(stdinBox);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var runBtn = new Button
            {
                Content = "运行代码",
                Style = (Style)FindResource("PrimaryButton"),
                Padding = new Thickness(18, 8, 18, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            runBtn.Click += async (_, _) => await RunCode(q, runBtn);
            btnRow.Children.Add(runBtn);
            panel.Children.Add(btnRow);

            _judgeText = new TextBlock
            {
                Style = (Style)FindResource("Body"),
                Margin = new Thickness(0, 10, 0, 0),
            };
            panel.Children.Add(_judgeText);

            var output = new TextBox
            {
                Style = (Style)FindResource("MonoBox"),
                IsReadOnly = true,
                MinHeight = 90,
                MaxHeight = 180,
                Margin = new Thickness(0, 10, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            _outputBox = output;
            panel.Children.Add(output);

            var rateRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 0),
            };
            rateRow.Children.Add(new TextBlock
            {
                Text = "自评：",
                Style = (Style)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            string[] labels = { "不会", "会一半", "会做" };
            for (int i = 0; i < 3; i++)
            {
                int rating = i;
                var brush = new SolidColorBrush(Colors.Transparent);
                var b = new Button
                {
                    Content = labels[i],
                    Style = (Style)FindResource("SoftButton"),
                    Padding = new Thickness(16, 7, 16, 7),
                    Margin = new Thickness(0, 0, 8, 0),
                    Background = brush,
                };
                b.Click += (_, _) =>
                {
                    q.SelfRating = rating;
                    for (int k = 0; k < _ratingBrushes.Count; k++)
                        AnimateBrush(_ratingBrushes[k],
                            k == rating ? ((SolidColorBrush)FindResource("AccentSoftBrush")).Color : Colors.Transparent);
                    UpdateNav();
                };
                _ratingButtons.Add(b);
                _ratingBrushes.Add(brush);
                rateRow.Children.Add(b);
            }
            panel.Children.Add(rateRow);

            if (q.SelfRating > 0)
            {
                // 按钮下标和 SelfRating 语义一致：0 不会 / 1 会一半 / 2 会做
                for (int k = 0; k < _ratingBrushes.Count; k++)
                    _ratingBrushes[k].Color = k == q.SelfRating
                        ? ((SolidColorBrush)FindResource("AccentSoftBrush")).Color
                        : Colors.Transparent;
            }

            QuestionArea.Children.Add(panel);
        }

        private async Task RunCode(ExamQuestion q, Button btn)
        {
            if (_codeBox == null) return;
            var code = _codeBox.Text;
            q.CodeText = code;
            var stdin = _stdinBox?.Text;

            // 要读键盘又没给输入，直接提示，别干等到超时
            if (code.Contains("input(") && string.IsNullOrWhiteSpace(stdin))
            {
                if (_judgeText != null)
                {
                    _judgeText.Text = "这题要从键盘读数据，先在上面「程序输入」里填上再运行";
                    _judgeText.Foreground = (Brush)FindResource("WarnBrush");
                }
                return;
            }

            btn.IsEnabled = false;
            if (_judgeText != null) _judgeText.Text = "运行中…";

            var res = await Task.Run(() =>
                PythonRunner.Run(code, string.IsNullOrWhiteSpace(stdin) ? null : stdin));

            if (_outputBox != null)
            {
                var sb = new System.Text.StringBuilder();
                if (!string.IsNullOrWhiteSpace(res.Stdout)) sb.AppendLine(res.Stdout.TrimEnd());
                if (!string.IsNullOrWhiteSpace(res.Stderr)) sb.AppendLine("[stderr] " + res.Stderr.TrimEnd());
                if (!string.IsNullOrWhiteSpace(res.Error)) sb.AppendLine("[错误] " + res.Error);
                _outputBox.Text = sb.Length == 0 ? "（没有输出）" : sb.ToString().TrimEnd();
            }

            var expected = q.Code?.ExpectedOutput ?? "";
            if (!res.TimedOut && string.IsNullOrWhiteSpace(res.Error) && !string.IsNullOrWhiteSpace(expected))
            {
                bool ok = PythonRunner.OutputMatches(res.Stdout, expected);
                q.RunPassed = ok;
                if (_judgeText != null)
                {
                    _judgeText.Text = ok ? "输出与答案一致，这题算满分" : "输出与答案不一致，可以用自评给分";
                    _judgeText.Foreground = (Brush)FindResource(ok ? "SuccessBrush" : "DangerBrush");
                }
                if (!ok && _outputBox != null)
                    _outputBox.Text += "\n\n--- 期望输出 ---\n" + expected.TrimEnd();
            }
            else if (_judgeText != null)
            {
                _judgeText.Text = "题库这题没有标准输出，用下面的自评给分";
                _judgeText.Foreground = (Brush)FindResource("TextMutedBrush");
            }

            btn.IsEnabled = true;
            UpdateNav();
        }

        private void SaveCurrent()
        {
            if (_paper == null) return;
            var q = _paper.Questions[_cur];
            if (q.Kind == "code" && _codeBox != null) q.CodeText = _codeBox.Text;
        }

        private void SubmitClicked(object sender, RoutedEventArgs e) => Submit(true);

        private void Submit(bool ask)
        {
            if (_paper == null || _paper.Submitted) return;
            SaveCurrent();

            if (ask)
            {
                int left = _paper.Questions.Count(q => !q.IsAnswered);
                if (!QuizHost.Confirm("交卷",
                        left > 0 ? $"还有 {left} 题没作答，确定交卷吗？" : "确定交卷吗？",
                        "交卷", "再检查一下"))
                    return;
            }

            _timer.Stop();
            _paper.Submitted = true;

            // 没作答的题不计入 —— 否则交卷一次就会把整卷没做的题全塞进错题本
            foreach (var q in _paper.Questions)
            {
                if (!q.IsAnswered) continue;
                if (q.Kind == "choice" && q.Choice != null)
                    ProgressService.Record(q.Choice.Id, q.Earned > 0);
                else if (q.Kind == "code" && q.Code != null)
                    ProgressService.Record(q.Code.Id, q.Earned > 0);
            }

            ShowScore();
            SwitchTo(RunPanel, ScorePanel);
        }

        private void ShowScore()
        {
            if (_paper == null) return;
            double total = _paper.TotalScore;
            double max = _paper.MaxScore;
            bool pass = ExamService.IsPass(_paper);

            TotalScoreText.Text = $"{total:0.#} / {max:0.#}";
            TotalScoreText.Foreground = (Brush)FindResource(pass ? "SuccessBrush" : "DangerBrush");
            PassText.Text = pass ? "及格（60 分及格线）" : "未及格，还差一点";
            var used = DateTime.Now - _paper.StartedAt;
            UsedTimeText.Text = $"用时 {used.Hours} 小时 {used.Minutes} 分 {used.Seconds} 秒";

            SectionBars.Children.Clear();
            int i = 0;
            foreach (var sec in SectionOrder)
            {
                double got = _paper.SectionScore(sec);
                double m = _paper.SectionMax(sec);
                if (m <= 0) continue;
                SectionBars.Children.Add(BuildSectionRow(sec, got, m, i));
                i++;
            }

            ReviewArea.Children.Clear();
        }

        private Grid BuildSectionRow(string name, double got, double max, int index)
        {
            const double track = 240.0;
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(track + 12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var label = new TextBlock
            {
                Text = name,
                Style = (Style)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(label, 0);

            var trackB = new Border
            {
                Width = track, Height = 8,
                Background = (Brush)FindResource("SurfaceAltBrush"),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var fill = new Border
            {
                Width = 0, Height = 8,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = (Brush)FindResource("AccentBrush"),
            };
            trackB.Child = fill;
            Grid.SetColumn(trackB, 1);

            var txt = new TextBlock
            {
                Text = $"{got:0.#} / {max:0.#}",
                Style = (Style)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(txt, 2);

            grid.Children.Add(label);
            grid.Children.Add(trackB);
            grid.Children.Add(txt);

            var delay = TimeSpan.FromMilliseconds(index * 60);
            fill.BeginAnimation(FrameworkElement.WidthProperty,
                new DoubleAnimation(0, track * got / max, Motion.Stagger)
                { EasingFunction = Motion.Ease, BeginTime = delay });
            return grid;
        }

        private void ReviewClicked(object sender, RoutedEventArgs e)
        {
            if (_paper == null) return;
            ReviewArea.Children.Clear();
            int shown = 0;

            foreach (var q in _paper.Questions)
            {
                if (q.Earned > 0) continue;
                var card = new Border
                {
                    Style = (Style)FindResource("CardStyle"),
                    Margin = new Thickness(0, 0, 0, 12),
                    Opacity = 0,
                };
                card.RenderTransform = new TranslateTransform(0, 10);
                var sp = new StackPanel();

                sp.Children.Add(MakeTag(q.Section, "DangerSoftBrush", "DangerBrush"));
                sp.Children.Add(new TextBlock
                {
                    Text = (q.Kind == "choice" ? q.Choice?.Stem : q.Code?.Stem) ?? "",
                    Style = (Style)FindResource("Body"),
                    Margin = new Thickness(0, 8, 0, 0),
                    LineHeight = 22,
                });

                if (q.Kind == "choice" && q.Choice != null)
                {
                    int ci = q.CorrectIndex;
                    sp.Children.Add(new TextBlock
                    {
                        Text = ci >= 0 && ci < q.Choice.Options.Count
                            ? $"正确答案：{"ABCD"[ci]}　{q.Choice.Options[ci]}"
                            : "正确答案：题库缺失",
                        Style = (Style)FindResource("Body"),
                        Foreground = (Brush)FindResource("SuccessBrush"),
                        Margin = new Thickness(0, 8, 0, 0),
                    });
                    if (!string.IsNullOrWhiteSpace(q.Choice.Explanation))
                        sp.Children.Add(new TextBlock
                        {
                            Text = q.Choice.Explanation,
                            Style = (Style)FindResource("Body"),
                            Margin = new Thickness(0, 6, 0, 0),
                            LineHeight = 22,
                        });
                }
                else if (q.Code != null)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(q.Code.Reference)
                            ? "（题库没有收录参考答案）"
                            : "参考答案：\n" + q.Code.Reference,
                        Style = (Style)FindResource("MonoBlock"),
                        Margin = new Thickness(0, 8, 0, 0),
                    });
                }

                card.Child = sp;
                ReviewArea.Children.Add(card);

                var delay = TimeSpan.FromMilliseconds(Math.Min(shown, 12) * 35);
                card.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, Motion.Stagger) { EasingFunction = Motion.Ease, BeginTime = delay });
                ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, Motion.Stagger) { EasingFunction = Motion.Ease, BeginTime = delay });
                shown++;
            }

            if (shown == 0)
                ReviewArea.Children.Add(new TextBlock
                {
                    Text = "全对，没有错题。",
                    Style = (Style)FindResource("Muted"),
                });
        }

        private void RestartClicked(object sender, RoutedEventArgs e)
        {
            _paper = null;
            ReviewArea.Children.Clear();
            SwitchTo(ScorePanel, BriefPanel);
        }

        private void SwitchTo(Grid from, Grid to)
        {
            var fadeOut = new DoubleAnimation(1, 0, Motion.Feedback) { EasingFunction = Motion.Ease };
            fadeOut.Completed += (_, _) =>
            {
                from.Visibility = Visibility.Collapsed;
                to.Visibility = Visibility.Visible;
                to.RenderTransform = new TranslateTransform(0, 12);
                to.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, Motion.Page) { EasingFunction = Motion.Ease });
                ((TranslateTransform)to.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(12, 0, Motion.Page) { EasingFunction = Motion.Ease });
            };
            from.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void FadeIn(UIElement el, int seed)
        {
            el.RenderTransform = new TranslateTransform(0, 8);
            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Feedback) { EasingFunction = Motion.Ease });
            ((TranslateTransform)el.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, Motion.Feedback) { EasingFunction = Motion.Ease });
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
                Text = text, FontSize = 11,
                Foreground = (Brush)FindResource(fgKey),
            };
            return b;
        }

        private static void AnimateBrush(SolidColorBrush brush, Color to) =>
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(to, Motion.Feedback) { EasingFunction = Motion.Ease });

        private static void Fade(UIElement el, double to, Duration d) =>
            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, d) { EasingFunction = Motion.Ease });
    }
}
