using System;
using System.Numerics;
using System.Drawing;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using NotifyRelay.Models.Render;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using DWriteFontStretch = Vortice.DirectWrite.FontStretch;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    private void RenderTopCards(double now, double freq)
    {
        float y = 10;
        var mediaItems = _items.OfType<MediaCardItem>().Where(m => m.Active).ToList();
        var superItems = _items.OfType<SuperIslandItem>().Where(s => s.Active).ToList();

        // Remove timed out items
        for (int i = mediaItems.Count - 1; i >= 0; i--)
        {
            var elapsed = (now - mediaItems[i].LastUpdateTime) / freq;
            if (elapsed > MediaCardItem.TimeoutSeconds)
            {
                mediaItems[i].Active = false;
                mediaItems[i].Dispose();
                _items.Remove(mediaItems[i]);
                mediaItems.RemoveAt(i);
            }
        }
        for (int i = superItems.Count - 1; i >= 0; i--)
        {
            var elapsed = (now - superItems[i].LastUpdateTime) / freq;
            if (elapsed > SuperIslandItem.TimeoutSeconds)
            {
                superItems[i].Active = false;
                superItems[i].Dispose();
                _items.Remove(superItems[i]);
                superItems.RemoveAt(i);
            }
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

            EnsureMediaResources(media);
            DrawMediaCard(media, _renderTarget!, y, now, freq);
            y += media.IsExpanded ? 108 : 48;
        }

        // Render SuperIsland cards (max 3) — 居中灵动岛胶囊
        foreach (var si in superItems.Take(3))
        {
            // 自动收起：Extra 信息展示后 5 秒自动收起为紧凑模式
            if (si.IsExpanded && si.State.HasExtra && (now - si.ExpandedSince) / freq > SuperIslandItem.AutoCollapseSeconds)
            {
                si.IsExpanded = false;
            }

            EnsureSuperIslandResources(si);
            DrawSuperIslandCard(si, _renderTarget!, y);
            y += si.IsExpanded ? 110 : 84;
        }
    }

    private void RenderDanmakuItems(double now, double freq)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i] is DanmakuItem item)
            {
                if (!item.Active) continue;
                double elapsed = (now - item.StartTime) / freq;
                double x = item.SpawnX - elapsed * item.Settings.PixelsPerSecond;

                if (x < -item.TotalWidth - 50)
                {
                    item.Dispose();
                    _items.RemoveAt(i);
                    continue;
                }

                EnsureDanmakuResources(item);
                DrawDanmaku(item, (float)x, _renderTarget!);
            }
        }
    }

    private void DrawDanmaku(DanmakuItem item, float x, ID2D1DCRenderTarget rt)
    {
        if (item.TextLayout == null) return;

        var s = _currentStyle;
        float y = item.TrackY;
        float opacity = s.Opacity;
        float iconOffset = 0;

        if (item.IconBitmap != null)
        {
            float iconSize = (float)s.FontSize;
            var destRect = new Vortice.Mathematics.Rect((int)(x + 10), (int)y, (int)iconSize, (int)iconSize);
            rt.DrawBitmap(item.IconBitmap, opacity, BitmapInterpolationMode.Linear, destRect);
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

    private void DrawMediaCard(MediaCardItem item, ID2D1DCRenderTarget rt, float y, double now, double freq)
    {
        if (!item.IsExpanded)
        {
            DrawMediaCardCollapsed(item, rt, y, now, freq);
            return;
        }

        const float pillWidth = 400;
        const float pillHeight = 100;
        float pillX = (_width - pillWidth) / 2;
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
    private void DrawMediaCardCollapsed(MediaCardItem item, ID2D1DCRenderTarget rt, float y, double now, double freq)
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
        float pillX = (_width - pillWidth) / 2;

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

    private void DrawSuperIslandCard(SuperIslandItem item, ID2D1DCRenderTarget rt, float y)
    {
        const float pillWidth = 380;
        float pillHeight = item.IsExpanded && item.State.HasExtra ? 102 : 76;
        float pillX = (_width - pillWidth) / 2;
        float pad = 14;
        float opacity = 0.9f;

        // Pill background
        DrawPillBackground(rt, pillX, y, pillWidth, pillHeight, 16, opacity);

        float cx = pillX + pad;
        float textW = pillWidth - pad * 2;

        // Icon (28x28)
        if (item.IconBitmap != null)
        {
            var iconRect = new Vortice.Mathematics.Rect((int)cx, (int)y + 10, 28, 28);
            rt.DrawBitmap(item.IconBitmap, opacity, BitmapInterpolationMode.Linear, iconRect);
            cx += 36;
            textW -= 36;
        }

        // Title + Subtitle in one line
        string titleText = item.State.Title ?? "";
        if (!string.IsNullOrEmpty(item.State.Subtitle))
            titleText = string.IsNullOrEmpty(titleText) ? item.State.Subtitle : $"{titleText} · {item.State.Subtitle}";

        using var titleFmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.SemiBold, 14);
        float titleMaxW = textW - 80; // 留出右侧计时器空间
        using var titleLyt = _dwFactory.CreateTextLayout(titleText, titleFmt, Math.Max(titleMaxW, 60), 22);
        using var titleBr = CreateSolidColorBrush(rt, new Color4(1, 1, 1, opacity));
        rt.DrawTextLayout(new Vector2(cx, y + 10), titleLyt, titleBr);

        float nextLineY = y + 34; // 第二行起始 Y

        // Timer（右侧）
        string timerText = item.State.GetDisplayTime();
        string? progressText = item.State.GetProgressText();
        string rightText = !string.IsNullOrEmpty(timerText) ? timerText : progressText ?? "";
        if (!string.IsNullOrEmpty(rightText))
        {
            using var timeFmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 11);
            using var timeLyt = _dwFactory.CreateTextLayout(rightText, timeFmt, 70, 16);
            using var timeBr = CreateSolidColorBrush(rt, new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(pillX + pillWidth - pad - 70, y + 11), timeLyt, timeBr);
        }

        // Additional text line（第二行）
        if (!string.IsNullOrEmpty(item.State.AdditionalText))
        {
            using var addFmt = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 11);
            using var addLyt = _dwFactory.CreateTextLayout(item.State.AdditionalText, addFmt, textW, 18);
            using var addBr = CreateSolidColorBrush(rt, new Color4(0.7f, 0.7f, 0.7f, opacity));
            rt.DrawTextLayout(new Vector2(cx, nextLineY), addLyt, addBr);
        }

        // Extra line（展开时第三行，从 ParamV2 解析出的结构化信息）
        if (item.IsExpanded && item.State.HasExtra)
        {
            float extraY = string.IsNullOrEmpty(item.State.AdditionalText) ? nextLineY : nextLineY + 20;
            if (item.ExtraLayout != null)
            {
                using var extraBr = CreateSolidColorBrush(rt, new Color4(0.6f, 0.8f, 1.0f, opacity));
                rt.DrawTextLayout(new Vector2(cx, extraY), item.ExtraLayout, extraBr);
            }
        }

        // Progress bar（底部）
        if (item.State.HasProgress)
        {
            using var progBg = CreateSolidColorBrush(rt, new Color4(0.35f, 0.35f, 0.35f, opacity * 0.6f));
            float progY = y + pillHeight - 10;
            float progW = pillWidth - pad * 2;
            var progBgRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, progW, 3), 1.5f, 1.5f);
            rt.FillRoundedRectangle(ref progBgRR, progBg);

            float pct = Math.Clamp(item.State.Progress / 100f, 0f, 1f);
            float fillW = progW * pct;
            using var progFill = CreateSolidColorBrush(rt, new Color4(0.3f, 0.7f, 1.0f, opacity));
            var progFillRR = new RoundedRectangle(new RectangleF(pillX + pad, progY, fillW, 3), 1.5f, 1.5f);
            rt.FillRoundedRectangle(ref progFillRR, progFill);
        }
    }
}
