using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TsuruLauncher.Quiz.Theme
{
    /// <summary>
    /// 真正的 cubic-bezier 缓动。WPF 内置的那几个 Ease 曲线和我们项目里用的
    /// cubic-bezier(0.22, 1, 0.36, 1) 对不上，所以自己实现一份。
    /// </summary>
    public sealed class CubicBezierEase : EasingFunctionBase
    {
        public double X1 { get; set; } = 0.22;
        public double Y1 { get; set; } = 1.0;
        public double X2 { get; set; } = 0.36;
        public double Y2 { get; set; } = 1.0;

        public CubicBezierEase() { }

        public CubicBezierEase(double x1, double y1, double x2, double y2)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }

        protected override Freezable CreateInstanceCore() => new CubicBezierEase(X1, Y1, X2, Y2);

        protected override double EaseInCore(double normalizedTime)
        {
            if (normalizedTime <= 0) return 0;
            if (normalizedTime >= 1) return 1;

            // 牛顿迭代解 x(u) = t，再取 y(u)
            double u = normalizedTime;
            for (int i = 0; i < 24; i++)
            {
                double err = Sample(u, X1, X2) - normalizedTime;
                if (Math.Abs(err) < 1e-6) break;
                double d = Slope(u, X1, X2);
                if (Math.Abs(d) < 1e-6) break;
                u -= err / d;
                if (u < 0) u = 0; else if (u > 1) u = 1;
            }
            return Sample(u, Y1, Y2);
        }

        private static double Sample(double t, double a, double b)
        {
            double mt = 1 - t;
            return 3 * mt * mt * t * a + 3 * mt * t * t * b + t * t * t;
        }

        private static double Slope(double t, double a, double b)
        {
            double mt = 1 - t;
            return 3 * mt * mt * a + 6 * mt * t * (b - a) + 3 * t * t * (1 - b);
        }
    }

    /// <summary>
    /// 动效时长与缓动，对齐 AxolotlMotion：短反馈 150ms、页面转场 260ms、侧栏 320ms。
    /// 任何新增 UI 元素的状态切换都必须走这里，不允许硬切。
    /// </summary>
    public static class Motion
    {
        public static readonly Duration Feedback = new(TimeSpan.FromMilliseconds(150));
        public static readonly Duration Page = new(TimeSpan.FromMilliseconds(260));
        public static readonly Duration Rail = new(TimeSpan.FromMilliseconds(320));
        public static readonly Duration Stagger = new(TimeSpan.FromMilliseconds(220));

        /// <summary>
        /// 侧栏滑块的位置过渡时长，取自 Axolotl 的 NavRail.vue：
        /// top / bottom / left / width 都是 150ms cubic-bezier(0.4, 0, 0.2, 1)。
        /// </summary>
        public static readonly Duration Slide = new(TimeSpan.FromMilliseconds(150));

        /// <summary>NavRail 的 STAGGER_DELAY = 120ms，用来做滑块先拉伸再位移的效果</summary>
        public static readonly TimeSpan StaggerDelay = TimeSpan.FromMilliseconds(120);

        public static CubicBezierEase Ease => new(0.22, 1.0, 0.36, 1.0);

        /// <summary>位置类动画用的曲线，对齐 NavRail 的 cubic-bezier(0.4, 0, 0.2, 1)</summary>
        public static CubicBezierEase SlideEase => new(0.4, 0.0, 0.2, 1.0);
    }
}
