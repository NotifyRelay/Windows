using NotifyRelay.Models.Render;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontWeight = Vortice.DirectWrite.FontWeight;
using DWriteTextAlignment = Vortice.DirectWrite.TextAlignment;
using LogiDevice = NotifyRelay.Models.Render.LogiBatteryDeviceInfo;

namespace NotifyRelay.Services.Overlay.UI.Elements;

/// <summary>
/// 罗技电池元素：单列纵向卡片 = 电池图标（Segoe MDL2 Assets） + 设备名（过长省略号截断）。
/// 维持「绘制时直读设置」的现状（<see cref="IOverlaySettings.LogiBattery*"/>），避免改动刷新时机。
/// </summary>
internal sealed class LogiBatteryElement : IOverlayElement
{
    private const float CardPaddingX = 12f;
    private const float CardPaddingY = 8f;
    private const float CardSpacing = 6f;
    private const float CardCornerRadius = 7f;
    private const float IconSize = 20f;
    private const float TextSize = 13f;
    private const float IconTextGap = 8f;
    private const float CardMaxWidthFactor = 0.35f;
    private const float Opacity = 0.9f;

    private readonly ElementContext _ctx;

    // Provider 与设备快照（_lock 保护）
    private ILogiBatteryProvider? _provider;
    private List<LogiBatteryDeviceInfo> _devices = [];

    public LogiBatteryElement(ElementContext ctx) => _ctx = ctx;

    public string Name => "LogiBattery";

    /// <summary>罗技电池无初始配置加载项（设置项在绘制时直读）。</summary>
    public void LoadSettings(IOverlaySettings settings) { }

    /// <summary>注入罗技电池数据提供者（DI 启动后调用）。</summary>
    public void SetProvider(ILogiBatteryProvider? provider)
        => _ctx.WithLock(() =>
        {
            // 退订旧 Provider、订阅新 Provider 走共用模板（与键盘等元素一致）
            OverlayElementCore.ReplaceProvider(ref _provider, provider,
                p => p.DevicesUpdated += OnDevicesUpdated,
                p => p.DevicesUpdated -= OnDevicesUpdated);
            _devices = provider != null ? provider.GetDevices().ToList() : [];
        }, "覆盖层数据锁获取超时，跳过罗技电池 Provider 替换");

    private void OnDevicesUpdated(object? sender, EventArgs e)
        => _ctx.WithLock(() =>
        {
            if (_provider != null) _devices = _provider.GetDevices().ToList();
        }, string.Empty);

    public bool IsActive()
    {
        if (!HasContent()) return false;
        // 覆盖层窗口集合仅由渲染线程维护，此处无需加锁
        var overlays = _ctx.Overlays();
        for (int i = 0; i < overlays.Count; i++)
            if (IsTargetScreen(overlays[i])) return true;
        return false;
    }

    /// <summary>开关启用 + Provider 已注入 + 存在可绘制设备。</summary>
    private bool HasContent()
    {
        var s = _ctx.Settings;
        if (!s.LogiBatteryEnabled || _provider == null) return false;
        lock (_ctx.StateLock)
        {
            foreach (var d in _devices)
            {
                if (s.LogiBatteryHideWhenDisconnected && !d.Online) continue;
                return true;
            }
        }
        return false;
    }

    public bool IsTargetScreen(ScreenOverlay o)
    {
        var s = _ctx.Settings;
        if (!s.LogiBatteryEnabled) return false;
        return _ctx.IsTargetScreen(o, s.LogiBatteryTargetScreen, allowSpan: true);
    }

