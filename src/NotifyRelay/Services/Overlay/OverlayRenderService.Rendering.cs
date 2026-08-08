using System.Drawing;
using System.Numerics;
using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    private void RenderTopCards(ScreenOverlay overlay, double now, double freq)
    {
        var rt = overlay.RenderTarget!;
        float screenWidth = overlay.Width;
        float y = 10;

        // 锁内仅做轻量快照与超时移除；资源加载与绘制移到锁外，避免渲染线程长时间持锁
        List<MediaCardItem> mediaItems;
        List<SuperIslandItem> superItems;
        if (Monitor.TryEnter(_lock, 2000))
        {
            try
            {
                mediaItems = _topItems.OfType<MediaCardItem>().Where(m => m.Active).ToList();
                superItems = _topItems.OfType<SuperIslandItem>().Where(s => s.Active).ToList();

                // Remove timed out items
                for (int i = mediaItems.Count - 1; i >= 0; i--)
                {
                    var elapsed = (now - mediaItems[i].LastUpdateTime) / freq;
                    if (elapsed > MediaCardItem.TimeoutSeconds)
                    {
                        mediaItems[i].Active = false;
                        mediaItems[i].Dispose();
                        _topItems.Remove(mediaItems[i]);
                        mediaItems.RemoveAt(i);
                    }
                }
                for (int i = superItems.Count - 1; i >= 0; i--)
                {
                    var elapsed = (now - superItems[i].LastUpdateTime) / freq;
                    // 对齐 Android：媒体条目 20s、普通条目 12s 自动移除
                    double timeout = superItems[i].State.IsMedia
                        ? SuperIslandItem.MediaTimeoutSeconds
                        : SuperIslandItem.TimeoutSeconds;
                    if (elapsed > timeout)
                    {
                        superItems[i].Active = false;
                        superItems[i].Dispose();
                        _topItems.Remove(superItems[i]);
                        superItems.RemoveAt(i);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
        }
        else
        {
            // 锁被异常持有：跳过本帧顶部卡片渲染
            overlay.TopOffset = y;
            return;
        }

        // Render media card (max 1) — 居中灵动岛胶囊
        var media = mediaItems.FirstOrDefault();
        if (media != null)
        {
            // 自动收起：媒体字段变更后 5 秒自动收起
            if (media.IsExpanded && (now - media.ExpandedSince) / freq > MediaCardItem.AutoCollapseSeconds)
            {
                media.IsExpanded = false;
            }

            EnsureMediaResources(media, rt);
            DrawMediaCard(media, rt, screenWidth, y, now, freq);
            y += media.IsExpanded ? 108 : 48;
        }

        // Render SuperIsland cards (max 3) — 居中灵动岛胶囊
        foreach (var si in superItems.Take(3))
        {
            // 自动收起：对齐 Android 3s 自动收起（summaryOnly 条目禁止展开）
            if (si.IsExpanded && !si.State.SummaryOnly &&
                (now - si.ExpandedSince) / freq > SuperIslandItem.AutoCollapseSeconds)
            {
                si.IsExpanded = false;
            }

            EnsureSuperIslandResources(si, rt);
            float cardHeight = DrawSuperIslandCard(si, rt, screenWidth, y);
            y += cardHeight;
        }

        overlay.TopOffset = y;
    }

    private void DrawDanmaku(DanmakuItem item, float x, ID2D1DCRenderTarget rt)
    {
        if (item.TextLayout == null) return;

        var s = item.Settings;
        float y = item.TrackY;
        float opacity = s.Opacity;
        float iconOffset = 0;

        if (item.IconBitmap != null)
        {
            float iconSize = (float)s.FontSize;
            // 在文本行内垂直居中，使图标视觉上与字体大小一致
            float iconY = y + Math.Max(0, (item.TextHeight - iconSize) / 2f);
            var destRect = new Vortice.Mathematics.Rect((int)(x + 10), (int)iconY, (int)iconSize, (int)iconSize);
            // DrawBitmap(bitmap, opacity, interp, rect) 的 rect 参数是【源矩形】，并非目标位置；
            // 必须改用 (bitmap, destRect, opacity, interp, srcRect) 重载才能把整张图标绘制到 destRect。
            var srcRect = new Vortice.Mathematics.Rect(0, 0, (int)item.IconBitmap.Size.Width, (int)item.IconBitmap.Size.Height);
            rt.DrawBitmap(item.IconBitmap, destRect, opacity, BitmapInterpolationMode.Linear, srcRect);
            iconOffset = iconSize + 8;
        }

        float textX = x + 10 + iconOffset;
        float textY = y;

        if (s.ShadowEnabled)
        {
            float sd = (float)s.ShadowDepth;
            float so = s.ShadowOpacityFloat * opacity;
            using var shadowBrush = CreateSolidColorBrush(rt,
                new Color4(s.ShadowColorR / 255f, s.ShadowColorG / 255f,
                           s.ShadowColorB / 255f, so));
            rt.DrawTextLayout(new Vector2(textX + sd, textY + sd), item.TextLayout, shadowBrush);
        }

        if (s.BorderEnabled && s.BorderThickness > 0)
        {
            float bt = (float)s.BorderThickness;
            using var strokeBrush = CreateSolidColorBrush(rt,
                new Color4(s.BorderColorR / 255f, s.BorderColorG / 255f,
                           s.BorderColorB / 255f, opacity));
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    rt.DrawTextLayout(new Vector2(textX + dx * bt, textY + dy * bt),
                        item.TextLayout, strokeBrush);
                }
        }

        using var fillBrush = CreateSolidColorBrush(rt,
            new Color4(s.ColorR / 255f, s.ColorG / 255f,
                       s.ColorB / 255f, opacity));
        rt.DrawTextLayout(new Vector2(textX, textY), item.TextLayout, fillBrush);
    }

    private void DrawMediaCard(MediaCardItem item, ID2D1DCRenderTarget rt, float screenWidth, float y, double now, double freq)
    {
        if (!item.IsExpanded)
        {
            DrawMediaCardCollapsed(item, rt, screenWidth, y, now, freq);
            return;
        }

        const float pillWidth = 400;
        const float pillHeight = 100;
        float pillX = (screenWidth - pillWidth) / 2;
        float pad = 14;
        float opacity = 0.9f;

        // Pill background
        DrawPillBackground(rt, pillX, y, pillWidth, pillHeight, 20, opacity);

        float cx = pillX + pad;

        // Cover / music note icon
        if (item.CoverBitmap != null)
        {
            DrawCoverBitmap(rt, item.CoverBitmap, cx, y + 10, 64, opacity);
            cx += 72;
        }
        else
        {
            using var noteFormat = CreateTextFormat("Segoe UI", DWriteFontWeight.Normal, 26);
            using var noteLayout = _dwFactory.CreateTextLayout("\uD83C\uDFB5", noteFormat, 36, 36);
            using var noteBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity * 0.5f));
            rt.DrawTextLayout(new Vector2(cx + 4, y + 30), noteLayout, noteBrush);
            cx += 46;
        }

        float textW = pillWidth - pad - (cx - pillX) - 50;

        // Title
        string title = string.IsNullOrEmpty(item.Title) ? "未在播放" : item.Title;
        DrawMediaMarqueeText(title, "Microsoft YaHei", DWriteFontWeight.Bold, 16, rt, cx, y + 10, textW, 24, item, now, freq, new Color4(1, 1, 1, opacity));

        // Artist
        if (!string.IsNullOrEmpty(item.Artist))
        {
            DrawMediaMarqueeText(item.Artist, "Microsoft YaHei", DWriteFontWeight.Normal, 12, rt, cx, y + 36, textW, 20, item, now, freq, new Color4(0.75f, 0.75f, 0.75f, opacity));
        }

        // Play/pause button
        string playIcon = item.IsPlaying ? "\u23F8" : "\u25B6";
        using var playFmt = CreateTextFormat("Segoe UI", DWriteFontWeight.Normal, 22);
        using var playLyt = _dwFactory.CreateTextLayout(playIcon, playFmt, 30, 30);
        using var playBr = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(pillX + pillWidth - pad - 36, y + 14), playLyt, playBr);

        // Progress bar
        using var progBg = CreateSolidColorBrush(rt, new Color4(0.35f, 0.35f, 0.35f, opacity * 0.6f));
        float progY = y + pillHeight - 12;
        float progW = pillWidth - pad * 2;
        var progBgRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, progW, 4), 2, 2);
        rt.FillRoundedRectangle(ref progBgRR, progBg);

        using var progFill = CreateSolidColorBrush(rt, new Color4(0.3f, 0.7f, 1.0f, opacity));
        float fillW = progW * 0.35f;
        var progFillRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, fillW, 4), 2, 2);
        rt.FillRoundedRectangle(ref progFillRR, progFill);
    }

    /// <summary>
    /// 收起态：紧凑胶囊 — 小封面 + 标题 + 播放频谱指示器
    /// </summary>
    private void DrawMediaCardCollapsed(MediaCardItem item, ID2D1DCRenderTarget rt, float screenWidth, float y, double now, double freq)
    {
        const float pillHeight = 36;
        float pad = 8;
        float opacity = 0.9f;

        // 计算内容宽度：封面(24) + 间距(6) + 标题(动态) + 间距(6) + 频谱5条(23)
        // 先测量标题文本完整宽度（单行、不截断），用于决定胶囊宽度
        string titleText = string.IsNullOrEmpty(item.Title) ? "未在播放" : item.Title;
        using var titleMeasureFmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 12);
        using var titleMeasure = _dwFactory.CreateTextLayout(titleText, titleMeasureFmt, 10000, 20);
        titleMeasure.WordWrapping = WordWrapping.NoWrap;
        float titleWidth = Math.Min(titleMeasure.Metrics.WidthIncludingTrailingWhitespace, 180);

        float contentWidth = 24 + 6 + titleWidth + 6 + 21; // 5条频谱: 5*2.5+4*2=20.5
        float pillWidth = Math.Max(contentWidth + pad * 2, 120);
        float pillX = (screenWidth - pillWidth) / 2;

        // Pill background
        DrawPillBackground(rt, pillX, y, pillWidth, pillHeight, 16, opacity);

        float cx = pillX + pad;
        float centerY = y + (pillHeight - 24) / 2.0f;

        // 小封面 (24x24)
        if (item.CoverBitmap != null)
        {
            DrawCoverBitmap(rt, item.CoverBitmap, cx, centerY, 24, opacity);
        }
        else
        {
            // 音符图标替代
            using var noteFormat = CreateTextFormat("Segoe UI", DWriteFontWeight.Normal, 14);
            using var noteLayout = _dwFactory.CreateTextLayout("\uD83C\uDFB5", noteFormat, 24, 24);
            using var noteBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity * 0.5f));
            rt.DrawTextLayout(new Vector2(cx, centerY), noteLayout, noteBrush);
        }
        cx += 24 + 6;

        // 标题文本（播放中过长则在裁剪框内滚动，否则省略号截断）
        DrawMediaMarqueeText(titleText, "Microsoft YaHei", DWriteFontWeight.Normal, 12, rt, cx, centerY + 2, titleWidth, 20, item, now, freq, new Color4(1, 1, 1, opacity));
        cx += titleWidth + 6;

        // 播放频谱指示器（5 个小竖条，双波峰W形流畅震荡动画，居中向两端缩放）
        const int barCount = 5;
        float barWidth = 2.5f;
        float barGap = 2;
        float maxBarHeight = 14;
        float barTop = centerY + (24 - maxBarHeight) / 2.0f; // 垂直居中
        using var barBrush = CreateSolidColorBrush(rt, new Color4(0.3f, 0.7f, 1.0f, opacity));

        if (item.IsPlaying)
        {
            // 双波峰 W 形震荡动画：bars 1和3为波峰，bar 2为波谷，bars 0和4为边缘
            double elapsed = (now - item.StartTime) / freq;

            float[] heights = new float[barCount];
            float phase = (float)(elapsed * 3.5); // 震荡速度

            for (int i = 0; i < barCount; i++)
            {
                // 两个波峰位于 bar 1 和 bar 3，用距离最近波峰的距离决定基础高度
                double dist = Math.Min(Math.Abs(i - 1.0), Math.Abs(i - 3.0));
                // 离波峰越远越低：peak=1.0, mid=0.65, edge=0.35
                double baseFactor = 1.0 - dist * 0.35;

                // 每根条独立的相位震荡，产生流动感
                double osc = 0.55 + 0.45 * Math.Sin(phase + i * 1.1);
                double h = maxBarHeight * baseFactor * osc;

                heights[i] = (float)Math.Max(1.0, h);
            }

            for (int i = 0; i < barCount; i++)
            {
                float bx = cx + i * (barWidth + barGap);
                float h = heights[i];
                float top = barTop + (maxBarHeight - h) / 2.0f; // 从中间向两端缩放
                int bw = Math.Max(1, (int)(barWidth));
                int bh = Math.Max(1, (int)(h));
                var barRect = new Vortice.Mathematics.Rect((int)bx, (int)top, bw, bh);
                rt.FillRectangle(in barRect, barBrush);
            }
        }
        else
        {
            // 暂停时：统一低高度，居中
            float h = maxBarHeight * 0.2f;
            float top = barTop + (maxBarHeight - h) / 2.0f;
            for (int i = 0; i < barCount; i++)
            {
                float bx = cx + i * (barWidth + barGap);
                int bw = Math.Max(1, (int)(barWidth));
                int bh = Math.Max(1, (int)(h));
                var barRect = new Vortice.Mathematics.Rect((int)bx, (int)top, bw, bh);
                rt.FillRectangle(in barRect, barBrush);
            }
        }
    }

    /// <summary>
    /// 绘制超级岛卡片（收起态胶囊 / 展开态大岛），返回实际占用的高度。
    /// 布局对齐 Android superislandui：胶囊 = A区 + B区；展开 = 按模板分支渲染。
    /// </summary>
    private float DrawSuperIslandCard(SuperIslandItem item, ID2D1DCRenderTarget rt, float screenWidth, float y)
    {
        if (!item.IsExpanded || item.State.SummaryOnly)
        {
            return DrawSuperIslandCollapsed(item, rt, screenWidth, y);
        }
        return DrawSuperIslandExpanded(item, rt, screenWidth, y);
    }

    // ---------- 收起态（胶囊，对齐 BigIslandCollapsedCompose） ----------

    private float DrawSuperIslandCollapsed(SuperIslandItem item, ID2D1DCRenderTarget rt, float screenWidth, float y)
    {
        const float pillHeight = 40;
        const float hPad = 10;
        const float gapAB = 48;
        const float opacity = 0.9f;

        var state = item.State;
        var pv = state.ParamV2;
        var big = pv?.ParamIsland?.BigIslandArea;
        var aComp = big?.AComponent;
        var bComp = big?.BComponent;

        // 计算内容宽度（A区 + 间距 + B区），wrapContentWidth 自适应
        float aWidth = MeasureAComponent(item, aComp);
        float bWidth = MeasureBComponent(item, state, bComp);
        bool hasA = aWidth > 0;
        bool hasB = bWidth > 0;
        if (!hasA && !hasB)
        {
            // 兜底：标题作为 B 区文本
            bWidth = MeasureTextWidth(state.Title ?? state.Subtitle ?? "", "Microsoft YaHei", DWriteFontWeight.Normal, 12);
            hasB = bWidth > 0;
        }

        float contentWidth = (hasA ? aWidth : 0) + (hasA && hasB ? gapAB : 0) + (hasB ? bWidth : 0);
        float pillWidth = Math.Max(contentWidth + hPad * 2, 120);
        float pillX = (screenWidth - pillWidth) / 2;
        float cx = pillX + hPad;
        float centerY = y + pillHeight / 2f;

        // 胶囊背景（0xCC 黑） + 1px 半透明白边框
        using var bgBrush = CreateSolidColorBrush(rt, new Color4(0, 0, 0, 0.8f * opacity));
        var bgRR = new RoundedRectangle(new RectangleF(pillX, y, pillWidth, pillHeight), pillHeight / 2f, pillHeight / 2f);
        rt.FillRoundedRectangle(ref bgRR, bgBrush);

        using var borderBrush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, 0.5f * opacity));
        rt.DrawRoundedRectangle(bgRR, borderBrush, 1.0f);

        if (hasA)
        {
            cx += DrawAComponent(item, rt, cx, centerY);
        }
        if (hasA && hasB)
        {
            cx += gapAB;
        }
        if (hasB)
        {
            DrawBComponent(item, state, bComp, rt, cx, centerY, pillWidth - hPad * 2 - (cx - pillX));
        }

        return pillHeight;
    }

    /// <summary>测量 A 区宽度（图标 + 文本）。</summary>
    private float MeasureAComponent(SuperIslandItem item, AComponentData? aComp)
    {
        if (aComp == null) return 0;
        float iconSize = aComp.Title == null && aComp.Content == null ? 24 : 18;
        float textW = 0;
        if (aComp.Title != null || aComp.Content != null)
        {
            var title = aComp.Title ?? "";
            var content = aComp.Content ?? "";
            float titleW = MeasureTextWidth(title, "Microsoft YaHei", DWriteFontWeight.Bold, 14);
            float contentW = MeasureTextWidth(content, "Microsoft YaHei", DWriteFontWeight.Normal, 12);
            textW = Math.Max(titleW, contentW);
        }
        return iconSize + (textW > 0 ? 6 + Math.Min(textW, 160) : 0);
    }

    /// <summary>绘制 A 区（图标 + 文本块），返回占用宽度。</summary>
    private float DrawAComponent(SuperIslandItem item, ID2D1DCRenderTarget rt, float x, float centerY)
    {
        var aComp = item.State.ParamV2?.ParamIsland?.BigIslandArea?.AComponent;
        if (aComp == null) return 0;

        bool hasText = aComp.Title != null || aComp.Content != null;
        float iconSize = hasText ? 18 : 24;
        float textW = 0;
        if (hasText)
        {
            var title = aComp.Title ?? "";
            var content = aComp.Content ?? "";
            float titleW = MeasureTextWidth(title, "Microsoft YaHei", DWriteFontWeight.Bold, 14);
            float contentW = MeasureTextWidth(content, "Microsoft YaHei", DWriteFontWeight.Normal, 12);
            textW = Math.Min(Math.Max(titleW, contentW), 160);
        }

        float drawX = x;
        var iconBmp = item.LeftIconBitmap ?? item.IconBitmap;
        if (iconBmp != null)
        {
            DrawCoverBitmap(rt, iconBmp, drawX, centerY - iconSize / 2f, iconSize, 0.9f);
            drawX += iconSize + 6;
        }
        else if (aComp.PicKey != null)
        {
            // 有 key 但图未加载成功：绘制占位圆
            using var phBrush = CreateSolidColorBrush(rt, new Color4(0.5f, 0.5f, 0.5f, 0.6f));
            var phCenter = new Vector2(drawX + iconSize / 2f, centerY);
            var phEllipse = new Ellipse(phCenter, iconSize / 2f, iconSize / 2f);
            rt.FillEllipse(phEllipse, phBrush);
            drawX += iconSize + 6;
        }

        if (hasText)
        {
            var title = aComp.Title ?? "";
            var content = aComp.Content ?? "";
            Color4 titleColor = aComp.ShowHighlightColor
                ? new Color4(0.25f, 0.77f, 1.0f, 0.9f)  // #40C4FF
                : new Color4(1, 1, 1, 0.9f);
            if (!string.IsNullOrEmpty(title))
            {
                using var fmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Bold, 14);
                using var lyt = CreateTruncatedLayout(title, "Microsoft YaHei", DWriteFontWeight.Bold, 14, Math.Min(textW, 160), 20);
                using var brush = CreateSolidColorBrush(rt, titleColor);
                rt.DrawTextLayout(new Vector2(drawX, centerY - 10), lyt, brush);
            }
            if (!string.IsNullOrEmpty(content))
            {
                using var fmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 12);
                using var lyt = CreateTruncatedLayout(content, "Microsoft YaHei", DWriteFontWeight.Normal, 12, Math.Min(textW, 160), 18);
                using var brush = CreateSolidColorBrush(rt, new Color4(0.8f, 0.8f, 0.8f, 0.9f));
                rt.DrawTextLayout(new Vector2(drawX, centerY - 9 + (string.IsNullOrEmpty(title) ? 0 : 2)), lyt, brush);
            }
            drawX += Math.Min(textW, 160);
        }

        return drawX - x;
    }

    /// <summary>测量 B 区宽度。</summary>
    private float MeasureBComponent(SuperIslandItem item, SuperIslandState state, BComponentData? bComp)
    {
        if (bComp == null || bComp is BEmptyData) return 0;

        var text = ResolveBText(bComp, state);
        float textW = text != null ? MeasureTextWidth(text, "Microsoft YaHei", DWriteFontWeight.Normal, 12) : 0;

        return bComp switch
        {
            BImageTextData img => (img.PicKey != null ? 18 + 6 : 0) + (text != null ? Math.Min(textW, 160) : 0),
            BDigitInfoData => text != null ? Math.Min(textW, 120) : 0,
            BProgressTextInfoData => 20 + 6 + (text != null ? Math.Min(textW, 100) : 0),
            BPicInfoData => 24,
            _ => 0
        };
    }

    /// <summary>绘制 B 区（文本 / 等宽数字 / 进度圆环 / 图片），返回占用宽度。</summary>
    private float DrawBComponent(SuperIslandItem item, SuperIslandState state, BComponentData? bComp,
        ID2D1DCRenderTarget rt, float x, float centerY, float maxWidth)
    {
        if (bComp == null || bComp is BEmptyData) return 0;

        var text = ResolveBText(bComp, state);
        float drawX = x;

        switch (bComp)
        {
            case BImageTextData img:
                var iconBmp = item.RightIconBitmap ?? item.IconBitmap;
                if (img.PicKey != null && iconBmp != null)
                {
                    DrawCoverBitmap(rt, iconBmp, drawX, centerY - 9, 18, 0.9f);
                    drawX += 24;
                }
                if (text != null)
                {
                    Color4 c = img.ShowHighlightColor ? new Color4(0.25f, 0.77f, 1.0f, 0.9f) : new Color4(1, 1, 1, 0.9f);
                    using var lyt = CreateTruncatedLayout(text, "Microsoft YaHei",
                        img.Kind is "imageText2" or "textInfo" ? DWriteFontWeight.Bold : DWriteFontWeight.Normal,
                        12, maxWidth - (drawX - x), 18);
                    using var brush = CreateSolidColorBrush(rt, c);
                    rt.DrawTextLayout(new Vector2(drawX, centerY - 9), lyt, brush);
                    drawX += Math.Min(MeasureTextWidth(text, "Microsoft YaHei", DWriteFontWeight.Normal, 12), maxWidth - (drawX - x));
                }
                break;

            case BDigitInfoData digit:
                if (text != null)
                {
                    Color4 c = digit.ShowHighlightColor ? new Color4(0.25f, 0.77f, 1.0f, 0.9f) : new Color4(1, 1, 1, 0.9f);
                    using var fmt = CreateTextFormat("Consolas", DWriteFontWeight.Normal, 12);
                    using var lyt = _dwFactory.CreateTextLayout(text, fmt, maxWidth, 18);
                    using var brush = CreateSolidColorBrush(rt, c);
                    rt.DrawTextLayout(new Vector2(drawX, centerY - 9), lyt, brush);
                    drawX += Math.Min(lyt.Metrics.WidthIncludingTrailingWhitespace, maxWidth);
                }
                break;

            case BProgressTextInfoData prog:
                // 20px 圆环 + 文本
                DrawProgressRing(rt, drawX, centerY, 20, 2.5f, prog.Progress,
                    prog.ColorReach, prog.ColorUnReach, 0.9f);
                drawX += 26;
                if (text != null)
                {
                    using var lyt = CreateTruncatedLayout(text, "Microsoft YaHei", DWriteFontWeight.Normal, 12,
                        Math.Max(0, maxWidth - 26), 18);
                    using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, 0.9f));
                    rt.DrawTextLayout(new Vector2(drawX, centerY - 9), lyt, brush);
                    drawX += Math.Min(lyt.Metrics.WidthIncludingTrailingWhitespace, maxWidth - 26);
                }
                break;

            case BPicInfoData pic:
                var picBmp = item.RightIconBitmap ?? item.IconBitmap;
                if (picBmp != null)
                {
                    DrawCoverBitmap(rt, picBmp, drawX, centerY - 12, 24, 0.9f);
                    drawX += 24;
                }
                break;
        }

        return drawX - x;
    }

    /// <summary>解析 B 区显示文本（含等宽数字计时）。</summary>
    private static string? ResolveBText(BComponentData bComp, SuperIslandState state)
    {
        switch (bComp)
        {
            case BImageTextData img:
                return img.Title ?? img.Content;
            case BDigitInfoData digit:
                if (digit.Timer != null)
                {
                    return FormatDigitTimer(digit.Timer);
                }
                return digit.Digit ?? digit.Content;
            case BProgressTextInfoData prog:
                return prog.Title ?? prog.Content;
            default:
                return null;
        }
    }

    /// <summary>格式化 B 区等宽数字计时器（对齐 Android formatTimerInfo）。</summary>
    private static string FormatDigitTimer(TimerInfoData timer)
    {
        long displayMs;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        switch (timer.TimerType)
        {
            case -2: // 倒计时暂停
                displayMs = Math.Max(0, timer.TimerWhen - timer.TimerSystemCurrent);
                break;
            case -1: // 倒计时进行中
                displayMs = Math.Max(0, (timer.TimerWhen - timer.TimerSystemCurrent) - (now - timer.TimerSystemCurrent));
                break;
            case 2: // 正计时暂停
                displayMs = Math.Max(0, timer.TimerSystemCurrent - timer.TimerWhen);
                break;
            case 1: // 正计时进行中
                displayMs = Math.Max(0, (timer.TimerSystemCurrent - timer.TimerWhen) + (now - timer.TimerSystemCurrent));
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
        long minutes = (totalSeconds % 3600) / 60;
        long seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";
    }

    // ---------- 展开态（大岛，按模板分支渲染） ----------

    private float DrawSuperIslandExpanded(SuperIslandItem item, ID2D1DCRenderTarget rt, float screenWidth, float y)
    {
        const float cardWidth = 380;
        const float pad = 8;
        const float opacity = 0.9f;

        var state = item.State;
        var pv = state.ParamV2;
        var cardX = (screenWidth - cardWidth) / 2;

        float contentWidth = cardWidth - pad * 2;
        float cx = cardX + pad;
        float cy = y + pad;

        // 按模板分支选择布局并计算内容高度
        float contentHeight;
        if (pv?.ChatInfo != null)
        {
            contentHeight = DrawChatInfoTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else if (pv?.HighlightInfo != null)
        {
            contentHeight = DrawHighlightTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else if (pv?.MultiProgressInfo != null)
        {
            contentHeight = DrawMultiProgressTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else if (pv?.PicInfo != null)
        {
            contentHeight = DrawPicInfoTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else if (pv?.AnimTextInfo != null)
        {
            contentHeight = DrawAnimTextTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else if (pv?.TextButton != null || pv?.Actions != null || pv?.HintInfo != null)
        {
            contentHeight = DrawActionsTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else if (pv?.BaseInfo != null)
        {
            contentHeight = DrawBaseInfoTemplate(item, rt, cx, cy, contentWidth, opacity);
        }
        else
        {
            contentHeight = DrawDefaultTemplate(item, rt, cx, cy, contentWidth, opacity);
        }

        float cardHeight = contentHeight + pad * 2;

        // 大岛背景：黑 0.92、圆角 16
        using var bgBrush = CreateSolidColorBrush(rt, new Color4(0, 0, 0, 0.92f * opacity));
        var bgRR = new RoundedRectangle(new RectangleF(cardX, y, cardWidth, cardHeight), 16, 16);
        rt.FillRoundedRectangle(ref bgRR, bgBrush);

        return cardHeight;
    }

    private float DrawChatInfoTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var chat = item.State.ParamV2!.ChatInfo!;
        const float avatarSize = 48;
        float textW = contentWidth - avatarSize - 12;
        float textHeight = MeasureTextHeight(chat.Title ?? "", 14) + MeasureTextHeight(chat.Content ?? "", 12);

        // 圆形头像
        if (item.AvatarBitmap != null)
        {
            DrawCircleCroppedBitmap(rt, item.AvatarBitmap, cx, cy, avatarSize, opacity);
        }
        else if (chat.PicProfile != null)
        {
            DrawCirclePlaceholder(rt, cx, cy, avatarSize, opacity);
        }

        float tx = cx + avatarSize + 12;
        if (!string.IsNullOrEmpty(chat.Title))
        {
            using var lyt = CreateTruncatedLayout(chat.Title, "Microsoft YaHei", DWriteFontWeight.Bold, 14, textW, 20);
            using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
            rt.DrawTextLayout(new Vector2(tx, cy), lyt, brush);
        }
        if (!string.IsNullOrEmpty(chat.Content))
        {
            using var lyt = CreateTruncatedLayout(chat.Content, "Microsoft YaHei", DWriteFontWeight.Normal, 12, textW, 18);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.75f, 0.75f, 0.75f, opacity));
            rt.DrawTextLayout(new Vector2(tx, cy + 22), lyt, brush);
        }
        // 计时器（右侧小字）
        var timer = chat.TimerInfo;
        if (timer != null)
        {
            var t = FormatDigitTimer(timer);
            using var fmt = CreateTextFormat("Consolas", DWriteFontWeight.Normal, 11);
            using var lyt = _dwFactory.CreateTextLayout(t, fmt, 70, 16);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(cx + contentWidth - 70, cy), lyt, brush);
        }

        return Math.Max(avatarSize, textHeight) + 6;
    }

    private float DrawHighlightTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var hi = item.State.ParamV2!.HighlightInfo!;
        const float iconSize = 40;
        const float bigImageSize = 44;
        float bigImages = (item.BigImageLeftBitmap != null ? bigImageSize + 6 : 0) + (item.BigImageRightBitmap != null ? bigImageSize : 0);
        float textW = contentWidth - iconSize - 12 - bigImages - (hi.IconOnly ? 0 : 0);

        // 图标（iconOnly 放大到 48）
        float effIconSize = hi.IconOnly ? 48 : iconSize;
        textW = contentWidth - effIconSize - 12 - bigImages;

        if (item.IconBitmap != null)
        {
            DrawCoverBitmap(rt, item.IconBitmap, cx, cy, effIconSize, opacity);
        }
        else if (hi.PicFunction != null)
        {
            DrawCirclePlaceholder(rt, cx, cy, effIconSize, opacity);
        }

        float tx = cx + effIconSize + 12;
        float ty = cy;
        // 主文本 15sp（高亮色）
        if (!string.IsNullOrEmpty(hi.Title))
        {
            using var lyt = CreateTruncatedLayout(hi.Title, "Microsoft YaHei", DWriteFontWeight.Bold, 15, textW, 22);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.25f, 0.77f, 1.0f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 22;
        }
        // 计时器 16sp
        if (hi.TimerInfo != null)
        {
            var t = FormatDigitTimer(hi.TimerInfo);
            using var fmt = CreateTextFormat("Consolas", DWriteFontWeight.Normal, 16);
            using var lyt = _dwFactory.CreateTextLayout(t, fmt, textW, 22);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.25f, 0.77f, 1.0f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 22;
        }
        // 内容
        if (!string.IsNullOrEmpty(hi.Content) && hi.Type != 1)
        {
            using var lyt = CreateTruncatedLayout(hi.Content, "Microsoft YaHei", DWriteFontWeight.Normal, 12, textW, 18);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.8f, 0.8f, 0.8f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 18;
        }
        // 子内容（状态）
        if (!string.IsNullOrEmpty(hi.SubContent))
        {
            using var lyt = CreateTruncatedLayout(hi.SubContent, "Microsoft YaHei", DWriteFontWeight.Normal, 12, textW, 18);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.6f, 0.6f, 0.6f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 18;
        }

        // 右侧大图
        float bx = cx + contentWidth - bigImages;
        if (item.BigImageRightBitmap != null)
        {
            DrawCoverBitmap(rt, item.BigImageRightBitmap, bx, cy, bigImageSize, opacity);
            bx -= bigImageSize + 6;
        }
        if (item.BigImageLeftBitmap != null)
        {
            DrawCoverBitmap(rt, item.BigImageLeftBitmap, bx, cy, bigImageSize, opacity);
        }

        return Math.Max(effIconSize, ty - cy) + 4;
    }

    private float DrawBaseInfoTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var bi = item.State.ParamV2!.BaseInfo!;
        float ty = cy;

        // type=1：次要文本在上；type=2：主要文本在上
        if (bi.Type == 1)
        {
            ty = DrawBaseTextLine(rt, cx, ty, contentWidth, opacity, bi.Content ?? bi.SubContent, 12, false, bi.ColorContent);
            ty = DrawBaseTextLine(rt, cx, ty, contentWidth, opacity, bi.Title ?? bi.SubTitle, 14, true, bi.ColorTitle);
            ty = DrawBaseTextLine(rt, cx, ty, contentWidth, opacity, bi.ExtraTitle, 12, false, bi.ColorExtraTitle);
        }
        else
        {
            ty = DrawBaseTextLine(rt, cx, ty, contentWidth, opacity, bi.Title ?? bi.SubTitle, 14, true, bi.ColorTitle);
            if (!string.IsNullOrEmpty(bi.ExtraTitle))
            {
                ty = DrawBaseTextLine(rt, cx, ty, contentWidth, opacity, bi.ExtraTitle, 14, true, bi.ColorExtraTitle);
            }
            ty = DrawBaseTextLine(rt, cx, ty, contentWidth, opacity, bi.Content ?? bi.SubContent, 12, false, bi.ColorContent);
        }

        // 特殊标签（specialTitle：圆角背景块）
        if (!string.IsNullOrEmpty(bi.SpecialTitle))
        {
            var tag = bi.SpecialTitle;
            float tagW = MeasureTextWidth(tag, "Microsoft YaHei", DWriteFontWeight.Normal, 11) + 12;
            using var tagFmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 11);
            using var tagLyt = _dwFactory.CreateTextLayout(tag, tagFmt, tagW, 18);
            using var bgBrush = CreateSolidColorBrush(rt, new Color4(0.3f, 0.3f, 0.3f, 0.8f));
            var tagRR = new RoundedRectangle(new RectangleF(cx, ty, tagW, 18), 4, 4);
            rt.FillRoundedRectangle(ref tagRR, bgBrush);
            using var textBrush = CreateSolidColorBrush(rt, new Color4(0.8f, 0.8f, 0.8f, opacity));
            rt.DrawTextLayout(new Vector2(cx + 6, ty + 1), tagLyt, textBrush);
            ty += 22;
        }

        return ty - cy + 2;
    }

    private float DrawBaseTextLine(ID2D1DCRenderTarget rt, float x, float y, float maxWidth, float opacity,
        string? text, float size, bool bold, string? colorHex)
    {
        if (string.IsNullOrEmpty(text)) return y;
        var color = ParseHexColor(colorHex) ?? new Color4(1, 1, 1, opacity);
        if (!bold && colorHex == null) color = new Color4(0.75f, 0.75f, 0.75f, opacity);
        using var lyt = CreateTruncatedLayout(text, "Microsoft YaHei", bold ? DWriteFontWeight.Bold : DWriteFontWeight.Normal, size, maxWidth, size + 6);
        using var brush = CreateSolidColorBrush(rt, color);
        rt.DrawTextLayout(new Vector2(x, y), lyt, brush);
        return y + size + 6;
    }

    private float DrawPicInfoTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var pic = item.State.ParamV2!.PicInfo!;
        const float picSize = 48;
        float ty = cy;

        if (item.PicInfoBitmap != null)
        {
            DrawCoverBitmap(rt, item.PicInfoBitmap, cx, cy, picSize, opacity);
        }
        else if (pic.Pic != null)
        {
            DrawCirclePlaceholder(rt, cx, cy, picSize, opacity);
        }

        float tx = cx + picSize + 12;
        if (!string.IsNullOrEmpty(pic.Title))
        {
            using var lyt = CreateTruncatedLayout(pic.Title, "Microsoft YaHei", DWriteFontWeight.Bold, 14, contentWidth - picSize - 12, 20);
            using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
            rt.DrawTextLayout(new Vector2(tx, cy + 14), lyt, brush);
        }

        return picSize + 4;
    }

    private float DrawAnimTextTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var anim = item.State.ParamV2!.AnimTextInfo!;
        const float iconSize = 40;
        float textW = contentWidth - iconSize - 12;
        float ty = cy;

        if (item.IconBitmap != null)
        {
            DrawCoverBitmap(rt, item.IconBitmap, cx, cy, iconSize, opacity);
        }
        else if (anim.IconSrc != null)
        {
            DrawCirclePlaceholder(rt, cx, cy, iconSize, opacity);
        }

        float tx = cx + iconSize + 12;
        if (!string.IsNullOrEmpty(anim.Title))
        {
            using var lyt = CreateTruncatedLayout(anim.Title, "Microsoft YaHei", DWriteFontWeight.Bold, 15, textW, 22);
            using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 22;
        }
        if (anim.TimerInfo != null)
        {
            var t = FormatDigitTimer(anim.TimerInfo);
            using var fmt = CreateTextFormat("Consolas", DWriteFontWeight.Normal, 15);
            using var lyt = _dwFactory.CreateTextLayout(t, fmt, textW, 22);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.25f, 0.77f, 1.0f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 22;
        }
        if (!string.IsNullOrEmpty(anim.Content))
        {
            using var lyt = CreateTruncatedLayout(anim.Content, "Microsoft YaHei", DWriteFontWeight.Normal, 12, textW, 18);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.75f, 0.75f, 0.75f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 18;
        }

        return Math.Max(iconSize, ty - cy) + 4;
    }

    private float DrawActionsTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var pv = item.State.ParamV2!;
        var actions = pv.TextButton?.Actions ?? pv.Actions ?? [];
        var hint = pv.HintInfo;
        float ty = cy;

        // 提示组件标题/内容
        if (hint != null)
        {
            if (!string.IsNullOrEmpty(hint.Title))
            {
                using var lyt = CreateTruncatedLayout(hint.Title, "Microsoft YaHei", DWriteFontWeight.Bold, 14, contentWidth, 20);
                using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
                rt.DrawTextLayout(new Vector2(cx, ty), lyt, brush);
                ty += 22;
            }
            if (!string.IsNullOrEmpty(hint.SubTitle))
            {
                using var lyt = CreateTruncatedLayout(hint.SubTitle, "Microsoft YaHei", DWriteFontWeight.Normal, 12, contentWidth, 18);
                using var brush = CreateSolidColorBrush(rt, new Color4(0.8f, 0.8f, 0.8f, opacity));
                rt.DrawTextLayout(new Vector2(cx, ty), lyt, brush);
                ty += 20;
            }
            if (hint.ActionInfo != null) actions = [hint.ActionInfo];
        }

        // 按钮行（对齐 Android：按钮 14sp #4A90E2 蓝 / Material3 白字按钮）
        float buttonGap = 8;
        float avail = contentWidth;
        int count = Math.Min(actions.Count, 2);
        if (count > 0)
        {
            float btnW = (avail - buttonGap * (count - 1)) / count;
            float bx = cx;
            foreach (var action in actions.Take(count))
            {
                var title = action.ActionTitle ?? action.Action ?? "按钮";
                var bgColor = ParseHexColor(action.ActionBgColor) ?? new Color4(0.22f, 0.22f, 0.22f, 0.8f);
                var textColor = ParseHexColor(action.ActionTitleColor) ?? new Color4(0.29f, 0.56f, 0.94f, opacity);
                var btnRR = new RoundedRectangle(new RectangleF(bx, ty, btnW, 30), 8, 8);
                using var bgBrush = CreateSolidColorBrush(rt, bgColor);
                rt.FillRoundedRectangle(ref btnRR, bgBrush);
                using var lyt = CreateTruncatedLayout(title, "Microsoft YaHei", DWriteFontWeight.Normal, 14, btnW - 12, 18);
                using var textBrush = CreateSolidColorBrush(rt, textColor);
                rt.DrawTextLayout(new Vector2(bx + 6, ty + 6), lyt, textBrush);
                bx += btnW + buttonGap;
            }
            ty += 34;
        }

        return ty - cy + 2;
    }

    private float DrawMultiProgressTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var mp = item.State.ParamV2!.MultiProgressInfo!;
        float ty = cy;

        // 标题行
        if (!string.IsNullOrEmpty(mp.Title))
        {
            using var lyt = CreateTruncatedLayout(mp.Title, "Microsoft YaHei", DWriteFontWeight.Bold, 14, contentWidth, 20);
            using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
            rt.DrawTextLayout(new Vector2(cx, ty), lyt, brush);
            ty += 24;
        }

        // 轨道（8px 高圆角）+ 前景
        const float barHeight = 8;
        const float nodeSize = 55;
        int points = Math.Clamp(mp.Points ?? 3, 1, 4);
        float nodeGap = (contentWidth - nodeSize) / Math.Max(1, points - 1);
        float barY = ty + nodeSize / 2f - barHeight / 2f;
        float barW = contentWidth;

        // 背景轨道（20% alpha）
        var trackColor = ParseHexColor(mp.Color) ?? new Color4(0.07f, 0.73f, 1.0f, 1.0f);  // 默认 #0ABAFF
        using var trackBrush = CreateSolidColorBrush(rt, new Color4(trackColor.R, trackColor.G, trackColor.B, 0.2f * opacity));
        var trackRR = new RoundedRectangle(new RectangleF(cx, barY, barW, barHeight), barHeight / 2f, barHeight / 2f);
        rt.FillRoundedRectangle(ref trackRR, trackBrush);

        // 前景
        float pct = Math.Clamp(mp.Progress / 100f, 0f, 1f);
        if (pct > 0)
        {
            using var fillBrush = CreateSolidColorBrush(rt, new Color4(trackColor.R, trackColor.G, trackColor.B, 0.9f * opacity));
            var fillRR = new RoundedRectangle(new RectangleF(cx, barY, Math.Max(barHeight, barW * pct), barHeight), barHeight / 2f, barHeight / 2f);
            rt.FillRoundedRectangle(ref fillRR, fillBrush);
        }

        // 节点（无节点图时用 27.5px 圆形指示器）
        const float dotSize = 27.5f;
        for (int i = 0; i < points; i++)
        {
            float nx = cx + nodeGap * i;
            float ncy = cy + nodeSize / 2f;
            bool reached = mp.Progress > 0 && (i == 0 || (float)i / Math.Max(1, points - 1) * 100f <= mp.Progress);
            using var dotBrush = CreateSolidColorBrush(rt, reached
                ? new Color4(trackColor.R, trackColor.G, trackColor.B, 0.9f * opacity)
                : new Color4(0.4f, 0.4f, 0.4f, 0.6f * opacity));
            var dotCenter = new Vector2(nx + dotSize / 2f, ncy);
            var dotEllipse = new Ellipse(dotCenter, dotSize / 2f, dotSize / 2f);
            rt.FillEllipse(dotEllipse, dotBrush);
        }

        return ty + nodeSize - cy + 2;
    }

    private float DrawDefaultTemplate(SuperIslandItem item, ID2D1DCRenderTarget rt, float cx, float cy, float contentWidth, float opacity)
    {
        var state = item.State;
        float ty = cy;
        float iconSize = 28;

        // Icon
        if (item.IconBitmap != null)
        {
            DrawCoverBitmap(rt, item.IconBitmap, cx, cy, iconSize, opacity);
        }

        float tx = item.IconBitmap != null ? cx + iconSize + 8 : cx;
        float textW = contentWidth - (tx - cx);

        // Title + Subtitle 一行
        string titleText = state.Title ?? "";
        if (!string.IsNullOrEmpty(state.Subtitle))
            titleText = string.IsNullOrEmpty(titleText) ? state.Subtitle : $"{titleText} · {state.Subtitle}";
        if (!string.IsNullOrEmpty(titleText))
        {
            using var lyt = CreateTruncatedLayout(titleText, "Microsoft YaHei", DWriteFontWeight.SemiBold, 14, textW, 22);
            using var brush = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 22;
        }

        // 计时器（右侧）
        string timerText = state.GetDisplayTime();
        string? progressText = state.GetProgressText();
        string rightText = !string.IsNullOrEmpty(timerText) ? timerText : progressText ?? "";
        if (!string.IsNullOrEmpty(rightText))
        {
            using var fmt = CreateTextFormat("Consolas", DWriteFontWeight.Normal, 11);
            using var lyt = _dwFactory.CreateTextLayout(rightText, fmt, 70, 16);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(cx + contentWidth - 70, cy + 2), lyt, brush);
        }

        // AdditionalText
        if (!string.IsNullOrEmpty(state.AdditionalText))
        {
            using var lyt = CreateTruncatedLayout(state.AdditionalText, "Microsoft YaHei", DWriteFontWeight.Normal, 11, textW, 18);
            using var brush = CreateSolidColorBrush(rt, new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), lyt, brush);
            ty += 18;
        }

        // Extra（展开时第三行）
        if (state.HasExtra && item.ExtraLayout != null)
        {
            using var brush = CreateSolidColorBrush(rt, new Color4(0.6f, 0.8f, 1.0f, opacity));
            rt.DrawTextLayout(new Vector2(tx, ty), item.ExtraLayout, brush);
            ty += 20;
        }

        // 进度条（底部）
        if (state.HasProgress)
        {
            float progY = ty + 6;
            using var progBg = CreateSolidColorBrush(rt, new Color4(0.35f, 0.35f, 0.35f, opacity * 0.6f));
            var progBgRR = new RoundedRectangle(new RectangleF(cx, progY, contentWidth, 3), 1.5f, 1.5f);
            rt.FillRoundedRectangle(ref progBgRR, progBg);
            float pct = Math.Clamp(state.Progress / 100f, 0f, 1f);
            using var progFill = CreateSolidColorBrush(rt, new Color4(0.3f, 0.7f, 1.0f, opacity));
            var progFillRR = new RoundedRectangle(new RectangleF(cx, progY, contentWidth * pct, 3), 1.5f, 1.5f);
            rt.FillRoundedRectangle(ref progFillRR, progFill);
            ty += 12;
        }

        return Math.Max(iconSize, ty - cy) + 2;
    }

    // ---------- 绘制辅助 ----------

    /// <summary>测量单行文本宽度。</summary>
    private float MeasureTextWidth(string text, string fontFamily, DWriteFontWeight weight, float size)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        using var fmt = CreateTextFormat(fontFamily, weight, size);
        using var lyt = _dwFactory.CreateTextLayout(text, fmt, 10000, 40);
        lyt.WordWrapping = WordWrapping.NoWrap;
        return lyt.Metrics.WidthIncludingTrailingWhitespace;
    }

    private float MeasureTextHeight(string text, float size)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return size + 6;
    }

    /// <summary>解析 #RRGGBB 颜色，失败返回 null。</summary>
    private static Color4? ParseHexColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        hex = hex.TrimStart('#');
        if (hex.Length == 6 && byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return new Color4(r / 255f, g / 255f, b / 255f, 1.0f);
        }
        return null;
    }

    /// <summary>圆形裁剪绘制头像。</summary>
    private void DrawCircleCroppedBitmap(ID2D1DCRenderTarget rt, ID2D1Bitmap bitmap, float x, float y, float size, float opacity)
    {
        float r = size / 2f;
        var center = new Vector2(x + r, y + r);
        var ellipse = new Ellipse(center, r, r);
        using var ellipseGeom = _d2dFactory.CreateEllipseGeometry(ellipse);
        var layerParams = new LayerParameters
        {
            ContentBounds = new RectangleF(x, y, size, size),
            GeometricMask = ellipseGeom,
            MaskAntialiasMode = AntialiasMode.PerPrimitive
        };
        using var layer = rt.CreateLayer();
        rt.PushLayer(layerParams, layer);
        try
        {
            DrawCoverBitmap(rt, bitmap, x, y, size, opacity);
        }
        finally
        {
            rt.PopLayer();
        }
    }

    /// <summary>圆形占位符（图标加载失败时）。</summary>
    private void DrawCirclePlaceholder(ID2D1DCRenderTarget rt, float x, float y, float size, float opacity)
    {
        using var brush = CreateSolidColorBrush(rt, new Color4(0.5f, 0.5f, 0.5f, 0.5f * opacity));
        var center = new Vector2(x + size / 2f, y + size / 2f);
        var ellipse = new Ellipse(center, size / 2f, size / 2f);
        rt.FillEllipse(ellipse, brush);
    }

    /// <summary>绘制进度圆环（B 区 progressTextInfo）。</summary>
    private void DrawProgressRing(ID2D1DCRenderTarget rt, float x, float centerY, float size, float strokeWidth,
        int progress, string? colorReachHex, string? colorUnReachHex, float opacity)
    {
        float r = size / 2f;
        var center = new Vector2(x + r, centerY);

        var reachColor = ParseHexColor(colorReachHex) ?? new Color4(0.3f, 0.7f, 1.0f, 1.0f);
        var unReachColor = ParseHexColor(colorUnReachHex) ?? new Color4(0.35f, 0.35f, 0.35f, 1.0f);

        // 未达部分（整圆底）
        using var unReachBrush = CreateSolidColorBrush(rt, new Color4(unReachColor.R, unReachColor.G, unReachColor.B, opacity));
        var ringEllipse = new Ellipse(center, r, r);
        rt.DrawEllipse(ringEllipse, unReachBrush, strokeWidth);

        // 已达部分（圆弧，从 12 点方向顺时针，用 PathGeometry 绘制弧段）
        if (progress > 0 && progress < 100)
        {
            float sweep = progress / 100f * 360f;
            using var reachBrush = CreateSolidColorBrush(rt, new Color4(reachColor.R, reachColor.G, reachColor.B, opacity));
            DrawRingArc(rt, center, r, sweep, reachBrush, strokeWidth);
        }
        else if (progress >= 100)
        {
            using var reachBrush = CreateSolidColorBrush(rt, new Color4(reachColor.R, reachColor.G, reachColor.B, opacity));
            rt.DrawEllipse(ringEllipse, reachBrush, strokeWidth);
        }
    }

    /// <summary>以 12 点方向为起点顺时针绘制指定角度（度）的圆弧描边。</summary>
    private void DrawRingArc(ID2D1DCRenderTarget rt, Vector2 center, float radius, float sweepDegrees,
        ID2D1Brush brush, float strokeWidth)
    {
        float startAngle = -90f * MathF.PI / 180f;
        float sweepRad = sweepDegrees * MathF.PI / 180f;
        var start = new Vector2(center.X + radius * MathF.Cos(startAngle), center.Y + radius * MathF.Sin(startAngle));
        var end = new Vector2(center.X + radius * MathF.Cos(startAngle + sweepRad), center.Y + radius * MathF.Sin(startAngle + sweepRad));

        using var pathGeom = _d2dFactory.CreatePathGeometry();
        using (var sink = pathGeom.Open())
        {
            sink.BeginFigure(start, FigureBegin.Hollow);
            sink.AddArc(new Vortice.Direct2D1.ArcSegment(end, new Vortice.Mathematics.Size(radius, radius), 0,
                Vortice.Direct2D1.SweepDirection.Clockwise,
                sweepDegrees > 180 ? Vortice.Direct2D1.ArcSize.Large : Vortice.Direct2D1.ArcSize.Small));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        rt.DrawGeometry(pathGeom, brush, strokeWidth);
    }
}
