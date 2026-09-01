using System.Drawing;
using System.Numerics;
using System.Text;
using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using LogiDevice = NotifyRelay.Models.Render.LogiBatteryDeviceInfo;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// OverlayRenderService 的罗技电池叠加层渲染 partial。
/// 渲染内容：电池图标（Segoe MDL2 Assets，共享 BatteryIconUtility 颜色/字形） + 设备名（过长自动省略号截断）。
/// 不渲染电量%数字：因为图标字形分 6 段 + 颜色编码（红/黄/绿）已经表达电量范围，避免信息冗余。
/// 设备名来源：FFI 原始名或用户在设置页手动修改的 Override。
/// </summary>
public partial class OverlayRenderService
{
    // ===== 罗技电池相关 =====
    private ILogiBatteryProvider? _logiBatteryProvider;
    private List<LogiBatteryDeviceInfo> _logiBatteryDevices = [];

    // 渲染资源缓存：避免每帧创建 DirectWrite 文本格式 / 文本布局 / 画刷对象，
    // 也避免每帧对设备名做二分查找截断。
    // 文本格式与文本布局由 _dwFactory 创建，与渲染目标无关，可跨窗口复用；
    // 画刷由具体渲染目标(rt)创建，与 rt 绑定，故在 rt 变化时重建。
    // 失效条件：设备数据版本变化(DevicesUpdated / Provider 切换)、scale 变化、
    // nameMaxWidth 变化、渲染目标变化。
    private IDWriteTextFormat? _logiIconFormat;
    private IDWriteTextFormat? _logiTextFormat;
    private ID2D1SolidColorBrush? _logiBgBrush;
    private ID2D1SolidColorBrush? _logiBorderBrush;
    private ID2D1SolidColorBrush? _logiTextBrush;
    private ID2D1DCRenderTarget? _logiBrushRt;       // 画刷归属的渲染目标
    private float _logiCacheScale = -1f;
    private float _logiCacheNameMaxWidth = -1f;
    private long _logiCacheVersion = -1;             // 已构建缓存对应的设备数据版本
    private long _logiDataVersion;                   // 设备数据版本（快照更新时自增）
    private readonly Dictionary<string, LogiBatteryRenderEntry> _logiDeviceCaches = new();

    /// <summary>单台设备缓存的渲染资源（与 rt 无关的资源 + 需随 rt 重建的图标画刷）。</summary>
    private sealed class LogiBatteryRenderEntry
    {
        public IDWriteTextLayout? NameLayout;
        public IDWriteTextLayout? IconLayout;
        public ID2D1SolidColorBrush? IconBrush;
        public string DisplayName = string.Empty;
        public float NameWidth;
        public Color4 BatteryColor;
        public float Scale = -1f;
        public float NameMaxWidth = -1f;
    }

    // 渲染尺寸常量（最终乘以 Scale）
    private const float LogiCardPaddingX = 12f;
    private const float LogiCardPaddingY = 8f;
    private const float LogiCardSpacing = 6f;
    private const float LogiCardCornerRadius = 7f;
    private const float LogiIconSize = 20f;        // Segoe MDL2 Assets 图标字号
    private const float LogiTextSize = 13f;         // 设备名字号
    private const float LogiIconTextGap = 8f;       // 图标与设备名间距
    private const float LogiMaxDeviceNameChars = 24;// 单卡片设备名最大字符估计（实际用像素宽度限制+省略号）
    private const float LogiCardMaxWidthFactor = 0.35f; // 单卡片最大宽度 = 屏幕宽度 × 此系数（防止长设备名撑满屏幕）