    public void Compose(OverlayComposer composer, ScreenOverlay o)
    {
        var s = _ctx.Settings;
        if (!s.LogiBatteryEnabled) return;

        List<LogiBatteryDeviceInfo> snapshot;
        lock (_ctx.StateLock)
        {
            if (_devices.Count == 0) return;
            snapshot = _devices.ToList();
        }

        // 过滤未连接设备（与旧实现一致）
        var toRender = new List<LogiBatteryDeviceInfo>(snapshot.Count);
        foreach (var d in snapshot)
        {
            if (s.LogiBatteryHideWhenDisconnected && !d.Online) continue;
            toRender.Add(d);
        }
        if (toRender.Count == 0) return;

        float scale = ElementContext.ResolveScale(s.LogiBatteryScale, 0.5f, 4f);
        float iconSize = IconSize * scale;
        float textSize = TextSize * scale;
        float px = CardPaddingX * scale;
        float py = CardPaddingY * scale;
        float radius = CardCornerRadius * scale;
        float gap = IconTextGap * scale;
        float spacing = CardSpacing * scale;

        float cardMaxWidth = Math.Clamp(o.Width * CardMaxWidthFactor, 120f * scale, 540f * scale);
        // 设备名可用最大宽度 = 卡片max - 图标 - 2*pad - gap
        float nameMaxWidth = MathF.Max(20f, cardMaxWidth - iconSize - px * 2 - gap);
        float rowHeight = Math.Max(iconSize, textSize) + py * 2;

        composer.Node<Align>(null, a =>
        {
            a.XPct = s.LogiBatteryXPercent;
            a.YPct = s.LogiBatteryYPercent;
            a.AnchorAtCenterX = false; a.AnchorAtCenterY = false;
            // 旧实现 drawX = min(baseX, max(0, screenW - maxCardWidth))：越界时左移，等价夹取
            a.ClampToBounds = true;
        }, () =>
        {
            // UniformWidth：复刻现状「先量测各卡自然宽，取最大值后统一卡片宽」。
            // 不设 FixedWidth —— 列宽由各卡自然宽的最大值决定，再由 MaxChildWidth 按屏宽比例封顶，
            // 与旧实现 maxCardWidth = max(min(cardMaxWidth, naturalCardW)) 等价。
            composer.Node<Column>("cards", col =>
            {
                col.Spacing = spacing;
                col.CrossAlignment = CrossAlignment.Start;
                col.UniformWidth = true;
                col.MaxChildWidth = cardMaxWidth;
            }, () =>
            {
                for (int i = 0; i < toRender.Count; i++)
                {
                    var device = toRender[i];
                    composer.Node<Surface>("device:" + device.DeviceId, surface =>
                    {
                        surface.Radius = radius;
                        surface.Filled = true;
                        surface.Fill = new Color4(0f, 0f, 0f, 0.6f * Opacity);
                        surface.Bordered = true;
                        surface.Border = new Color4(1f, 1f, 1f, 0.35f * Opacity);
                        surface.BorderWidth = 1f * scale;
                        surface.Insets = new Insets(px, py);
                    }, () =>
                    {
                        composer.Node<Row>("content", r =>
                        {
                            r.Gap = gap;
                            r.CrossAlignment = CrossAlignment.Center;
                        }, () =>
                        {
                            // 电池图标：字形/颜色统一来自共享 BatteryIconUtility
                            composer.Leaf<Icon>("battery", icon =>
                            {
                                icon.Glyph = device.BatteryGlyph;
                                icon.FontFamily = "Segoe MDL2 Assets";
                                icon.Weight = DWriteFontWeight.Regular;
                                icon.FontSize = iconSize;
                                icon.ReportedWidth = iconSize;
                                icon.Color = device.BatteryColor;
                            });

                            // 设备名：由布局按剩余宽度以省略号截断（替代旧「二分 + 手工拼 …」）
                            composer.Leaf<Text>("name", t =>
                            {
                                t.TextValue = device.DeviceName;
                                t.FontFamily = "Microsoft YaHei";
                                t.Weight = DWriteFontWeight.SemiBold;
                                t.FontSize = textSize;
                                t.LineHeight = textSize * 1.4f;
                                t.ReportedHeight = Math.Max(iconSize, textSize);
                                t.MaxWidth = nameMaxWidth;
                                t.Ellipsis = true;
                                t.Color = new Color4(1f, 1f, 1f, Opacity);
                            });
                        });
                    });
                }
            });
        });
    }

    /// <summary>释放渲染缓存（本元素已无字段级缓存，保留钩子以统一清理路径）。</summary>
    public void Reset() { }
}
