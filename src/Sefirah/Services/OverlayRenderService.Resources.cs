using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.WIC;
using NotifyRelay.Models.Render;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services;

public partial class OverlayRenderService
{
    private void EnsureDanmakuResources(DanmakuItem item)
    {
        if (item.TextLayout == null)
        {
            var s = _currentStyle;
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
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, _renderTarget!); }
            catch
            {
                item.IconPng = null;
            }
        }
    }

    private void EnsureMediaResources(MediaCardItem item)
    {
        if (item.CoverBitmap == null && item.CoverPng != null)
        {
            try
            {
                _logger.LogDebug("EnsureMediaResources: 尝试加载封面图片, 数据长度={Length}", item.CoverPng.Length);
                item.CoverBitmap = LoadBitmapFromPng(item.CoverPng, _renderTarget!);
                _logger.LogDebug("EnsureMediaResources: 封面加载成功, 尺寸={Width}x{Height}",
                    item.CoverBitmap?.Size.Width, item.CoverBitmap?.Size.Height);
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

    private void EnsureSuperIslandResources(SuperIslandItem item)
    {
        if (item.IconBitmap == null && item.IconPng != null)
        {
            try { item.IconBitmap = LoadBitmapFromPng(item.IconPng, _renderTarget!); }
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

    private void AssignTrack(DanmakuItem item)
    {
        var s = _currentStyle;
        double trackHeight = s.FontSize + 24;
        int totalTracks = Math.Max(1, (int)(_height / trackHeight));
        int activeCount = Math.Clamp(
            (int)(totalTracks * (s.DisplayAreaPercent / 100.0)), 1, totalTracks);

        double minGap = s.Density switch { 1 => 20, 2 => -300, _ => 100 };
        double now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        var available = new List<int>();
        for (int i = 0; i < activeCount; i++)
        {
            bool occupied = false;
            foreach (var existing in _items.OfType<DanmakuItem>())
            {
                if (existing.TrackIndex != i || !existing.Active) continue;
                double elapsed = (now - existing.StartTime) / freq;
                double rightEdge = existing.SpawnX
                    - elapsed * existing.Settings.PixelsPerSecond + existing.TotalWidth;
                if (rightEdge > _width - minGap) { occupied = true; break; }
            }
            if (!occupied) available.Add(i);
        }

        int track = available.Count > 0
            ? available[Random.Shared.Next(available.Count)]
            : Random.Shared.Next(0, activeCount);

        item.TrackIndex = track;
        item.TrackY = (float)(260 + track * trackHeight);
        item.SpawnX = _width;
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
