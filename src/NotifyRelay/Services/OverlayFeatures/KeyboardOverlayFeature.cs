using NotifyRelay.Data.Contracts;
using NotifyRelay.Models.Render;
using NotifyRelay.Platforms.Windows.Services;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 键盘按键叠加层功能：把键盘钩子的按键状态推送到叠加层，
/// 并在开关开启时按当前状态安装/卸载低级键盘钩子。
/// 登记到容器后由 OverlayFeatureHost 统一引导，无需在启动流程中手写。
/// </summary>
public sealed class KeyboardOverlayFeature : IOverlayHostFeature
{
    private readonly KeyboardHookService _hookService;
    private readonly IGeneralSettingsService _settings;

    private IOverlayDataSink? _sink;
    private int _pushPending;

    public KeyboardOverlayFeature(KeyboardHookService hookService, IGeneralSettingsService settings)
    {
        _hookService = hookService;
        _settings = settings;
    }

    public string Name => "键盘按键叠加层";

    public bool IsEnabled => _settings.KeyboardOverlayEnabled;

    /// <summary>
    /// 订阅按键状态与映射提示事件，并推送一次当前快照。
    /// 幂等：看门狗重启会再次调用本方法，先退订再订阅，避免事件重复挂钩。
    /// </summary>
    public void Attach(IOverlayDataSink sink)
    {
        _sink = sink;
        _hookService.KeyStateChanged -= OnStateChanged;
        _hookService.MappingTriggered -= OnMappingTriggered;
        _hookService.KeyStateChanged += OnStateChanged;
        _hookService.MappingTriggered += OnMappingTriggered;
        sink.ApplyKeyboardState(CaptureSnapshot());
    }

    public void Start() => _hookService.SyncState();

    public void Stop() => _hookService.Uninstall();

    /// <summary>按键变化回调（钩子线程）：不在回调内取元素锁，合并为一次线程池推送。</summary>
    private void OnStateChanged(object? sender, KeyStateChangedEventArgs e)
    {
        if (Interlocked.Exchange(ref _pushPending, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Interlocked.Exchange(ref _pushPending, 0);
            PushSnapshot();
        });
    }

    /// <summary>映射提示回调（钩子线程）：转到线程池推送，避免输入路径被覆盖层数据锁阻塞。</summary>
    private void OnMappingTriggered(object? sender, KeyMappingDisplayEventArgs e)
    {
        var sink = _sink;
        if (sink == null || string.IsNullOrEmpty(e.DisplayText)) return;
        var text = e.DisplayText;
        ThreadPool.QueueUserWorkItem(_ => sink.ShowKeyMappingHint(text));
    }

    private void PushSnapshot() => _sink?.ApplyKeyboardState(CaptureSnapshot());

    /// <summary>读取当前按键与切换键状态；修饰键由 GetPressedKeys 实时补偿。</summary>
    private KeyboardStateSnapshot CaptureSnapshot() => new()
    {
        PressedKeys = _hookService.GetPressedKeys().ToArray(),
        CapsLockToggled = _hookService.IsKeyToggled(0x14),
        NumLockToggled = _hookService.IsKeyToggled(0x90),
        ScrollLockToggled = _hookService.IsKeyToggled(0x91),
    };
}
