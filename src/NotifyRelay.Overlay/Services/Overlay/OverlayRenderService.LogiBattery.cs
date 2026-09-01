using System.Drawing;
using System.Numerics;
using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// OverlayRenderService 的罗技电池叠加层渲染 partial。
/// Direct2D/DirectWrite 绘制图标、设备名、电量%。
/// 所有电池字体/颜色直接调用 BatteryIconUtility（主页同款，不重复逻辑）。
/// </summary>
public partial class OverlayRenderService
{
    // ===== 罗技电池相关 =====
    private ILogiBatteryProvider? _logiBatteryProvider;
    private List<LogiBatteryDeviceInfo> _logiBatteryDevices = [];

    // 渲染尺寸常量（最终乘以 Scale）
    private const float LogiCardPaddingX = 14f;
    private const float LogiCardPaddingY = 10f;
    private const float LogiCardSpacing = 8f;
    private const float LogiCardCornerRadius = 8f;
    private const float LogiIconSize = 20f;       // Segoe MDL2 图标字号
    private const float LogiTextBaseSize = 14f;   // 设备名/电量字号
    private const float LogiIconTextGap = 8f;     // 图标与设备名间距
    private const float LogiNamePercentGap = 10f; // 设备名与电量%间距

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
                // 立即快照一次
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

    /// <summary>目标屏幕是否为罗技电池叠加层的目标屏。</summary>
    private bool IsLogiBatteryTarget(ScreenOverlay o)
    {
        if (!_settings.LogiBatteryEnabled) return false;
        string target = _settings.LogiBatteryTargetScreen ?? string.Empty;
        if (string.Equals(target, "primary", StringComparison.OrdinalIgnoreCase))
            return o.IsPrimary;
        if (string.Equals(target, "span", StringComparison.OrdinalIgnoreCase))
            return ReferenceEquals(o, _spanOverlay);
        // 默认 "all"：仅在主屏显示（防止多屏重复）
        return o.IsPrimary;
    }

    /// <summary>
    /// 是否存在需要显示的罗技电池内容：
    /// 开关开启 + 至少一台设备满足显示条件（在线 HideWhenDisconnected 过滤后）
    /// </summary>
    private bool HasLogiBatteryContent()
    {
        if (!_settings.LogiBatteryEnabled) return false;
        if (_logiBatteryProvider == null) return false;
        lock (_lock)
        {
            foreach (var d in _logiBatteryDevices)
            {
                if (_settings.LogiBatteryHideWhenDisconnected && !d.Online) continue;
                return true; // 至少一台需要显示
            }
        }
        return false;
    }

    /// <summary>
    /// 渲染罗技电池卡片。与键盘渲染模式类似：半透明黑圆角背景 + 图标(MDL2) + 设备名 + 电量%。
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
        float textSize = LogiTextBaseSize * scale;
        float px = LogiCardPaddingX * scale;
        float py = LogiCardPaddingY * scale;
        float radius = LogiCardCornerRadius * scale;

        int screenW = overlay.Width;
        int screenH = overlay.Height;
        float baseX = Math.Clamp(_settings.LogiBatteryXPercent, 0, 100) / 100f * screenW;
        float baseY = Math.Clamp(_settings.LogiBatteryYPercent, 0, 100) / 100f * screenH;

        using var iconFormat = CreateTextFormat("Segoe MDL2 Assets", DWriteFontWeight.Regular, iconSize);
        using var textFormat = CreateTextFormat("Microsoft YaHei", DWriteFontWeight.SemiBold, textSize);

        // 先测量所有设备，以确定单卡片最大宽度（左对齐时整齐）
        var measurements = new List<(LogiBatteryDeviceInfo d, float nameW, float percentW)>();
        float maxCardWidth = 0;
        foreach (var d in toRender)
        {
            using var nameLayout = _dwFactory.CreateTextLayout(d.DeviceName, textFormat, 1200, textSize * 2);
            float nameW = nameLayout.Metrics.Width;
            string percentText = d.HasBattery ? $"{d.BatteryPercent}%" : "--";
            using var pctLayout = _dwFactory.CreateTextLayout(percentText, textFormat, 200, textSize * 2);
            float percentW = pctLayout.Metrics.Width;

            float cardInner = LogiIconTextGap + nameW + LogiNamePercentGap + percentW;
            float totalW = iconSize + cardInner + px * 2;
            if (totalW > maxCardWidth) maxCardWidth = totalW;

            measurements.Add((d, nameW, percentW));
        }

        float rowHeight = Math.Max(iconSize, textSize) + py * 2;

        // 颜色画笔（随用随创建，性能可接受）
        float opacity = 0.85f;
        using var bgBrush = rt.CreateSolidColorBrush(new Color4(0, 0, 0, 0.6f * opacity));
        using var borderBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, 0.35f * opacity));
        using var textBrush = rt.CreateSolidColorBrush(new Color4(1, 1, 1, opacity));

        float cursorY = baseY;
        for (int i = 0; i < measurements.Count; i++)
        {
            var (d, nameW, percentW) = measurements[i];
            // 安全边界：不允许卡片右侧超出屏幕
            float drawX = MathF.Min(baseX, MathF.Max(0, screenW - maxCardWidth));
            var rect = new RoundedRectangle(new RectangleF(drawX, cursorY, maxCardWidth, rowHeight), radius, radius);
            rt.FillRoundedRectangle(ref rect, bgBrush);
            rt.DrawRoundedRectangle(rect, borderBrush, 1.2f * scale);

            float innerY = cursorY + py;
            // 1. 电池图标
            string iconGlyph = d.BatteryGlyph;
            using var iconLayout = _dwFactory.CreateTextLayout(iconGlyph, iconFormat, iconSize * 2, iconSize * 2);
            using var iconColorBrush = rt.CreateSolidColorBrush(d.BatteryColor);
            rt.DrawTextLayout(new Vector2(drawX + px, innerY + Math.Max(0, (rowHeight - py * 2 - iconSize) / 2)), iconLayout, iconColorBrush);

            // 2. 设备名
            float textX = drawX + px + iconSize + LogiIconTextGap * scale;
            float textY = innerY + Math.Max(0, (rowHeight - py * 2 - textSize) / 2);
            using var nameLayout = _dwFactory.CreateTextLayout(d.DeviceName, textFormat, 1200, textSize * 2);
            rt.DrawTextLayout(new Vector2(textX, textY), nameLayout, textBrush);

            // 3. 电量%（右对齐到卡片内边距右侧）
            string percentText = d.HasBattery ? $"{d.BatteryPercent}%" : "--";
            using var pctLayout = _dwFactory.CreateTextLayout(percentText, textFormat, 200, textSize * 2);
            float pctX = drawX + maxCardWidth - px - percentW;
            rt.DrawTextLayout(new Vector2(pctX, textY), pctLayout, d.HasBattery ? iconColorBrush : textBrush);

            cursorY += rowHeight + LogiCardSpacing * scale;
        }
    }
}
