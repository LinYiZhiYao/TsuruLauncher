using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TsuruLauncher.Converters
{
    /// <summary>
    /// AI 工具状态点：把 AiToolState.StatusDot（"ok" | "missing" | "warn" | "checking"）
    /// 映射成主题里的画刷，全部走 TryFindResource，不硬编码颜色。
    ///
    /// <para>为什么不用 Style + DataTrigger + ColorAnimation：每个列表项各自实例化一个带
    /// Storyboard 的 Style，WPF 在 Seal() 这个 Style 时抛「无法冻结此 Storyboard 时间线树供
    /// 跨线程使用」——整块面板直接渲染失败。这里改由转换器给颜色，
    /// 过渡动画由 <see cref="AnimateOnStatusChange"/> 在属性变化时做 ColorAnimation。</para>
    /// </summary>
    public class AiStatusBrushConverter : IValueConverter
    {
        public static Color ColorFor(string? status)
        {
            string key = status switch
            {
                "ok" => "AccentBrush",
                "warn" => "WarningBrush",
                "missing" => "DangerBrush",
                _ => "TextDimBrush",
            };

            if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
                return b.Color;
            return Colors.Gray;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => new SolidColorBrush(ColorFor(value as string));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 状态点专用的过渡小工具：给 <see cref="Ellipse"/> 挂一个「StatusDot 变化 → Fill 用
    /// ColorAnimation 渐变到新颜色」的行为。不走 XAML Style/Storyboard，所以不受
    /// ItemTemplate 里 Style.Seal() 那个限制。
    /// </summary>
    public static class AiDotMotion
    {
        /// <summary>要观察的状态字符串（绑定 AiToolState.StatusDot）。</summary>
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.RegisterAttached("Status", typeof(string), typeof(AiDotMotion),
                new PropertyMetadata(null, OnStatusChanged));

        public static string? GetStatus(DependencyObject o) => (string?)o.GetValue(StatusProperty);
        public static void SetStatus(DependencyObject o, string? v) => o.SetValue(StatusProperty, v);

        private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Ellipse dot) return;

            Color to = AiStatusBrushConverter.ColorFor(e.NewValue as string);

            // 初值直接落定，避免刚进场时从灰跳一下
            if (e.OldValue == null)
            {
                dot.Fill = new SolidColorBrush(to);
                return;
            }

            // 每次动画用一个新的 SolidColorBrush，绝不改共享主题画刷
            var from = (dot.Fill as SolidColorBrush)?.Color ?? Colors.Transparent;
            var brush = new SolidColorBrush(from);
            dot.Fill = brush;

            var anim = new ColorAnimation(to, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }
}
