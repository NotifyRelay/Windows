using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.WIC;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

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

    private void EnsureMediaResources(MediaCardItem item, ID2D1DCRenderTarget rt)
    {
        if (item.CoverBitmap == null && item.CoverPng != null)
        {
            try
            {
                item.CoverBitmap = LoadBitmapFromPng(item.CoverPng, rt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EnsureMediaResources: 封面图片加载失败, 数据长度={Length}", item.CoverPng?.Length);
            }
        }

        if (item.TitleLayout == null && !string.IsNullOrEmpty(item.Title))
        {
            using var format = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Bold, 18);
            item.TitleLayout = _dwFactory.CreateTextLayout(item.Title, format, 400, 30);
        }

        if (item.ArtistLayout == null && !string.IsNullOrEmpty(item.Artist))
        {
            using var format = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 14);
            item.ArtistLayout = _dwFactory.CreateTextLayout(item.Artist, format, 400, 25);
        }
    }

    private void EnsureSuperIslandResources(SuperIslandItem item, ID2D1DCRenderTarget rt)
    {
        if (item.IconBitmap == null && item.IconPng != null)
        {
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, rt); }
            catch { }
        }

        if (item.TitleLayout == null && !string.IsNullOrEmpty(item.State.Title))
        {
            using var format = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Bold, 16);
            item.TitleLayout = _dwFactory.CreateTextLayout(item.State.Title, format, 400, 25);
        }

        if (item.SubtitleLayout == null && !string.IsNullOrEmpty(item.State.Subtitle))
        {
            using var format = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 13);
            item.SubtitleLayout = _dwFactory.CreateTextLayout(item.State.Subtitle, format, 400, 22);
        }

        if (item.AdditionalTextLayout == null && !string.IsNullOrEmpty(item.State.AdditionalText))
        {
            using var format = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 12);
            item.AdditionalTextLayout = _dwFactory.CreateTextLayout(item.State.AdditionalText, format, 400, 20);
        }

        if (item.ExtraLayout == null && !string.IsNullOrEmpty(item.State.Extra))
        {
            using var format = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.Normal, 11);
            item.ExtraLayout = _dwFactory.CreateTextLayout(item.State.Extra, format, 380, 20);
        }
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

    private ID2D1Bitmap? LoadBitmapFromPng(byte[] pngData, ID2D1DCRenderTarget rt)
    {
        using var stream = _wicFactory.CreateStream();
        stream.Initialize(pngData);
        using var decoder = _wicFactory.CreateDecoderFromStream(stream, DecodeOptions.CacheOnLoad);
        using var frame = decoder.GetFrame(0);
        using var converter = _wicFactory.CreateFormatConverter();
        converter.Initialize(frame, Vortice.WIC.PixelFormat.Format32bppPBGRA);

        var size = converter.Size;
        int stride = size.Width * 4;
        byte[] pixels = new byte[stride * size.Height];

        unsafe
        {
            fixed (byte* pPixels = pixels)
            {
                converter.CopyPixels(new RectI(0, 0, size.Width, size.Height), (uint)stride, (uint)pixels.Length, (IntPtr)pPixels);
            }
        }

        var props = new BitmapProperties(new Vortice.DCommon.PixelFormat(
            Vortice.DXGI.Format.B8G8R8A8_UNorm,
            Vortice.DCommon.AlphaMode.Premultiplied));
        var bitmap = rt.CreateBitmap(size, props);

        unsafe
        {
            fixed (byte* pPixels = pixels)
            {
                bitmap.CopyFromMemory((IntPtr)pPixels, (uint)stride);
            }
        }
        return bitmap;
    }
}