    /// <summary>注入罗技电池数据提供者（DI 启动后调用）。</summary>
    public void SetLogiBatteryProvider(ILogiBatteryProvider? provider)
    {
        lock (_lock)
        {
            // 退订旧 Provider、订阅新 Provider 走共用模板（与键盘等元素一致）
            OverlayElementCore.ReplaceProvider(ref _logiBatteryProvider, provider,
                p => p.DevicesUpdated += OnLogiBatteryDevicesUpdated,
                p => p.DevicesUpdated -= OnLogiBatteryDevicesUpdated);
            _logiBatteryDevices = provider != null ? provider.GetDevices().ToList() : [];
            _logiDataVersion++;
            _displayDirty = true;
        }
    }

    private void OnLogiBatteryDevicesUpdated(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_logiBatteryProvider != null)
                _logiBatteryDevices = _logiBatteryProvider.GetDevices().ToList();
            _logiDataVersion++;
            _displayDirty = true;
        }
    }

    /// <summary>
    /// 判断指定覆盖层窗口是否为罗技电池的目标屏。
    /// 目标取值：primary（主屏） / span（跨屏窗口） / 具体显示器 DeviceName（如 \\.\DISPLAY2）。
    /// 具体显示器按 DeviceName 精确匹配，匹配不到时回退主屏，
    /// 与心率覆盖层 IsHeartRateTarget 的行为保持一致。
    /// 跨屏窗口不参与 DeviceName 匹配；span 目标时只有跨屏窗口命中。
    /// </summary>
    private bool IsLogiBatteryTarget(ScreenOverlay o)
    {
        if (!_settings.LogiBatteryEnabled) return false;
        // 目标屏解析复用共用核心（primary / span / 设备名精确匹配 / 回退主屏），
        // 与心率等元素共用同一真源，避免多处实现漂移
        return OverlayElementCore.IsTargetScreen(o, _settings.LogiBatteryTargetScreen,
            _windowManager.Overlays, _windowManager.SpanOverlay, allowSpan: true);
    }

    private bool HasLogiBatteryContent()
    {
        if (!_settings.LogiBatteryEnabled) return false;
        if (_logiBatteryProvider == null) return false;
        lock (_lock)
        {
            foreach (var d in _logiBatteryDevices)
            {
                if (_settings.LogiBatteryHideWhenDisconnected && !d.Online) continue;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 罗技电池叠加层是否需要保持渲染循环活跃。
    /// 判定条件全部来自电量自身：开关启用 + Provider 已注入 + 存在可绘制设备 + 存在匹配的目标屏窗口。
    /// 与顶部卡片 / 弹幕 / 心率 / 键盘等其他叠加层元素完全无关，保证电量可独立显示与隐藏。
    /// 无设备时返回 false 只让主循环进入 30ms 轮询休眠；设备一旦上线，
    /// Provider 的 DevicesUpdated 会置 _displayDirty，下一次轮询（≤30ms）即重新点亮窗口。
    /// </summary>
    private bool LogiBatteryActive()
    {
        if (!HasLogiBatteryContent()) return false;
        // 覆盖层窗口集合仅由渲染线程维护，此处无需加锁
        foreach (var o in _windowManager.Overlays)
            if (IsLogiBatteryTarget(o)) return true;
        return false;
    }

    /// <summary>
    /// 渲染每台设备：圆角小卡片 = 电池图标 + 设备名（过长自动省略号截断）。
    /// 单列纵向排列；整张卡片的最大宽度为屏幕宽 × LogiCardMaxWidthFactor，
    /// 超过后设备名 TextLayout 会被强制 WordEllipsis，确保不会把卡片撑满屏幕。
    /// </summary>
    private void RenderLogiBattery(ScreenOverlay overlay, double now, double freq)
    {
        if (!IsLogiBatteryTarget(overlay)) return;
        var rt = overlay.RenderTarget;
        if (rt == null) return;

        List<LogiBatteryDeviceInfo> snapshot;
        long dataVersion;
        lock (_lock)
        {
            if (_logiBatteryDevices.Count == 0) return;
            snapshot = _logiBatteryDevices.ToList();
            dataVersion = _logiDataVersion;
        }

        var toRender = new List<LogiBatteryDeviceInfo>(snapshot.Count);
        foreach (var d in snapshot)
        {
            if (_settings.LogiBatteryHideWhenDisconnected && !d.Online) continue;
            toRender.Add(d);
        }
        if (toRender.Count == 0) return;

        float scale = OverlayElementCore.ResolveScale(_settings.LogiBatteryScale, 0.5f, 4f);
        float iconSize = LogiIconSize * scale;
        float textSize = LogiTextSize * scale;
        float px = LogiCardPaddingX * scale;
        float py = LogiCardPaddingY * scale;
        float radius = LogiCardCornerRadius * scale;
        float gap = LogiIconTextGap * scale;

        int screenW = overlay.Width;
        int screenH = overlay.Height;
        var (baseX, baseY) = OverlayElementCore.ResolveAnchor(overlay,
            _settings.LogiBatteryXPercent, _settings.LogiBatteryYPercent);

        float cardMaxWidth = Math.Clamp(screenW * LogiCardMaxWidthFactor, 120f * scale, 540f * scale);
        // 设备名可用最大宽度 = 卡片max - 图标 - 2*pad - gap
        float nameMaxWidth = cardMaxWidth - iconSize - px * 2 - gap;
        if (nameMaxWidth < 20f) nameMaxWidth = 20f;

        // 复用/按需重建缓存资源（按 scale + nameMaxWidth + 设备数据版本 + 渲染目标）
        EnsureLogiRenderCache(rt, toRender, scale, nameMaxWidth, textSize, iconSize, dataVersion);

        // 第一轮：取缓存中的截断设备名宽度，计算整列最大卡片宽
        float maxCardWidth = 0;
        foreach (var d in toRender)
        {
            if (!_logiDeviceCaches.TryGetValue(d.DeviceId, out var entry) || entry.NameLayout == null) continue;
            float naturalCardW = iconSize + gap + entry.NameWidth + px * 2;
            float cardW = MathF.Min(cardMaxWidth, naturalCardW);
            if (cardW > maxCardWidth) maxCardWidth = cardW;
        }

        float rowHeight = Math.Max(iconSize, textSize) + py * 2;
        float cursorY = baseY;
        foreach (var d in toRender)
        {
            if (!_logiDeviceCaches.TryGetValue(d.DeviceId, out var entry)
                || entry.NameLayout == null || entry.IconLayout == null || entry.IconBrush == null) continue;

            float drawX = MathF.Min(baseX, MathF.Max(0, screenW - maxCardWidth));
            var rect = new RoundedRectangle(new RectangleF(drawX, cursorY, maxCardWidth, rowHeight), radius, radius);
            rt.FillRoundedRectangle(ref rect, _logiBgBrush!);
            rt.DrawRoundedRectangle(rect, _logiBorderBrush!, 1f * scale);

            float innerY = cursorY + py;
            float contentTopOffset = Math.Max(0f, (rowHeight - py * 2 - iconSize) / 2);

            // 1. 电池图标（字形/颜色统一来自共享 BatteryIconUtility；资源取自缓存）
            rt.DrawTextLayout(new Vector2(drawX + px, innerY + contentTopOffset), entry.IconLayout, entry.IconBrush);

            // 2. 设备名（截断结果已在缓存构建阶段一次性计算）
            float textX = drawX + px + iconSize + gap;
            float textY = innerY + Math.Max(0, (rowHeight - py * 2 - textSize) / 2);
            rt.DrawTextLayout(new Vector2(textX, textY), entry.NameLayout, _logiTextBrush!);

            cursorY += rowHeight + LogiCardSpacing * scale;
        }
    }

    /// <summary>
    /// 确保罗技电池渲染缓存与当前 scale / nameMaxWidth / 设备数据版本 / 渲染目标一致。
    /// 仅在对应值变化时重建资源，避免每帧创建 DirectWrite 对象与对设备名做二分查找截断。
    /// </summary>
    private void EnsureLogiRenderCache(ID2D1DCRenderTarget rt, List<LogiBatteryDeviceInfo> toRender,
        float scale, float nameMaxWidth, float textSize, float iconSize, long dataVersion)
    {
        bool dirty = dataVersion != _logiCacheVersion;
        bool scaleChanged = !Approximately(_logiCacheScale, scale);
        bool widthChanged = !Approximately(_logiCacheNameMaxWidth, nameMaxWidth);
        bool rtChanged = !ReferenceEquals(_logiBrushRt, rt);

        // 兜底：toRender 中存在尚未缓存的设备（理论上数据更新已置脏，此处防止遗漏导致设备不渲染）
        bool missing = false;
        foreach (var d in toRender)
        {
            if (!_logiDeviceCaches.ContainsKey(d.DeviceId)) { missing = true; break; }
        }

        // 画刷与渲染目标绑定：rt 变化时重建（颜色固定，不随脏标志重建）
        if (rtChanged || _logiBgBrush == null)
        {
            _logiBgBrush?.Dispose();
            _logiBorderBrush?.Dispose();
            _logiTextBrush?.Dispose();
            const float opacity = 0.9f;
            _logiBgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.6f * opacity));
            _logiBorderBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, 0.35f * opacity));
            _logiTextBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));
            _logiBrushRt = rt;
            // 设备图标画刷同样与 rt 绑定，随 rt 重建
            foreach (var e in _logiDeviceCaches.Values)
            {
                e.IconBrush?.Dispose();
                e.IconBrush = rt.CreateSolidColorBrush(e.BatteryColor);
            }
        }

        // 文本格式随 scale 变化重建（与渲染目标无关，可跨窗口复用）
        if (scaleChanged || _logiIconFormat == null)
        {
            _logiIconFormat?.Dispose();
            _logiTextFormat?.Dispose();
            _logiIconFormat = CreateTextFormat("Segoe MDL2 Assets", DWriteFontWeight.Regular, iconSize);
            _logiTextFormat = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.SemiBold, textSize);
            _logiTextFormat.WordWrapping = WordWrapping.NoWrap;
        }

        // 设备级资源：版本 / scale / nameMaxWidth / 缺失 变化时整体重建（截断名也在此一次性计算）
        if (dirty || scaleChanged || widthChanged || missing)
        {
            RebuildLogiDeviceCaches(rt, toRender, scale, nameMaxWidth, textSize, iconSize);
            _logiCacheVersion = dataVersion;
            _logiCacheScale = scale;
            _logiCacheNameMaxWidth = nameMaxWidth;
        }
    }

    /// <summary>按当前 toRender 重建每台设备的缓存（截断名、名称/图标布局、图标画刷），并清理已消失设备。</summary>
    private void RebuildLogiDeviceCaches(ID2D1DCRenderTarget rt, List<LogiBatteryDeviceInfo> toRender,
        float scale, float nameMaxWidth, float textSize, float iconSize)
    {
        var alive = new HashSet<string>(toRender.Count);
        foreach (var d in toRender) alive.Add(d.DeviceId);

        // 移除已消失设备，释放其资源
        foreach (var key in _logiDeviceCaches.Keys.ToList())
        {
            if (!alive.Contains(key))
            {
                DisposeLogiEntry(_logiDeviceCaches[key]);
                _logiDeviceCaches.Remove(key);
            }
        }

        foreach (var d in toRender)
        {
            if (!_logiDeviceCaches.TryGetValue(d.DeviceId, out var entry))
            {
                entry = new LogiBatteryRenderEntry();
                _logiDeviceCaches[d.DeviceId] = entry;
            }

            // 设备名截断仅在此计算（避免每帧二分查找创建 DirectWrite 对象）
            string displayName = TruncateNameToWidth(d.DeviceName, _logiTextFormat!, nameMaxWidth, out float nameW);

            entry.NameLayout?.Dispose();
            entry.IconLayout?.Dispose();
            entry.IconBrush?.Dispose();

            entry.DisplayName = displayName;
            entry.NameWidth = nameW;
            entry.NameLayout = _dwFactory.CreateTextLayout(displayName, _logiTextFormat!, nameMaxWidth + 10f, textSize * 1.4f);
            entry.IconLayout = _dwFactory.CreateTextLayout(d.BatteryGlyph, _logiIconFormat!, iconSize * 2, iconSize * 2);
            entry.IconBrush = rt.CreateSolidColorBrush(d.BatteryColor);
            entry.BatteryColor = d.BatteryColor;
            entry.Scale = scale;
            entry.NameMaxWidth = nameMaxWidth;
        }
    }

    private static void DisposeLogiEntry(LogiBatteryRenderEntry e)
    {
        try { e.NameLayout?.Dispose(); } catch { }
        try { e.IconLayout?.Dispose(); } catch { }
        try { e.IconBrush?.Dispose(); } catch { }
    }

    /// <summary>释放罗技电池渲染缓存（服务销毁时调用）。</summary>
    private void DisposeLogiBatteryCache()
    {
        try { _logiIconFormat?.Dispose(); } catch { }
        try { _logiTextFormat?.Dispose(); } catch { }
        try { _logiBgBrush?.Dispose(); } catch { }
        try { _logiBorderBrush?.Dispose(); } catch { }
        try { _logiTextBrush?.Dispose(); } catch { }
        foreach (var e in _logiDeviceCaches.Values) DisposeLogiEntry(e);
        _logiDeviceCaches.Clear();
        _logiIconFormat = _logiTextFormat = null;
        _logiBgBrush = _logiBorderBrush = _logiTextBrush = null;
        _logiBrushRt = null;
        _logiCacheVersion = -1;
        _logiCacheScale = -1f;
        _logiCacheNameMaxWidth = -1f;
    }

    private static bool Approximately(float a, float b) => Math.Abs(a - b) <= 1e-4f;

    /// <summary>
    /// 按目标像素宽度截断设备名；必要时追加"…"（既用于 UI 省略号视觉，也避免长名字撑满卡片）。
    /// 返回（截断后字符串，实际绘制宽度）。
    /// </summary>
    private string TruncateNameToWidth(string name, IDWriteTextFormat fmt, float maxWidth, out float actualWidth)
    {
        if (string.IsNullOrEmpty(name))
        {
            actualWidth = 0;
            return string.Empty;
        }

        using var full = _dwFactory.CreateTextLayout(name, fmt, maxWidth * 10f, float.PositiveInfinity);
        actualWidth = full.Metrics.Width;
        if (actualWidth <= maxWidth || name.Length <= 2)
        {
            return name;
        }

        // 二分（退化为逐字符）搜索可容纳的最大长度 + 拼接"…"
        const string ellipsis = "…";
        using var ell = _dwFactory.CreateTextLayout(ellipsis, fmt, 200, float.PositiveInfinity);
        float ellWidth = ell.Metrics.Width;
        float budget = Math.Max(0, maxWidth - ellWidth);

        int left = 0, right = name.Length;
        int best = 0;
        while (left <= right)
        {
            int mid = (left + right) / 2;
            if (mid == 0) { best = 0; left = mid + 1; continue; }
            string sub = name[..mid];
            using var tmp = _dwFactory.CreateTextLayout(sub, fmt, budget + 20f, float.PositiveInfinity);
            if (tmp.Metrics.Width <= budget)
            {
                best = mid;
                left = mid + 1;
            }
            else
            {
                right = mid - 1;
            }
        }

        string truncated = best == 0 ? ellipsis : string.Concat(name.AsSpan(0, best), ellipsis);
        using var final = _dwFactory.CreateTextLayout(truncated, fmt, maxWidth * 10f, float.PositiveInfinity);
        actualWidth = final.Metrics.Width;
        return truncated;
    }
}
