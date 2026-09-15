using NotifyRelay.Models.Render;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;

namespace NotifyRelay.Services.Overlay.UI.Islands;

/// <summary>
/// 超级岛卡片：收起 / 展开只是两种组合，不再是两条绘制分支。
/// <code>
/// Align(topCenter) {
///   Surface(radius = expanded ? 16 : h/2) {
///     Padding(expanded ? 8 : 10) {
///       Column(uniformWidth = !expanded) {
///         if (!expanded) Row(gap = 48) { AComponent ; BComponent }
///         else           Column { BgInfo背景 ; 模板节点 ; 追加进度 }
///       }
///     }
///   }
/// }
/// </code>
/// </summary>
internal static class SuperIslandCard
{
    private const float Opacity = 0.9f;
    private const float ExpandedCardWidth = 380f;
    private const float ExpandedPad = 8f;
    private const float CollapsedHPad = 10f;
    private const float CollapsedGapAB = 48f;
    private const float CollapsedMinWidth = 120f;
    /// <summary>收起态文本块显示宽度上限（对齐 Android CommonTextBlockCompose maxWidth=160.dp）。</summary>
    private const float CollapsedTextMaxWidth = 160f;

    /// <summary>
    /// 声明卡片 UI 子树。高度不再由本方法返回：顶部卡片容器（Column）的实测高度
    /// 由调用方在 measure 之后读取并写回 <c>overlay.TopOffset</c>。
    /// </summary>
    public static void Compose(OverlayComposer c, SuperIslandItem item, ScreenOverlay o, double now, double freq)
    {
        if (!item.IsExpanded || item.State.SummaryOnly)
            ComposeCollapsed(c, item, o, now, freq);
        else
            ComposeExpanded(c, item, o);
    }

    // ================= 收起态 =================

    private static void ComposeCollapsed(OverlayComposer c, SuperIslandItem item, ScreenOverlay o,
        double now, double freq)
    {
        var state = item.State;
        var pv = state.ParamV2;
        var big = pv?.ParamIsland?.BigIslandArea;
        var aComp = big?.AComponent;
        var bComp = big?.BComponent;

        // A 区文本块是否两行（标题 + 内容）：胶囊高度自适应
        bool aTwoLine = aComp != null
            && !string.IsNullOrEmpty(aComp.Title) && !string.IsNullOrEmpty(aComp.Content);
        float pillHeight = aTwoLine ? 46f : 40f;

        bool hasA = aComp != null && (!string.IsNullOrEmpty(aComp.Title) || !string.IsNullOrEmpty(aComp.Content)
            || aComp.PicKey != null);
        bool hasB = bComp != null && bComp is not BEmptyData;

        var fallbackText = state.Title ?? state.Subtitle ?? "";
        var bText = hasB ? ResolveBText(bComp!) : fallbackText;

        // 收起态滚动锚点：显示文本变化时重置（对齐 Android AutoScrollText lastText 检查）
        var scrollKey = string.Join('\u0001', aComp?.Title, aComp?.Content, bText);
        if (!string.Equals(item.CollapsedScrollKey, scrollKey, StringComparison.Ordinal))
        {
            item.CollapsedScrollKey = scrollKey;
            item.CollapsedScrollTime = now;
        }
        double anchor = item.CollapsedScrollTime / freq;

        float aIconSize = aComp == null ? 0f : (aComp.Title == null && aComp.Content == null ? 24f : 18f);
        float bIconSize = 18f;

        // 位置由顶部卡片列负责：纵向按实测高度依次累加，横向逐卡片居中
        c.Node<Constrained>(null, k => { k.MinWidth = CollapsedMinWidth; k.FixedHeight = pillHeight; }, () =>
        {
            c.Node<Surface>("pill", s =>
            {
                s.Radius = pillHeight / 2f;
                s.Filled = true;
                s.Fill = new Color4(0f, 0f, 0f, 0.8f * Opacity);
                s.Bordered = true;
                s.Border = new Color4(1f, 1f, 1f, 0.5f * Opacity);
                s.BorderWidth = 1f;
                s.Insets = new Insets(CollapsedHPad, 0f);
                s.MinWidth = CollapsedMinWidth;
                s.MinHeight = pillHeight;
                // 内容左对齐于内边距（MinWidth 兜底撑宽时不得居中偏移）；
                // 纵向由 Row 的 CrossAlignment=Center 在胶囊高内居中
                s.CenterContent = false;
            }, () =>
            {
                c.Node<Row>("content", r =>
                {
                    r.Gap = hasA && hasB ? CollapsedGapAB : 0f;
                    r.CrossAlignment = CrossAlignment.Center;
                }, () =>
                {
                    if (hasA)
                    {
                        c.Node<Row>("a", ar =>
                        {
                            ar.Gap = 6f;
                            ar.CrossAlignment = CrossAlignment.Center;
                        }, () =>
                        {
                            // A 区图标（有文本时 18，无文本时 24）
                            ComposeAIcon(c, item, aComp!, aIconSize);
                            ComposeAText(c, item, aComp!, anchor);
                        });
                    }

                    if (hasB)
                    {
                        ComposeBComponent(c, item, bComp!, bIconSize, anchor);
                    }
                    else if (!string.IsNullOrEmpty(fallbackText))
                    {
                        // 兜底：标题/副标题作为 B 区单行文本（滚动处理）
                        c.Leaf<MarqueeText>("fallback", m =>
                        {
                            m.TextValue = fallbackText;
                            m.FontFamily = "Microsoft YaHei";
                            m.Weight = DWriteFontWeight.Normal;
                            m.FontSize = 12f;
                            m.LineHeight = 18f;
                            m.MaxWidth = CollapsedTextMaxWidth;
                            m.Color = new Color4(1f, 1f, 1f, 0.9f);
                            m.AnchorSeconds = anchor;
                        });
                    }
                });
            });
        });
    }

