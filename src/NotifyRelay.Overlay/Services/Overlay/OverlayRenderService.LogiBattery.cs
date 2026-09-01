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
            if (_logiBatteryProvider != null)
                _logiBatteryProvider.DevicesUpdated -= OnLogiBatteryDevicesUpdated;
            _logiBatteryProvider = provider;
            if (provider != null)
            {
                provider.DevicesUpdated += OnLogiBatteryDevicesUpdated;
                _logiBatteryDevices = provider.GetDevices().ToList();
            }
            else
            {
                _logiBatteryDevices.Clear();
            }
            _displayDirty = true;
        }
    }

    private void OnLogiBatteryDevicesUpdated(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_logiBatteryProvider != null)
                _logiBatteryDevices = _logiBatteryProvider.GetDevices().ToList();
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
        string target = _settings.LogiBatteryTargetScreen ?? string.Empty;
        if (string.Equals(target, "primary", StringComparison.OrdinalIgnoreCase))
            return o.IsPrimary;
        if (string.Equals(target, "span", StringComparison.OrdinalIgnoreCase))
            return ReferenceEquals(o, _spanOverlay);
        // 具体显示器：按 DeviceName 精确匹配
        var match = _overlays.Find(x => !x.IsSpan
            && string.Equals(x.DeviceName, target, StringComparison.Ordinal));
        if (match != null) return ReferenceEquals(o, match);
        return o.IsPrimary;
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
        // _overlays / _spanOverlay 仅由渲染线程（SyncOverlays、CleanupOverlays）维护，此处无需加锁
        foreach (var o in _overlays)
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
        lock (_lock)
        {
            if (_logiBatteryDevices.Count == 0) return;
            snapshot = _logiBatteryDevices.ToList();
        }

        var toRender = new List<LogiBatteryDeviceInfo>(snapshot.Count);
        foreach (var d in snapshot)
        {
            if (_settings.LogiBatteryHideWhenDisconnected && !d.Online) continue;
            toRender.Add(d);
        }
        if (toRender.Count == 0) return;

        float scale = Math.Clamp(_settings.LogiBatteryScale, 0.5f, 4f);
        float iconSize = LogiIconSize * scale;
        float textSize = LogiTextSize * scale;
        float px = LogiCardPaddingX * scale;
        float py = LogiCardPaddingY * scale;
        float radius = LogiCardCornerRadius * scale;
        float gap = LogiIconTextGap * scale;

        int screenW = overlay.Width;
        int screenH = overlay.Height;
        float baseX = Math.Clamp(_settings.LogiBatteryXPercent, 0, 100) / 100f * screenW;
        float baseY = Math.Clamp(_settings.LogiBatteryYPercent, 0, 100) / 100f * screenH;

        float cardMaxWidth = Math.Clamp(screenW * LogiCardMaxWidthFactor, 120f * scale, 540f * scale);
        // 设备名可用最大宽度 = 卡片max - 图标 - 2*pad - gap
        float nameMaxWidth = cardMaxWidth - iconSize - px * 2 - gap;
        if (nameMaxWidth < 20f) nameMaxWidth = 20f;

        using var iconFormat = CreateTextFormat("Segoe MDL2 Assets", DWriteFontWeight.Regular, iconSize);
        using var textFormat = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.SemiBold, textSize);
        textFormat.WordWrapping = WordWrapping.NoWrap;
        // Vortice IDWriteTextFormat 未暴露 Trimming 属性（需要 SetTrimming + InlineObject 组合）。
        // 为了减少复杂度，这里改为"手动截断 + 追加省略号"：测量超出 nameMaxWidth 时截短字符串补"…"。

        const float opacity = 0.9f;
        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.6f * opacity));
        using var borderBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, 0.35f * opacity));
        using var textBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));

        // 第一轮：计算每个设备经过"手动省略号截断"后的显示文本与宽度
        float maxCardWidth = 0;
        var displayInfos = new List<(LogiDevice d, string DisplayName, float NameWidth)>();
        foreach (var d in toRender)
        {
            string displayName = TruncateNameToWidth(d.DeviceName, textFormat, nameMaxWidth, out var nameW);
            float naturalCardW = iconSize + gap + nameW + px * 2;
            float cardW = MathF.Min(cardMaxWidth, naturalCardW);
            if (cardW > maxCardWidth) maxCardWidth = cardW;
            displayInfos.Add((d, displayName, nameW));
        }

        float rowHeight = Math.Max(iconSize, textSize) + py * 2;
        float cursorY = baseY;
        foreach (var (d, displayName, _) in displayInfos)
        {
            float drawX = MathF.Min(baseX, MathF.Max(0, screenW - maxCardWidth));
            var rect = new RoundedRectangle(new RectangleF(drawX, cursorY, maxCardWidth, rowHeight), radius, radius);
            rt.FillRoundedRectangle(ref rect, bgBrush);
            rt.DrawRoundedRectangle(rect, borderBrush, 1f * scale);

            float innerY = cursorY + py;
            float contentTopOffset = Math.Max(0f, (rowHeight - py * 2 - iconSize) / 2);

            // 1. 电池图标（字形/颜色统一来自共享 BatteryIconUtility）
            using var iconLayout = _dwFactory.CreateTextLayout(d.BatteryGlyph, iconFormat, iconSize * 2, iconSize * 2);
            using var iconBrush = rt.CreateSolidColorBrush(d.BatteryColor);
            rt.DrawTextLayout(new Vector2(drawX + px, innerY + contentTopOffset), iconLayout, iconBrush);

            // 2. 设备名（已在 TruncateNameToWidth 截断；再次使用显示名 Draw）
            float textX = drawX + px + iconSize + gap;
            float textY = innerY + Math.Max(0, (rowHeight - py * 2 - textSize) / 2);
            using var nameLayout = _dwFactory.CreateTextLayout(displayName, textFormat, nameMaxWidth + 10f, textSize * 1.4f);
            rt.DrawTextLayout(new Vector2(textX, textY), nameLayout, textBrush);

            cursorY += rowHeight + LogiCardSpacing * scale;
        }
    }

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
