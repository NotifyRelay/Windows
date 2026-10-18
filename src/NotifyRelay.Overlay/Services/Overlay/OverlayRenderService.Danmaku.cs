using System.Numerics;
using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using BitmapInterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 弹幕命令式绘制（保留）：轨道分配与跨屏分发依赖窗口集合与时间推进，不是「静态 UI 描述」，
/// 声明式化收益低、风险高，故维持直接 D2D 调用，仅从 Rendering.cs 拆出独立 partial。
/// </summary>
public partial class OverlayRenderService
{
    private void EnsureDanmakuResources(DanmakuItem item, ID2D1DCRenderTarget rt)
    {
        if (item.TextLayout == null)
        {
            var s = item.Settings;
            using var format = CreateTextFormat(s.FontFamilyName,
                s.Bold ? DWriteFontWeight.Bold : DWriteFontWeight.Normal, (float)s.FontSize);

            item.TextLayout = _dwFactory.CreateTextLayout(
                item.Text, format, float.MaxValue, (float)s.FontSize * 2);

            var metrics = item.TextLayout.Metrics;
            item.TextWidth = metrics.Width;
            item.TextHeight = metrics.Height;
            item.TotalWidth = item.TextWidth
                + (item.IconPng != null ? (float)s.FontSize + 8 : 0) + 20;
        }

        if (item.IconBitmap == null && item.IconPng != null)
        {
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, rt); }
            catch
            {
                item.IconPng = null;
            }
        }
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
            var srcRect = new Vortice.Mathematics.Rect(0, 0,
                (int)item.IconBitmap.Size.Width, (int)item.IconBitmap.Size.Height);
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

    /// <summary>
    /// 为弹幕分配轨道。使用"三角装箱"式判定修复弹幕重叠：
    /// 既要求同轨道上一条弹幕已进入足够距离，又要保证较快的新弹幕不会在
    /// 前一条离场前追尾重叠。分配失败时返回 false，调用方保留在待发队列。
    /// </summary>
    private bool TryAssignTrack(DanmakuItem item, ScreenOverlay overlay)
    {
        var s = item.Settings;
        double trackHeight = Math.Max(s.FontSize + 24, s.FontSize * 1.5);
        double avail = Math.Max(trackHeight, overlay.Height - overlay.TopOffset);
        int totalTracks = Math.Max(1, (int)(avail / trackHeight));
        int activeCount = Math.Clamp(
            (int)(totalTracks * (s.DisplayAreaPercent / 100.0)), 1, totalTracks);

        double minGap = s.Density switch { 1 => 20, 2 => -300, _ => 100 };
        bool allowOverlap = s.Density == 2;
        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;
        double width = overlay.Width;
        double vNew = s.PixelsPerSecond;

        var candidates = new List<int>();
        for (int i = 0; i < activeCount; i++)
        {
            bool ok = true;
            foreach (var existing in overlay.Items)
            {
                if (existing.TrackIndex != i || !existing.Active) continue;
                double elapsed = (now - existing.StartTime) / freq;
                double vOld = existing.Settings.PixelsPerSecond;
                double rightEdge = existing.SpawnX - elapsed * vOld + existing.TotalWidth;

                // 初始间距不足
                if (rightEdge > width - minGap) { ok = false; break; }

                // 追尾判定：新弹幕更快时，检查其是否会在前一条离场前追上
                if (vNew > vOld)
                {
                    double tCatch = (width - rightEdge) / (vNew - vOld);
                    double tExit = rightEdge / vOld;
                    if (tCatch < tExit) { ok = false; break; }
                }
            }
            if (ok) candidates.Add(i);
        }

        int track;
        if (candidates.Count > 0)
        {
            track = candidates[_rand.Next(candidates.Count)];
        }
        else if (allowOverlap)
        {
            track = _rand.Next(activeCount);
        }
        else
        {
            return false;
        }

        item.TrackIndex = track;
        item.TrackY = (float)(overlay.TopOffset + track * trackHeight);
        item.SpawnX = overlay.Width;
        return true;
    }

    /// <summary>从待发队列尝试将弹幕分配到空闲轨道。</summary>
    private void SpawnPending(ScreenOverlay overlay, ID2D1DCRenderTarget rt)
    {
        while (overlay.Pending.Count > 0)
        {
            var item = overlay.Pending.Peek();
            EnsureDanmakuResources(item, rt);
            if (TryAssignTrack(item, overlay))
            {
                item.StartTime = Stopwatch.GetTimestamp();
                overlay.Pending.Dequeue();
                overlay.Items.Add(item);
            }
            else
            {
                break;
            }
        }
    }
}