    private static void ComposeAIcon(OverlayComposer c, SuperIslandItem item, AComponentData aComp, float iconSize)
    {
        bool hasBitmap = item.LeftIconBitmap != null || item.IconBitmap != null;
        bool hasKey = aComp.PicKey != null;
        if (!hasBitmap && !hasKey) return;

        c.Node<Box>("aIcon", b => { b.Width = iconSize; b.Height = iconSize; }, () =>
        {
            c.Leaf<Bitmap>(null, b =>
            {
                b.Source = () => item.LeftIconBitmap ?? item.IconBitmap;
                b.DrawSize = iconSize;
                b.Opacity = 0.9f;
                b.PlaceholderWhenEmpty = hasKey;
            });
        });
    }

    private static void ComposeAText(OverlayComposer c, SuperIslandItem item, AComponentData aComp, double anchor)
    {
        var title = aComp.Title ?? "";
        var content = aComp.Content ?? "";
        bool hasTitle = !string.IsNullOrEmpty(title);
        bool hasContent = !string.IsNullOrEmpty(content);
        if (!hasTitle && !hasContent) return;

        // 文本块宽度 = min(max(标题宽, 内容宽), 160)，由布局按各自宽度自然取最大值
        c.Node<Constrained>("aText", k => { k.MaxWidth = CollapsedTextMaxWidth; }, () =>
        {
            c.Node<Column>("aCol", col =>
            {
                col.CrossAlignment = CrossAlignment.Start;
                col.Spacing = 0f;
                col.MaxChildWidth = CollapsedTextMaxWidth;
            }, () =>
            {
                if (hasTitle)
                {
                    var titleColor = aComp.ShowHighlightColor
                        ? new Color4(0.25f, 0.77f, 1f, 0.9f)   // #40C4FF
                        : new Color4(1f, 1f, 1f, 0.9f);
                    c.Leaf<MarqueeText>("aTitle", m =>
                    {
                        m.TextValue = title;
                        m.FontFamily = "Microsoft YaHei";
                        m.Weight = DWriteFontWeight.Bold;
                        m.FontSize = 14f;
                        m.LineHeight = 20f;
                        m.ReportedHeight = 20f;
                        m.MaxWidth = CollapsedTextMaxWidth;
                        m.Color = titleColor;
                        m.AnchorSeconds = anchor;
                    });
                }
                if (hasContent)
                {
                    c.Leaf<MarqueeText>("aContent", m =>
                    {
                        m.TextValue = content;
                        m.FontFamily = "Microsoft YaHei";
                        m.Weight = DWriteFontWeight.Normal;
                        m.FontSize = 12f;
                        m.LineHeight = 18f;
                        m.ReportedHeight = 18f;
                        m.MaxWidth = CollapsedTextMaxWidth;
                        m.Color = new Color4(0.8f, 0.8f, 0.8f, 0.9f);
                        m.AnchorSeconds = anchor;
                    });
                }
            });
        });
    }

