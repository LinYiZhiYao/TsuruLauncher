using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Quiz.Theme;

namespace TsuruLauncher.Quiz.Views
{
    /// <summary>
    /// 刷题模块的宿主页：顶部「返回启动器」+ 子页导航，下面是原来的刷题页面。
    ///
    /// 从独立窗口搬过来时改了两件事：
    ///   1. 侧栏（竖排图标）换成顶部横向胶囊，省宽度
    ///   2. 「切子页 / 弹确认框」不再依赖 MainWindow，改走 <see cref="QuizHost"/>
    /// 指示块的水平滑动仍然是 NavRail 那一套：150ms cubic-bezier(0.4,0,0.2,1) + 120ms stagger。
    /// </summary>
    public partial class PythonQuizPage : UserControl
    {
        private static readonly string[] Titles = { "首页", "选择题", "操作题", "模拟套题", "错题本", "统计" };

        private readonly Action? _onBack;
        private readonly List<Button> _navButtons = new();
        private readonly List<TextBlock> _activeTexts = new();
        private readonly List<SolidColorBrush> _navBrushes = new();
        private readonly List<SolidColorBrush> _navBorderBrushes = new();
        private readonly List<Func<UserControl>> _factories = new();
        private readonly UserControl?[] _cache = new UserControl?[6];
        private UserControl? _current;
        private int _index = -1;

        public PythonQuizPage() : this(null) { }

        public PythonQuizPage(Action? onBack)
        {
            InitializeComponent();
            _onBack = onBack;

            // 题库和进度原来是刷题程序的 App.OnStartup 里加载的，
            // 移植进来之后由宿主页负责（重复调用是幂等的）
            Services.BankService.Load();
            Services.ProgressService.Load();

            // 挂上宿主能力：模块内部页面通过 QuizHost 调回来
            QuizHost.ConfirmImpl = ShowConfirm;
            QuizHost.NavigateImpl = Navigate;

            BuildNav();

            // 等布局完成才能量出每个胶囊的宽度，指示块才知道该滑到哪
            Loaded += (_, _) =>
            {
                // 调试用：TSURU_QUIZ_TAB=0..5 指定进入时停在哪一页（配合 --screenshot 自检）
                int start = 0;
                if (int.TryParse(Environment.GetEnvironmentVariable("TSURU_QUIZ_TAB"), out int tab)
                    && tab >= 0 && tab < Titles.Length)
                    start = tab;
                Navigate(start);
            };
        }

        private void BuildNav()
        {
            _factories.Add(() => new HomeView());
            _factories.Add(() => new ChoiceView());
            _factories.Add(() => new CodeView());
            _factories.Add(() => new ExamView());
            _factories.Add(() => new WrongView());
            _factories.Add(() => new StatsView());

            for (int i = 0; i < Titles.Length; i++)
            {
                int idx = i;

                // 文字两层交叉淡入：选中时白字浮上来（共享画刷不能直接做 Color 动画）
                var dark = new TextBlock
                {
                    Text = Titles[i],
                    FontSize = 12.5,
                    Foreground = (Brush)FindResource("TextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var light = new TextBlock
                {
                    Text = Titles[i],
                    FontSize = 12.5,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0,
                };
                var stack = new Grid();
                stack.Children.Add(dark);
                stack.Children.Add(light);
                _activeTexts.Add(light);

                var bg = new SolidColorBrush(Colors.Transparent);
                var bd = new SolidColorBrush(Colors.Transparent);
                var btn = new Button
                {
                    Content = stack,
                    Style = (Style)FindResource("NavPillButton"),
                    Height = 32,
                    Margin = new Thickness(0, 0, 6, 0),
                    Padding = new Thickness(16, 0, 16, 0),
                    Background = bg,
                    BorderBrush = bd,
                };
                btn.Click += (_, _) => Navigate(idx);

                _navButtons.Add(btn);
                _navBrushes.Add(bg);
                _navBorderBrushes.Add(bd);
                NavItems.Children.Add(btn);
            }
        }

        private void BackClicked(object sender, RoutedEventArgs e) => _onBack?.Invoke();

        public void Navigate(int index)
        {
            if (index < 0 || index >= _factories.Count) return;
            if (index == _index) return;

            var prev = _index;
            _index = index;

            AnimateIndicator(prev, index);

            for (int i = 0; i < _activeTexts.Count; i++)
                _activeTexts[i].BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(i == index ? 1 : 0, Motion.Feedback) { EasingFunction = Motion.Ease });

            var page = _cache[index] ??= _factories[index]();
            TransitionTo(page);
        }

        /// <summary>量出目标胶囊的位置和宽度（横向排列，逐个累加）。</summary>
        private (double x, double width) MeasureSlot(int index)
        {
            double x = 0;
            for (int i = 0; i < index && i < _navButtons.Count; i++)
                x += _navButtons[i].ActualWidth + _navButtons[i].Margin.Left + _navButtons[i].Margin.Right;

            var btn = _navButtons[index];
            x += btn.Margin.Left;
            return (x, Math.Max(24, btn.ActualWidth));
        }

        /// <summary>
        /// 复刻 NavRail 的滑块：位置 150ms cubic-bezier(0.4,0,0.2,1) + 120ms stagger ——
        /// 向右移动时右边缘先伸过去、左边缘再跟上；向左则相反。横向同理。
        /// </summary>
        private void AnimateIndicator(int prev, int next)
        {
            var (toX, width) = MeasureSlot(next);

            if (prev < 0)
            {
                IndicatorTransform.X = toX;
                NavIndicator.Width = width;
                return;
            }

            var (fromX, _) = MeasureSlot(prev);
            if (Math.Abs(fromX - toX) < 0.5 && Math.Abs(NavIndicator.Width - width) < 0.5) return;

            bool movingRight = toX > fromX;
            double stretch = Math.Abs(toX - fromX);
            double baseWidth = NavIndicator.Width > 0 ? NavIndicator.Width : width;

            IndicatorTransform.X = toX;
            NavIndicator.Width = width;

            var slide = Motion.SlideEase;
            var stagger = Motion.StaggerDelay;
            var after = stagger + TimeSpan.FromMilliseconds(150);

            var xAnim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(fromX, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if (movingRight)
            {
                xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(fromX, KeyTime.FromTimeSpan(stagger)));
                xAnim.KeyFrames.Add(new EasingDoubleKeyFrame(toX, KeyTime.FromTimeSpan(after)) { EasingFunction = slide });
            }
            else
            {
                xAnim.KeyFrames.Add(new EasingDoubleKeyFrame(toX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))) { EasingFunction = slide });
                xAnim.KeyFrames.Add(new LinearDoubleKeyFrame(toX, KeyTime.FromTimeSpan(after)));
            }
            IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, xAnim);

