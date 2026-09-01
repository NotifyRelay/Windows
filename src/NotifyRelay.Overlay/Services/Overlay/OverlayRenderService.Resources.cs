using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using Vortice.WIC;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

public partial class OverlayRenderService
{
    /// <summary>网络 URL 图片下载用 HttpClient（复用，避免每帧重建）。</summary>
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

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

        // 多图位图槽：按需懒加载，失败键记入 FailedPicKeys 避免每帧重复解析
        var pics = item.State.Pics;
        if (pics == null || pics.Count == 0) return;

        var pv = item.State.ParamV2;
        item.AvatarBitmap = EnsurePicBitmap(item, item.AvatarBitmap, pics, pv?.ChatInfo?.PicProfile, rt);
        item.BigImageLeftBitmap = EnsurePicBitmap(item, item.BigImageLeftBitmap, pics,
            pv?.HighlightInfo?.BigImageLeft ?? ResolveFocusPicKey(pics, pv?.HighlightInfo?.PicFunction), rt);
        item.BigImageRightBitmap = EnsurePicBitmap(item, item.BigImageRightBitmap, pics, pv?.HighlightInfo?.BigImageRight, rt);
        item.PicInfoBitmap = EnsurePicBitmap(item, item.PicInfoBitmap, pics, pv?.PicInfo?.Pic, rt);
        var island = pv?.ParamIsland?.BigIslandArea;
        item.LeftIconBitmap = EnsurePicBitmap(item, item.LeftIconBitmap, pics,
            island?.LeftImage ?? ResolveFocusPicKey(pics, island?.AComponent?.PicKey), rt);
        item.RightIconBitmap = EnsurePicBitmap(item, item.RightIconBitmap, pics,
            island?.RightImage ?? GetBComponentPicKey(pv), rt);

        // multiProgressInfo 节点与指针图（对齐 Android MultiProgressCompose）
        var mp = pv?.MultiProgressInfo;
        item.MultiForwardBitmap = EnsurePicBitmap(item, item.MultiForwardBitmap, pics, mp?.PicForward, rt);
        item.MultiForwardBoxBitmap = EnsurePicBitmap(item, item.MultiForwardBoxBitmap, pics, mp?.PicForwardBox, rt);
        item.MultiMiddleBitmap = EnsurePicBitmap(item, item.MultiMiddleBitmap, pics, mp?.PicMiddle, rt);
        item.MultiMiddleUnselBitmap = EnsurePicBitmap(item, item.MultiMiddleUnselBitmap, pics, mp?.PicMiddleUnselected, rt);
        item.MultiEndBitmap = EnsurePicBitmap(item, item.MultiEndBitmap, pics, mp?.PicEnd, rt);
        item.MultiEndUnselBitmap = EnsurePicBitmap(item, item.MultiEndUnselBitmap, pics, mp?.PicEndUnselected, rt);

