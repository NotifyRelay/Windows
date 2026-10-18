using NotifyRelay.Models.Render;
using System.Numerics;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay.UI.Islands;

/// <summary>
/// 媒体卡片（收起 / 展开两种组合，不再是两条绘制分支）。
/// 展开：固定 400x100 胶囊 = 封面 + 标题/艺术家跑马灯 + 播放按钮 + 进度条。
/// 收起：自适应宽胶囊 = 小封面 + 标题 + 播放频谱（频谱走 Canvas 逃生节点）。
/// </summary>
internal static class MediaCard
{
    private const float Opacity = 0.9f;

    // 展开态尺寸
    private const float PillWidth = 400f;
    private const float PillHeight = 100f;
    private const float PillPad = 14f;
    private const float CoverSize = 64f;
    private const float ProgressFillRatio = 0.35f;

    // 收起态尺寸
    private const float CollapsedHeight = 36f;
    private const float CollapsedPad = 8f;
    private const float CollapsedCoverSize = 24f;
    private const float CollapsedTitleMax = 180f;
    private const float BarWidth = 2.5f;
    private const float BarGap = 2f;
    private const float BarMaxHeight = 14f;
    private const int BarCount = 5;
    private const float BarsWidth = BarCount * BarWidth + (BarCount - 1) * BarGap;

    public static void Compose(OverlayComposer c, MediaCardItem item, double now, double freq)
    {
        if (item.IsExpanded) ComposeExpanded(c, item, freq);
        else ComposeCollapsed(c, item, freq);
    }

    // ---------- 展开态 ----------

    private static void ComposeExpanded(OverlayComposer c, MediaCardItem item, double freq)
    {
        float contentW = PillWidth - PillPad * 2;
        string title = string.IsNullOrEmpty(item.Title) ? "未在播放" : item.Title;
        double anchor = item.MarqueeAnchorTime / freq;

        c.Node<Surface>(null, s =>
        {
            s.Radius = 20f;
            s.Filled = true;
            s.Fill = new Color4(0f, 0f, 0f, 0.65f * Opacity);
            s.Insets = new Insets(PillPad);
            s.MinWidth = PillWidth;
            s.MinHeight = PillHeight;
        }, () =>
        {
            c.Node<Constrained>(null, k =>
            {
                k.FixedWidth = contentW;
                k.FixedHeight = PillHeight - PillPad * 2;
            }, () =>
            {
                c.Node<Stack>(null, static _ => { }, () =>
                {
                    // 封面（有封面图时）或音符图标
                    if (item.CoverBitmap != null)
                    {
                        c.Node<Absolute>("cover", a => { a.Left = 0f; a.Top = -4f; }, () =>
                        {
                            c.Leaf<Bitmap>(null, b =>
                            {
                                b.Source = () => item.CoverBitmap;
                                b.DrawSize = CoverSize;
                                b.Opacity = Opacity;
                            });
                        });
                    }
                    else
                    {
                        c.Node<Absolute>("note", a => { a.Left = 4f; a.Top = 16f; }, () =>
                        {
                            c.Leaf<Icon>(null, i =>
                            {
                                i.Glyph = "\uD83C\uDFB5";
                                i.FontFamily = "Segoe UI";
                                i.FontSize = 26f;
                                i.HalfOpacity = true;
                                i.Color = new Color4(1f, 1f, 1f, Opacity);
                            });
                        });
                    }

                    // 文本起点：有封面时右移 72，无封面时右移 46（与现状一致）
                    float textX = item.CoverBitmap != null ? 72f : 46f;
                    // 现状：textW = pillWidth - pad - (cx - pillX) - 50，其中 (cx - pillX) = pad + textX
                    float textW = PillWidth - PillPad * 2 - textX - 50f;

                    c.Node<Absolute>("title", a => { a.Left = textX; a.Top = -4f; }, () =>
                    {
                        c.Leaf<MarqueeText>(null, m =>
                        {
                            m.TextValue = title;
                            m.FontFamily = "Microsoft YaHei";
                            m.Weight = DWriteFontWeight.Bold;
                            m.FontSize = 16f;
                            m.LineHeight = 24f;
                            m.MaxWidth = textW;
                            m.Color = new Color4(1f, 1f, 1f, Opacity);
                            m.AnchorSeconds = anchor;
                            m.ScrollEnabled = item.IsPlaying;
                        });
                    });

                    if (!string.IsNullOrEmpty(item.Artist))
                    {
                        c.Node<Absolute>("artist", a => { a.Left = textX; a.Top = 22f; }, () =>
                        {
                            c.Leaf<MarqueeText>(null, m =>
                            {
                                m.TextValue = item.Artist;
                                m.FontFamily = "Microsoft YaHei";
                                m.Weight = DWriteFontWeight.Normal;
                                m.FontSize = 12f;
                                m.LineHeight = 20f;
                                m.MaxWidth = textW;
                                m.Color = new Color4(0.75f, 0.75f, 0.75f, Opacity);
                                m.AnchorSeconds = anchor;
                                m.ScrollEnabled = item.IsPlaying;
                            });
                        });
                    }

                    // 播放 / 暂停按钮
                    c.Node<Absolute>("play", a => { a.Left = contentW - 36f; a.Top = 0f; }, () =>
                    {
                        c.Leaf<Icon>(null, i =>
                        {
                            i.Glyph = item.IsPlaying ? "\u23F8" : "\u25B6";
                            i.FontFamily = "Segoe UI";
                            i.FontSize = 22f;
                            i.Color = new Color4(1f, 1f, 1f, Opacity);
                        });
                    });

                    // 进度条（底部）：现状 progY = 卡片顶 + pillHeight - 12，内容区顶 = 卡片顶 + PillPad
                    c.Node<Absolute>("progress", a => { a.Left = 0f; a.Top = PillHeight - PillPad - 12f; }, () =>
                    {
                        c.Node<Constrained>(null, k => { k.FixedWidth = contentW; k.FixedHeight = 4f; }, () =>
                        {
                            c.Leaf<ProgressBar>(null, p =>
                            {
                                p.TrackColor = new Color4(0.35f, 0.35f, 0.35f, Opacity * 0.6f);
                                p.FillColor = new Color4(0.3f, 0.7f, 1f, Opacity);
                                p.Height = 4f;
                                p.Radius = 2f;
                                p.Progress = ProgressFillRatio * 100f;
                            });
                        });
                    });
                });
            });
        });
    }