    // ================= B 区（收起态） =================

    private static void ComposeBComponent(OverlayComposer c, SuperIslandItem item,
        BComponentData bComp, float iconSize, double anchor)
    {
        switch (bComp)
        {
            case BImageTextData img:
                {
                    var text = img.Title ?? img.Content;
                    bool hasPic = img.PicKey != null;
                    bool hasText = !string.IsNullOrEmpty(text);
                    if (!hasPic && !hasText) return;

                    c.Node<Row>("b", r => { r.Gap = 6f; r.CrossAlignment = CrossAlignment.Center; }, () =>
                    {
                        if (hasPic)
                        {
                            c.Node<Box>("bIcon", b => { b.Width = iconSize; b.Height = iconSize; }, () =>
                            {
                                c.Leaf<Bitmap>(null, b =>
                                {
                                    b.Source = MakeProvider(() => item.RightIconBitmap ?? item.IconBitmap);
                                    b.DrawSize = iconSize;
                                    b.Opacity = 0.9f;
                                });
                            });
                        }
                        if (hasText)
                        {
                            var weight = img.Kind is "imageText2" or "textInfo"
                                ? DWriteFontWeight.Bold : DWriteFontWeight.Normal;
                            var color = img.ShowHighlightColor
                                ? new Color4(0.25f, 0.77f, 1f, 0.9f)
                                : new Color4(1f, 1f, 1f, 0.9f);
                            c.Leaf<MarqueeText>("bText", m =>
                            {
                                m.TextValue = text!;
                                m.FontFamily = "Microsoft YaHei";
                                m.Weight = weight;
                                m.FontSize = 12f;
                                m.LineHeight = 18f;
                                m.ReportedHeight = 18f;
                                m.MaxWidth = CollapsedTextMaxWidth;
                                m.Color = color;
                                m.AnchorSeconds = anchor;
                            });
                        }
                    });
                }
                break;

            case BDigitInfoData digit:
                {
                    var text = ResolveBText(digit);
                    if (text == null) return;
                    var color = digit.ShowHighlightColor
                        ? new Color4(0.25f, 0.77f, 1f, 0.9f)
                        : new Color4(1f, 1f, 1f, 0.9f);
                    // 等宽数字使用 Consolas；宽度上限 120 保证计时器不换行
                    c.Leaf<Text>("bDigit", t =>
                    {
                        t.TextValue = text;
                        t.FontFamily = "Consolas";
                        t.Weight = DWriteFontWeight.Normal;
                        t.FontSize = 12f;
                        t.LineHeight = 18f;
                        t.ReportedHeight = 18f;
                        t.MaxWidth = 120f;
                        t.Ellipsis = false;
                        t.Color = color;
                    });
                }
                break;

            case BProgressTextInfoData prog:
                {
                    var text = ResolveBText(prog);
                    c.Node<Row>("b", r => { r.Gap = 6f; r.CrossAlignment = CrossAlignment.Center; }, () =>
                    {
                        c.Node<Box>("ring", b => { b.Width = 20f; b.Height = 20f; }, () =>
                        {
                            c.Leaf<ProgressRing>(null, ring =>
                            {
                                ring.Diameter = 20f;
                                ring.StrokeWidth = 2.5f;
                                ring.Progress = Math.Clamp(prog.Progress, 0, 100);
                                ring.ReachColor = ColorHexParser.Parse(prog.ColorReach) ?? new Color4(0.3f, 0.7f, 1f, 1f);
                                ring.UnReachColor = ColorHexParser.Parse(prog.ColorUnReach) ?? new Color4(0.35f, 0.35f, 0.35f, 1f);
                                ring.Opacity = 0.9f;
                            });
                        });
                        if (text != null)
                        {
                            c.Leaf<Text>("bText", t =>
                            {
                                t.TextValue = text;
                                t.FontFamily = "Microsoft YaHei";
                                t.Weight = DWriteFontWeight.Normal;
                                t.FontSize = 12f;
                                t.LineHeight = 18f;
                                t.ReportedHeight = 18f;
                                t.MaxWidth = 100f;
                                t.Color = new Color4(1f, 1f, 1f, 0.9f);
                            });
                        }
                    });
                }
                break;

            case BPicInfoData:
                c.Node<Box>("bPic", b => { b.Width = 24f; b.Height = 24f; }, () =>
                {
                    c.Leaf<Bitmap>(null, b =>
                    {
                        b.Source = MakeProvider(() => item.RightIconBitmap ?? item.IconBitmap);
                        b.DrawSize = 24f;
                        b.Opacity = 0.9f;
                    });
                });
                break;
        }
    }