            var wAnim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            wAnim.KeyFrames.Add(new LinearDoubleKeyFrame(baseWidth, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            wAnim.KeyFrames.Add(new EasingDoubleKeyFrame(baseWidth + stretch, KeyTime.FromTimeSpan(stagger)) { EasingFunction = slide });
            wAnim.KeyFrames.Add(new EasingDoubleKeyFrame(width, KeyTime.FromTimeSpan(after)) { EasingFunction = slide });
            NavIndicator.BeginAnimation(FrameworkElement.WidthProperty, wAnim);
        }

        private void TransitionTo(UserControl page)
        {
            if (_current == page) return;

            var old = _current;
            if (old == null)
            {
                EnterPage(page);
                return;
            }

            old.IsHitTestVisible = false;
            var fadeOut = new DoubleAnimation(1, 0, Motion.Feedback) { EasingFunction = Motion.Ease };
            fadeOut.Completed += (_, _) =>
            {
                PageHost.Children.Remove(old);
                old.IsHitTestVisible = true;
                EnterPage(page);
            };
            old.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            if (old.RenderTransform is TranslateTransform tf)
                tf.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -8, Motion.Feedback) { EasingFunction = Motion.Ease });
        }

        private void EnterPage(UserControl page)
        {
            if (page.Parent is Panel p) p.Children.Remove(page);

            page.Opacity = 0;
            page.RenderTransform = new TranslateTransform(0, 12);
            PageHost.Children.Add(page);
            _current = page;

            page.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Page) { EasingFunction = Motion.Ease });
            ((TranslateTransform)page.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, Motion.Page) { EasingFunction = Motion.Ease });

            if (page is IRefreshable r) r.Refresh();
        }

        // ─────────────── 确认框（替代系统 MessageBox，与模块内其它界面同风格）───────────────

        private bool ShowConfirm(string title, string message, string yesText, string noText)
        {
            DialogTitle.Text = title;
            DialogMessage.Text = message;
            DialogYes.Content = yesText;
            DialogNo.Content = noText;

            OverlayHost.Visibility = Visibility.Visible;
            Scrim.Opacity = 0;
            DialogCard.Opacity = 0;
            DialogShift.Y = 10;

            Scrim.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Feedback) { EasingFunction = Motion.Ease });
            DialogCard.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Motion.Page) { EasingFunction = Motion.Ease });
            DialogShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, Motion.Page) { EasingFunction = Motion.Ease });

            bool result = false;
            var frame = new DispatcherFrame();
            RoutedEventHandler onYes = (_, _) => { result = true; frame.Continue = false; };
            RoutedEventHandler onNo = (_, _) => { result = false; frame.Continue = false; };
            DialogYes.Click += onYes;
            DialogNo.Click += onNo;
            Dispatcher.PushFrame(frame);
            DialogYes.Click -= onYes;
            DialogNo.Click -= onNo;

            var fade = new DoubleAnimation(1, 0, Motion.Feedback) { EasingFunction = Motion.Ease };
            fade.Completed += (_, _) => OverlayHost.Visibility = Visibility.Collapsed;
            DialogCard.BeginAnimation(UIElement.OpacityProperty, fade);
            Scrim.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, 0, Motion.Feedback) { EasingFunction = Motion.Ease });

            return result;
        }
    }
}