    // ---------- 收起态 ----------

    private static void ComposeCollapsed(OverlayComposer c, MediaCardItem item, double freq)
    {
        string title = string.IsNullOrEmpty(item.Title) ? "未在播放" : item.Title;
        double anchor = item.MarqueeAnchorTime / freq;

        c.Node<Constrained>(null, k => { k.MinWidth = 120f; }, () =>
        {
            c.Node<Surface>(null, s =>
            {
                s.Radius = 16f;
                s.Filled = true;
                s.Fill = new Color4(0f, 0f, 0f, 0.65f * Opacity);
                s.Insets = new Insets(CollapsedPad, 0f);
                s.MinWidth = 120f;
                s.MinHeight = CollapsedHeight;
                // 内容左对齐于内边距（MinWidth 兜底撑宽时不得居中偏移）；
                // 纵向由 Row 的 CrossAlignment=Center 在胶囊高内居中
                s.CenterContent = false;
            }, () =>
            {
                c.Node<Row>(null, r =>
                {
                    r.Gap = 6f;
                    r.CrossAlignment = CrossAlignment.Center;
                    // 短文本时胶囊被 MinWidth 撑宽，把多余空白插入频谱之前（末项贴右），
                    // 避免末项与右边界之间留空隙
                    r.AlignLastToEnd = true;
                }, () =>
                {
                    // 小封面（有封面图时）或音符图标
                    if (item.CoverBitmap != null)
                    {
                        c.Node<Box>("cover", b => { b.Width = CollapsedCoverSize; b.Height = CollapsedCoverSize; }, () =>
                        {
                            c.Leaf<Bitmap>(null, bmp =>
                            {
                                bmp.Source = () => item.CoverBitmap;
                                bmp.DrawSize = CollapsedCoverSize;
                                bmp.Opacity = Opacity;
                            });
                        });
                    }
                    else
                    {
                        c.Node<Box>("note", b => { b.Width = CollapsedCoverSize; b.Height = CollapsedCoverSize; }, () =>
                        {
                            c.Leaf<Icon>(null, i =>
                            {
                                i.Glyph = "\uD83C\uDFB5";
                                i.FontFamily = "Segoe UI";
                                i.FontSize = 14f;
                                // 上报整格宽度：Box 居中后仍落在格左上角，与现状 noteLayout(24x24) 的左对齐一致
                                i.ReportedWidth = CollapsedCoverSize;
                                i.HalfOpacity = true;
                                i.Color = new Color4(1f, 1f, 1f, Opacity);
                            });
                        });
                    }

                    // 标题（播放中过长则在裁剪框内滚动，否则省略号截断）
                    c.Leaf<MarqueeText>("title", m =>
                    {
                        m.TextValue = title;
                        m.FontFamily = "Microsoft YaHei";
                        m.Weight = DWriteFontWeight.Normal;
                        m.FontSize = 12f;
                        m.LineHeight = 20f;
                        m.MaxWidth = CollapsedTitleMax;
                        m.Color = new Color4(1f, 1f, 1f, Opacity);
                        m.AnchorSeconds = anchor;
                        m.ScrollEnabled = item.IsPlaying;
                    });

                    // 播放频谱指示器（5 个小竖条，双波峰 W 形震荡动画）
                    c.Node<Box>("bars", b => { b.Width = BarsWidth; b.Height = BarMaxHeight; }, () =>
                    {
                        bool playing = item.IsPlaying;
                        double startTime = item.StartTime;
                        c.Leaf<Canvas>(null, cv =>
                        {
                            cv.FixedSize = new Size(BarsWidth, BarMaxHeight);
                            cv.OnPaint = (s, r) => PaintBars(s, r, playing, startTime);
                        });
                    });
                });
            });
        });
    }

