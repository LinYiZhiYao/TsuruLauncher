using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 把 Markdown 渲染成原生 WPF 元素（不是 HTML / 不是 WebView）。
    ///
    /// <para>背景：资源详情页的描述原来直接 <c>TextBlock Text="{Binding Resource.Description}"</c>，
    /// Modrinth 返回的是 Markdown + 内嵌 HTML，于是界面上原样显示
    /// <c>![](https://...)</c>、<c>---</c>、<c>&lt;center&gt;</c>、<c>&lt;img src=...&gt;</c> —— 看着像坏了。</para>
    ///
    /// <para>为什么不用 WebView2 显示 HTML：那会给一个纯文本区块挂一个浏览器实例，
    /// 内存和首帧成本都不划算，而且滚动会和外层 ScrollViewer 打架。
    /// 这里用 Markdig 解析成 AST，再逐块生成 TextBlock / Image / Border，
    /// 所有颜色都用 <c>SetResourceReference</c> 绑定主题画刷，深浅色自动跟随。</para>
    /// </summary>
    public static class MarkdownView
    {
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseEmphasisExtras().UseAutoLinks().Build();

        private static readonly FontFamily Mono = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI");

        #region 附加属性（给 XAML 用）

        public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(MarkdownView), new PropertyMetadata(null, OnTextChanged));

        public static string? GetText(DependencyObject o) => (string?)o.GetValue(TextProperty);
        public static void SetText(DependencyObject o, string? v) => o.SetValue(TextProperty, v);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ContentControl host) return;
            try
            {
                host.Content = Build(e.NewValue as string);
            }
            catch
            {
                // 渲染失败也要有降级：退回纯文本，绝不能空白
                var fallback = new TextBlock { Text = e.NewValue as string ?? "", TextWrapping = TextWrapping.Wrap };
                fallback.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                host.Content = fallback;
            }
        }

        #endregion

        /// <summary>把一段 Markdown 渲染成可放进任意容器的 StackPanel。</summary>
        public static FrameworkElement Build(string? markdown)
        {
            var root = new StackPanel();
            if (string.IsNullOrWhiteSpace(markdown)) return root;

            MarkdownDocument doc;
            try { doc = Markdown.Parse(Normalize(markdown!), Pipeline); }
            catch { doc = Markdown.Parse(markdown!, new MarkdownPipelineBuilder().Build()); }

            RenderBlocks(root, doc, 0);
            return root;
        }

        /// <summary>
        /// 预处理：Modrinth 的描述里混着 <c>&lt;center&gt;</c>、<c>&lt;img src&gt;</c>、<c>&lt;br&gt;</c>，
        /// Markdig 会把它们当 HTML 块原样保留。这里把能表达成 Markdown 的转掉，
        /// 其余标签剥掉，再解 HTML 实体（<c>&amp;nbsp;</c> 之类）。
        /// </summary>
        private static string Normalize(string s)
        {
            // <img src="x"> → Markdown 图片
            s = Regex.Replace(s, @"<img[^>]*?\bsrc\s*=\s*[""']([^""']+)[""'][^>]*>",
                m => "\n\n![](" + m.Groups[1].Value + ")\n\n", RegexOptions.IgnoreCase);

            // <br> → 换行；块级闭合标签 → 空行
            s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</(p|div|center|section|article|h[1-6]|li|tr|table)\s*>", "\n\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<hr\s*/?>", "\n\n---\n\n", RegexOptions.IgnoreCase);

            // 剩下的标签直接剥掉（Markdig 的 HtmlInline 也会被忽略，这里是双保险）
            s = Regex.Replace(s, @"<[^>]+>", "");

            try { s = WebUtility.HtmlDecode(s); } catch { }
            return s;
        }

        private static void RenderBlocks(Panel host, ContainerBlock blocks, int depth)
        {
            foreach (var block in blocks)
            {
                var el = RenderBlock(block, depth);
                if (el != null) host.Children.Add(el);
            }
        }

        // ⚠ 必须写全 Markdig.Syntax.Block：System.Windows.Documents.Block 同名，
        //    两个 using 都在时会 CS0104 二义。
        private static FrameworkElement? RenderBlock(Markdig.Syntax.Block block, int depth)
        {
            switch (block)
            {
                case HeadingBlock h:
                {
                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.Bold,
                        FontSize = h.Level switch { 1 => 22, 2 => 18, 3 => 16, 4 => 14.5, 5 => 13.5, _ => 13 },
                        Margin = new Thickness(0, depth == 0 ? 6 : 4, 0, 8)
                    };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    if (h.Inline != null) AddInlines(tb.Inlines, h.Inline);
                    return tb;
                }

                case ParagraphBlock p:
                {
                    // 整段只有一张图 → 直接出 Image，而不是把它塞进文字里
                    var only = OnlyImage(p.Inline);
                    if (only != null) return BuildImage(only);

                    var tb = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 22,
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    if (p.Inline != null) AddInlines(tb.Inlines, p.Inline);
                    return tb;
                }

                case FencedCodeBlock fenced:
                    return BuildCode(fenced.Lines.ToString());

                case CodeBlock indented:
                    return BuildCode(indented.Lines.ToString());

                case ListBlock list:
                    return BuildList(list, depth);

                case ThematicBreakBlock:
                {
                    var hr = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 14) };
                    hr.SetResourceReference(Border.BackgroundProperty, "GlassBorderBrush");
                    return hr;
                }

                case QuoteBlock quote:
                {
                    var bar = new Border
                    {
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(12, 2, 0, 2),
                        Margin = new Thickness(0, 0, 0, 10)
                    };
                    bar.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                    var inner = new StackPanel();
                    bar.Child = inner;
                    RenderBlocks(inner, quote, depth + 1);
                    return bar;
                }

                case HtmlBlock:
                    return null;   // 已在 Normalize 里处理

                default:
                    return null;
            }
        }

        private static FrameworkElement BuildCode(string code)
        {
            var box = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 2, 0, 12)
            };
            box.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");

            var tb = new TextBlock
            {
                Text = code.TrimEnd('\r', '\n'),
                FontFamily = Mono,
                FontSize = 12.5,
                TextWrapping = TextWrapping.NoWrap,
                LineHeight = 19
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            box.Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = tb
            };
            return box;
        }

        private static FrameworkElement BuildList(ListBlock list, int depth)
        {
            var host = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            int index = 1;

            foreach (var item in list)
            {
                if (item is not ListItemBlock li) continue;

                // 序号 / 圆点单独一列，正文另起一列 → 换行时对齐
                var row = new Grid { Margin = new Thickness(depth * 14, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var bullet = new TextBlock
                {
                    Text = list.IsOrdered ? (index + ".") : "•",
                    Margin = new Thickness(0, 0, 8, 0),
                    MinWidth = 14,
                    LineHeight = 22
                };
                bullet.SetResourceReference(TextBlock.ForegroundProperty, list.IsOrdered ? "TextSecondaryBrush" : "AccentBrush");
                Grid.SetColumn(bullet, 0);
                row.Children.Add(bullet);

                var body = new StackPanel();
                foreach (var child in li)
                {
                    if (child is ParagraphBlock para && para.Inline != null)
                    {
                        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
                        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
                        AddInlines(tb.Inlines, para.Inline);
                        body.Children.Add(tb);
                    }
                    else if (child is ListBlock nested)
                    {
                        body.Children.Add(BuildList(nested, depth + 1));
                    }
                    else
                    {
                        var sub = RenderBlock(child, depth + 1);
                        if (sub != null) body.Children.Add(sub);
                    }
                }
                Grid.SetColumn(body, 1);
                row.Children.Add(body);
                host.Children.Add(row);
                index++;
            }
            return host;
        }

        private static FrameworkElement BuildImage(string url)
        {
            var img = new Image
            {
                MaxWidth = 760,
                MaxHeight = 480,               // 别让一张大图把整页顶下去
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                Visibility = Visibility.Collapsed
            };

            // ⚠ BitmapImage 用远程 URL 时是**异步加载**，失败不会抛异常，
            //   只会在 DownloadFailed / DecodeFailed 里报到，Source 一直是 null。
            //   原来这里没有兜底，结果页面下部留一大块空白 —— 看起来像渲染坏了。
            var hint = new TextBlock
            {
                Text = "图片加载中…",
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 10),
                Visibility = Visibility.Collapsed
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextDimBrush");

            var host = new Grid { Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
            host.Children.Add(hint);
            host.Children.Add(img);

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();

                hint.Visibility = Visibility.Visible;
                bmp.DownloadCompleted += (_, __) =>
                {
                    img.Visibility = Visibility.Visible;
                    hint.Visibility = Visibility.Collapsed;
                };
                bmp.DownloadFailed += (_, __) => { hint.Text = "（图片无法加载）"; };
                bmp.DecodeFailed += (_, __) => { hint.Text = "（图片格式不支持）"; };

                img.Source = bmp;
                // 已经缓存命中时不会再触发 DownloadCompleted，这里补一次判定
                if (bmp.IsDownloading == false && bmp.PixelWidth > 0)
                {
                    img.Visibility = Visibility.Visible;
                    hint.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                hint.Text = "（图片无法加载）";
                hint.Visibility = Visibility.Visible;
            }
            return host;
        }

        private static string? OnlyImage(ContainerInline? inline)
        {
            if (inline == null) return null;
            string? url = null;
            int count = 0;
            foreach (var child in inline)
            {
                if (child is LinkInline li && li.IsImage) { url = li.Url; count++; }
                else if (child is LiteralInline lit && string.IsNullOrWhiteSpace(lit.Content.ToString())) { }
                else if (child is LineBreakInline) { }
                else return null;
            }
            return count == 1 ? url : null;
        }

        private static void AddInlines(InlineCollection target, ContainerInline container)
        {
            foreach (var child in container)
            {
                switch (child)
                {
                    case LiteralInline lit:
                        target.Add(new Run(lit.Content.ToString()));
                        break;

                    case EmphasisInline em:
                    {
                        // Markdig 用 DelimiterCount 区分 **粗体** / *斜体* / ~~删除线~~
                        bool bold = em.DelimiterChar == '~' || em.DelimiterCount >= 2;
                        var span = new Span();
                        if (bold) span.FontWeight = FontWeights.SemiBold;
                        if (em.DelimiterChar == '*') span.FontStyle = FontStyles.Italic;
                        if (em.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                        AddInlines(span.Inlines, em);
                        target.Add(span);
                        break;
                    }

                    case CodeInline code:
                    {
                        var run = new Run(code.Content) { FontFamily = Mono, FontSize = 12.5 };
                        run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                        run.SetResourceReference(TextElement.BackgroundProperty, "SurfaceInputBrush");
                        target.Add(run);
                        break;
                    }

                    case LinkInline link when link.IsImage:
                        break;   // 段落内的行内图片不渲染（块级已处理）

                    case LinkInline link:
                    {
                        var hl = new Hyperlink();
                        if (!string.IsNullOrWhiteSpace(link.Url))
                        {
                            try { hl.NavigateUri = new Uri(link.Url!, UriKind.RelativeOrAbsolute); } catch { }
                        }
                        var captured = link.Url;
                        hl.RequestNavigate += (_, ev) =>
                        {
                            try
                            {
                                var target2 = ev.Uri?.ToString() ?? captured;
                                if (!string.IsNullOrWhiteSpace(target2))
                                    Process.Start(new ProcessStartInfo(target2!) { UseShellExecute = true });
                                ev.Handled = true;
                            }
                            catch { }
                        };
                        hl.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                        hl.TextDecorations = null;
                        AddInlines(hl.Inlines, link);
                        target.Add(hl);
                        break;
                    }

                    case LineBreakInline:
                        target.Add(new LineBreak());
                        break;

                    case HtmlInline:
                        break;

                    case ContainerInline container2:
                        AddInlines(target, container2);
                        break;
                }
            }
        }
    }
}