    /// <summary>解析 B 区显示文本（含等宽数字计时）。</summary>
    private static string? ResolveBText(BComponentData bComp) => bComp switch
    {
        BImageTextData img => img.Title ?? img.Content,
        BDigitInfoData digit => digit.Timer != null ? FormatDigitTimer(digit.Timer) : (digit.Digit ?? digit.Content),
        BProgressTextInfoData prog => prog.Title ?? prog.Content,
        _ => null
    };

    /// <summary>格式化 B 区等宽数字计时器（对齐 Android formatTimerInfo）。</summary>
    public static string FormatDigitTimer(TimerInfoData timer)
    {
        long displayMs;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        switch (timer.TimerType)
        {
            case -2: // 倒计时暂停
                displayMs = Math.Max(0, timer.TimerWhen - timer.TimerSystemCurrent);
                break;
            case -1: // 倒计时进行中
                displayMs = Math.Max(0, timer.TimerWhen - timer.TimerSystemCurrent - (now - timer.TimerSystemCurrent));
                break;
            case 2: // 正计时暂停
                displayMs = Math.Max(0, timer.TimerSystemCurrent - timer.TimerWhen);
                break;
            case 1: // 正计时进行中
                displayMs = Math.Max(0, timer.TimerSystemCurrent - timer.TimerWhen + (now - timer.TimerSystemCurrent));
                break;
            default:
                displayMs = 0;
                break;
        }
        return FormatMilliseconds(displayMs);
    }

