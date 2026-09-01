using NotifyRelay.Data.Contracts;
using NotifyRelay.Platforms.Windows.Services;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 键盘按键叠加层功能：把键盘钩子服务作为按键状态源注入叠加层，
/// 并在开关开启时按当前状态安装/卸载低级键盘钩子。
/// 登记到容器后由叠加层模块主初始化自动引导，无需在启动流程中手写。
/// </summary>
public sealed class KeyboardOverlayFeature : IOverlayFeature
{
    private readonly KeyboardHookService _hookService;
    private readonly IGeneralSettingsService _settings;

    public KeyboardOverlayFeature(KeyboardHookService hookService, IGeneralSettingsService settings)
    {
        _hookService = hookService;
        _settings = settings;
    }

    public string Name => "键盘按键叠加层";

    public bool IsEnabled => _settings.KeyboardOverlayEnabled;

    public void Bind(OverlayRenderService renderService)
        => renderService.SetKeyboardStateProvider(_hookService);

    public void Start() => _hookService.SyncState();

    public void Stop() => _hookService.Uninstall();
}
