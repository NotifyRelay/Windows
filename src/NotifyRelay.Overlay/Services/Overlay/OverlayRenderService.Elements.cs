using NotifyRelay.Services.Overlay.UI;
using NotifyRelay.Services.Overlay.UI.Elements;
using Vortice.Direct2D1;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// OverlayRenderService 的声明式 UI 部分：持有 5 个叠加层元素与每屏一棵的
/// <see cref="OverlayUiRoot"/>，并把原各 partial 的 public 方法原样转发到对应元素
/// （签名与语义保持不变，调用方零改动）。
/// </summary>
public partial class OverlayRenderService
{
    private ElementContext? _elementContext;

    // 5 个叠加层元素（主体构造函数内固定创建）
    private ClockElement? _clockElement;
    private HeartRateElement? _heartRateElement;
    private LogiBatteryElement? _logiBatteryElement;
    private DeepSeekBalanceElement? _deepSeekBalanceElement;
    private KeyboardElement? _keyboardElement;
    private IOverlayElement[] _elements = [];

    /// <summary>创建元素上下文与 5 个元素（构造函数调用）。</summary>
    private void InitializeElements()
    {
        _elementContext = new ElementContext(
            _settings, _logger, _d2dFactory, _dwFactory, _lock,
            () => _windowManager.Overlays,
            () => _windowManager.SpanOverlay,
            markDirty: () => _displayDirty = true);

        _clockElement = new ClockElement(_elementContext);
        _heartRateElement = new HeartRateElement(_elementContext);
        _logiBatteryElement = new LogiBatteryElement(_elementContext);
        _deepSeekBalanceElement = new DeepSeekBalanceElement(_elementContext);
        _keyboardElement = new KeyboardElement(_elementContext);
        _elements =
        [
            _clockElement,
            _heartRateElement,
            _logiBatteryElement,
            _deepSeekBalanceElement,
            _keyboardElement,
        ];
    }

    /// <summary>从已保存设置初始化全部元素的配置（Start 时调用，收敛原 4 个 LoadInitialXxxConfig）。</summary>
    private void LoadInitialElementSettings()
    {
        foreach (var e in _elements) e.LoadSettings(_settings);
    }

    // ===== public 转发方法（签名与语义与原 partial 完全一致） =====

    /// <summary>更新时间浮窗配置（启用、目标屏、位置百分比、颜色、描边、缩放、格式）。</summary>
    public void SetClockConfig(bool enabled, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool showSeconds, bool use24Hour)
        => _clockElement!.SetConfig(enabled, targetScreen, xPct, yPct, colorHex, outlineWidth, scale,
            showSeconds, use24Hour);

    /// <summary>更新心率覆盖层配置（启用、样式组合、目标屏、位置百分比、颜色）。</summary>
    public void SetHeartRateConfig(bool enabled, int styleFlags, string targetScreen, float xPct, float yPct,
        string colorHex, float outlineWidth, float scale, bool alertEnabled, int lowAlert, int highAlert,
        int spikeDelta, bool hideWhenDisconnected = true)
        => _heartRateElement!.SetConfig(enabled, styleFlags, targetScreen, xPct, yPct, colorHex, outlineWidth,
            scale, alertEnabled, lowAlert, highAlert, spikeDelta, hideWhenDisconnected);

    /// <summary>推送最新心率值（BLE 通知回调线程调用）。</summary>
    public void UpdateHeartRate(int bpm) => _heartRateElement!.UpdateHeartRate(bpm);

    /// <summary>设置心率设备连接状态；断开时清空当前值与历史。</summary>
    public void SetHeartRateConnected(bool connected) => _heartRateElement!.SetConnected(connected);

    /// <summary>清空心率显示数据。</summary>
    public void ClearHeartRate() => _heartRateElement!.Clear();

    /// <summary>注入罗技电池数据提供者（DI 启动后调用）。</summary>
    public void SetLogiBatteryProvider(ILogiBatteryProvider? provider)
        => _logiBatteryElement!.SetProvider(provider);

    /// <summary>设置键盘状态查询服务（由 DI 注入后调用）。</summary>
    public void SetKeyboardStateProvider(IKeyboardStateProvider? provider)
        => _keyboardElement!.SetProvider(provider);

    /// <summary>更新 DeepSeek 余额叠加层配置（启用、目标屏、位置百分比、缩放）。</summary>
    public void SetDeepSeekBalanceConfig(bool enabled, string targetScreen, float xPct, float yPct, float scale)
        => _deepSeekBalanceElement!.SetConfig(enabled, targetScreen, xPct, yPct, scale);

    /// <summary>
    /// 推送最新余额与相对上一条记录的变化量（余额服务线程调用）。
    /// balance 传 null 表示暂无数据（如未配置 Token / 查询失败且无历史）。
    /// </summary>
    public void UpdateDeepSeekBalance(double? balance, double change)
        => _deepSeekBalanceElement!.Update(balance, change);

    /// <summary>获取当前显示器列表（DeviceName + 是否主屏），供设置页屏幕下拉使用。</summary>
    public IReadOnlyList<(string DeviceName, bool IsPrimary)> GetScreenList()
        => EnumerateScreens().ConvertAll(s => (s.DeviceName, s.IsPrimary));

    // ===== 元素与声明式 UI 的渲染协调 =====

    /// <summary>是否存在任一活跃的元素（收敛原逐个 XxxActive 调用链）。</summary>
    private bool AnyElementActive()
    {
        foreach (var e in _elements)
            if (e.IsActive()) return true;
        return false;
    }

    /// <summary>本窗口是否需要保留（任一元件的目标屏命中）。</summary>
    private bool AnyElementTargets(ScreenOverlay o)
    {
        foreach (var e in _elements)
            if (e.IsTargetScreen(o)) return true;
        return false;
    }

    /// <summary>
    /// 除时钟外的其它元素是否命中本窗口。
    /// 时钟单独判定：它自身有「仅秒变化时重绘」的门控，不能与其他元素混为一谈。
    /// </summary>
    private bool AnyNonClockElementTargets(ScreenOverlay o)
    {
        foreach (var e in _elements)
        {
            if (ReferenceEquals(e, _clockElement)) continue;
            if (e.IsTargetScreen(o)) return true;
        }
        return false;
    }

    /// <summary>获取（或懒创建）本屏的声明式 UI 运行时。</summary>
    private OverlayUiRoot GetUiRoot(ScreenOverlay o)
        => o.UiRoot ??= new OverlayUiRoot(_d2dFactory, _dwFactory);

    /// <summary>声明元素层的 UI 子树。</summary>
    private void ComposeElements(OverlayUiRoot ui, ScreenOverlay o)
    {
        var c = ui.BeginElements();
        foreach (var e in _elements)
        {
            if (!e.IsTargetScreen(o)) continue;
            e.Compose(c, o);
        }
        ui.EndElements(o);
    }

    /// <summary>覆盖层重建 / 渲染目标失效时重置声明式 UI 与元素的缓存资源。</summary>
    private void ResetUiAndElements()
    {
        foreach (var o in _windowManager.Overlays)
        {
            o.UiRoot?.Reset();
        }
        foreach (var e in _elements) e.Reset();
    }

    /// <summary>
    /// 覆盖层窗口集合即将重建：清理按窗口实例记录的时钟门控状态，
    /// 避免已销毁窗口的引用长期滞留。由 <c>SyncOverlays</c> 的重建前回调调用。
    /// </summary>
    private void ClearPerOverlayRenderState()
    {
        _lastClockTextByOverlay.Clear();
        _lastClockRootByOverlay.Clear();
    }
}