    private static string FormatMilliseconds(long ms)
    {
        long totalSeconds = Math.Max(0, ms / 1000);
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    // ================= 展开态 =================

    private static void ComposeExpanded(OverlayComposer c, SuperIslandItem item, ScreenOverlay o)
    {
        var state = item.State;
        var pv = state.ParamV2;
        float contentWidth = ExpandedCardWidth - ExpandedPad * 2;

        // 位置由顶部卡片列负责（纵向累加 + 横向居中），本卡片只描述自身内容
        c.Node<Constrained>(null, k => { k.FixedWidth = ExpandedCardWidth; }, () =>
        {
            c.Node<Surface>("card", s =>
            {
                s.Radius = 16f;
                s.Filled = true;
                s.Fill = new Color4(0f, 0f, 0f, 0.92f * Opacity);
                s.Insets = new Insets(ExpandedPad);
                s.MinWidth = ExpandedCardWidth;
            }, () =>
            {
                c.Node<Constrained>(null, k => { k.FixedWidth = contentWidth; }, () =>
                {
                    c.Node<Stack>(null, static _ => { }, () =>
                    {
                        // 背景层 bgInfo（type=1 全宽 / type=2 右半宽），作为背景先声明
                        if (pv?.BgInfo != null)
                        {
                            var bg = pv.BgInfo;
                            c.Leaf<Canvas>("bgInfo", cv =>
                            {
                                cv.IgnoreInMeasure = true;
                                cv.OnPaint = (s, r) => PaintBgInfo(s, item, r, bg);
                            });
                        }

                        // 内容：单次 measure 遍历（合并了旧的 MeasureExpandedTemplate + 分派双 if 链）
                        c.Node<Column>("content", col =>
                        {
                            col.CrossAlignment = CrossAlignment.Stretch;
                            col.Spacing = 0f;
                        }, () =>
                        {
                            ComposeTemplate(c, item, pv, contentWidth);

                            // 追加进度组件（对齐 Android：主链后 multiProgressInfo ?: progressInfo 独立追加）
                            if (pv?.MultiProgressInfo != null)
                            {
                                ComposeMultiProgress(c, item, pv.MultiProgressInfo, contentWidth);
                            }
                            else if (pv?.ProgressInfo != null)
                            {
                                c.Leaf<ProgressBar>("progress", p =>
                                {
                                    p.Height = 4f;
                                    p.Radius = 2f;
                                    p.Progress = Math.Clamp(pv.ProgressInfo.Progress, 0, 100);
                                    p.FillColor = ColorHexParser.Parse(pv.ProgressInfo.ColorProgress
                                        ?? pv.ProgressInfo.ColorProgressEnd) ?? new Color4(0f, 1f, 0f, 1f);
                                    p.Opacity = Opacity;
                                });
                                c.Leaf<Spacer>("progressGap", sp => { sp.FixedHeight = 6f; });
                            }
                        });
                    });
                });
            });
        });
    }

    private static void ComposeTemplate(OverlayComposer c, SuperIslandItem item, ParamV2? pv, float contentWidth)
    {
        // 模板分派：唯一判据 = ParamV2 字段非空，顺序即优先级
        if (pv?.ParamIsland != null && (pv.ParamIsland.SmallIslandArea != null || pv.ParamIsland.BigIslandArea != null))
            Templates.ParamIsland(c, item, pv.ParamIsland, contentWidth);
        else if (pv?.BaseInfo != null) Templates.BaseInfo(c, item, pv.BaseInfo, contentWidth);
        else if (pv?.ChatInfo != null) Templates.ChatInfo(c, item, pv.ChatInfo, contentWidth);
        else if (pv?.AnimTextInfo != null) Templates.AnimText(c, item, pv.AnimTextInfo, contentWidth);
        else if (pv?.HighlightInfo != null) Templates.Highlight(c, item, pv.HighlightInfo, contentWidth);
        else if (pv?.HighlightInfoV3 != null) Templates.HighlightV3(c, item, pv.HighlightInfoV3, contentWidth);
        else if (pv?.PicInfo != null) Templates.PicInfo(c, item, pv.PicInfo, contentWidth);
        else if (pv?.IconTextInfo != null) Templates.IconText(c, item, pv.IconTextInfo, contentWidth);
        else if (pv?.CoverInfo != null) Templates.CoverInfo(c, item, pv.CoverInfo, contentWidth);
        else if (pv?.TextButton != null || pv?.Actions != null || pv?.HintInfo != null)
            Templates.Actions(c, item, pv, contentWidth);
        else Templates.Default(c, item, contentWidth);
    }

    private static void ComposeMultiProgress(OverlayComposer c, SuperIslandItem item,
        MultiProgressData mp, float contentWidth) => Templates.MultiProgress(c, item, mp, contentWidth);

    /// <summary>绘制模板背景 bgInfo（type=1 全屏铺满卡片，type=2 仅右侧）。</summary>
    private static void PaintBgInfo(PaintScope s, SuperIslandItem item, Rect cardRect, BgInfoData bg)
    {
        bool rightOnly = bg.Type == 2;
        float bx = rightOnly ? cardRect.X + cardRect.Width / 2f : cardRect.X;
        float bw = rightOnly ? cardRect.Width / 2f : cardRect.Width;

        if (item.BgInfoBitmap != null)
        {
            var dest = new Vortice.Mathematics.Rect((int)bx, (int)cardRect.Y, (int)bw, (int)cardRect.Height);
            var src = new Vortice.Mathematics.Rect(0, 0,
                (int)item.BgInfoBitmap.Size.Width, (int)item.BgInfoBitmap.Size.Height);
            s.Rt.DrawBitmap(item.BgInfoBitmap, dest, Opacity,
                Vortice.Direct2D1.BitmapInterpolationMode.Linear, src);
            return;
        }

        // 已有背景图时不再用 colorBg 覆盖位图；仅在无图时填充背景色
        if (ColorHexParser.Parse(bg.ColorBg) is not { } color) return;
        var brush = s.BrushWithOpacity(new Color4(color.R, color.G, color.B, 0.92f * Opacity));
        var rr = new RoundedRectangle(cardRect.ToRectangleF(), 16f, 16f);
        s.Rt.FillRoundedRectangle(ref rr, brush);
    }

    /// <summary>把位图读取器包装为可空委托（位图槽为 null 时返回 null）。</summary>
    internal static Func<Vortice.Direct2D1.ID2D1Bitmap?>? MakeProvider(
        Func<Vortice.Direct2D1.ID2D1Bitmap?> source) => source;
}