    /// <summary>绘制播放频谱：双波峰 W 形震荡动画，居中向两端缩放。</summary>
    private static void PaintBars(PaintScope s, Rect rect, bool playing, double startTime)
    {
        var brush = s.BrushWithOpacity(new Color4(0.3f, 0.7f, 1f, Opacity));
        float top = rect.Y + (BarMaxHeight - BarMaxHeight) / 2f;

        if (!playing)
        {
            // 暂停时：统一低高度，居中
            float h = BarMaxHeight * 0.2f;
            float y = rect.Y + (BarMaxHeight - h) / 2f;
            for (int i = 0; i < BarCount; i++)
            {
                float bx = rect.X + i * (BarWidth + BarGap);
                s.Rt.FillRectangle(
                    new Vortice.Mathematics.Rect((int)bx, (int)y, Math.Max(1, (int)BarWidth), Math.Max(1, (int)h)),
                    brush);
            }
            return;
        }

        double elapsed = s.NowSeconds - startTime / s.Freq;
        float phase = (float)(elapsed * 3.5);   // 震荡速度
        for (int i = 0; i < BarCount; i++)
        {
            // 两个波峰位于 bar 1 和 bar 3，用距离最近波峰的距离决定基础高度
            double dist = Math.Min(Math.Abs(i - 1.0), Math.Abs(i - 3.0));
            // 离波峰越远越低：peak=1.0, mid=0.65, edge=0.35
            double baseFactor = 1.0 - dist * 0.35;
            // 每根条独立的相位震荡，产生流动感
            double osc = 0.55 + 0.45 * Math.Sin(phase + i * 1.1);
            float h = (float)Math.Max(1.0, BarMaxHeight * baseFactor * osc);

            float bx = rect.X + i * (BarWidth + BarGap);
            float y = top + (BarMaxHeight - h) / 2f;   // 从中间向两端缩放
            s.Rt.FillRectangle(
                new Vortice.Mathematics.Rect((int)bx, (int)y, Math.Max(1, (int)BarWidth), Math.Max(1, (int)h)),
                brush);
        }
    }
}