        // 网络 URL 图片：本帧发起异步下载（不阻塞渲染线程），下帧起生效
        EnsureUrlPics(item, pics);
    }

    /// <summary>发起 Pics 中网络 URL 图片的异步下载（去重），完成后写入 UrlPngCache。</summary>
    private void EnsureUrlPics(SuperIslandItem item, Dictionary<string, string> pics)
    {
        foreach (var kv in pics)
        {
            if (!IsHttpUrl(kv.Value)) continue;
            if (item.UrlPngCache.ContainsKey(kv.Key)
                || !item.UrlFetching.TryAdd(kv.Key, 0)
                || item.FailedPicKeys.Contains(kv.Key))
            {
                continue;
            }
            var key = kv.Key;
            var url = kv.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var bytes = await s_httpClient.GetByteArrayAsync(url, cts.Token);
                    if (bytes is { Length: > 0 })
                    {
                        lock (item.UrlPngCache) item.UrlPngCache[key] = bytes;
                    }
                    else
                    {
                        lock (item.FailedPicKeys) item.FailedPicKeys.Add(key);
                    }
                }
                catch
                {
                    lock (item.FailedPicKeys) item.FailedPicKeys.Add(key);
                }
                finally
                {
                    item.UrlFetching.TryRemove(key, out _);
                }
            });
        }
    }

    private static bool IsHttpUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>从 Pics 字典按 key 懒加载位图；网络 URL 走异步下载缓存，失败键记忆避免重复。</summary>
    private ID2D1Bitmap? EnsurePicBitmap(SuperIslandItem item, ID2D1Bitmap? current, Dictionary<string, string> pics,
        string? key, ID2D1DCRenderTarget rt)
    {
        if (current != null) return current;
        if (string.IsNullOrEmpty(key) || !pics.TryGetValue(key, out var value)) return null;
        lock (item.FailedPicKeys)
        {
            if (item.FailedPicKeys.Contains(key)) return null;
        }
        if (IsHttpUrl(value))
        {
            // 网络 URL：下载完成后从 UrlPngCache 解码（EnsureUrlPics 已发起异步下载）
            lock (item.UrlPngCache)
            {
                if (!item.UrlPngCache.TryGetValue(key, out var png))
                {
                    return null;
                }
                try
                {
                    return LoadBitmapFromPng(png, rt);
                }
                catch
                {
                    lock (item.FailedPicKeys) item.FailedPicKeys.Add(key);
                    return null;
                }
            }
        }
        try
        {
            var png = DecodePicBytes(value);
            if (png == null)
            {
                lock (item.FailedPicKeys) item.FailedPicKeys.Add(key);
                return null;
            }
            return LoadBitmapFromPng(png, rt);
        }
        catch
        {
            lock (item.FailedPicKeys) item.FailedPicKeys.Add(key);
            return null;
        }
    }

    /// <summary>解析 B 区组件的图片键（仅显示位图的组件）。</summary>
    private static string? GetBComponentPicKey(ParamV2? pv)
    {
        var b = pv?.ParamIsland?.BigIslandArea?.BComponent;
        return b switch
        {
            BImageTextData img => img.PicKey,
            BPicInfoData pic => pic.PicKey,
            _ => null
        };
    }

    /// <summary>
    /// 焦点图标键解析（对齐 Android FocusIconResolver.getFocusIconUrl）：
    /// 主键命中优先；否则按 expand 主题图标 → aod/ado_pic/app_icon → ic_* → 其余 pic_* 顺序回退。
    /// </summary>
    private static string? ResolveFocusPicKey(Dictionary<string, string> pics, string? primaryKey)
    {
        if (!string.IsNullOrEmpty(primaryKey) && primaryKey.StartsWith("miui.focus.", StringComparison.OrdinalIgnoreCase)
            && pics.ContainsKey(primaryKey))
        {
            return primaryKey;
        }
        foreach (var k in new[] { "miui.focus.pic_expand_light", "miui.focus.pic_expand_dark" })
        {
            if (pics.ContainsKey(k)) return k;
        }
        foreach (var k in new[] { "miui.focus.pic_aod", "miui.focus.pic_ado_pic", "miui.focus.pic_app_icon" })
        {
            if (pics.ContainsKey(k)) return k;
        }
        foreach (var k in pics.Keys)
        {
            if (k.StartsWith("miui.focus.ic_", StringComparison.OrdinalIgnoreCase)) return k;
        }
        foreach (var k in pics.Keys)
        {
            if (k.StartsWith("miui.focus.pic_", StringComparison.OrdinalIgnoreCase)
                && !k.Equals("miui.focus.pics", StringComparison.OrdinalIgnoreCase)) return k;
        }
        return null;
    }

    /// <summary>将 Pics 值（data URL 或纯 base64）解码为 PNG 字节，失败返回 null。</summary>
    private static byte[]? DecodePicBytes(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var s = value;
        var idx = s.IndexOf(',');
        if (idx > 0 && s.AsSpan(0, idx).Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            s = s[(idx + 1)..];
        }
        try { return Convert.FromBase64String(s); }
        catch { return null; }
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
