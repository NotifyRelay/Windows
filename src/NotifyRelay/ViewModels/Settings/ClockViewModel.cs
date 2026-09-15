using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Overlay;

namespace NotifyRelay.ViewModels.Settings;

/// <summary>
/// 时间浮窗子页 ViewModel：显示开关、目标屏幕、X/Y 位置、颜色、描边、缩放、格式，
/// 并把配置推送到覆盖层渲染服务。复用 HeartRateViewModel 的 ScreenOption 作为屏幕下拉项。
/// </summary>
public class ClockViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _settings;
    private readonly OverlayRenderService? _renderService;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<ScreenOption> Screens { get; } = [];

    private ScreenOption? _selectedScreen;
    public ScreenOption? SelectedScreen
    {
        get => _selectedScreen;
        set
        {
            _selectedScreen = value;
            if (value != null)
                _settings.ClockTargetScreen = value.Id;
            OnPropertyChanged();
            PushConfig();
        }
    }

    public ClockViewModel()
    {
        _settings = Ioc.Default.GetRequiredService<IGeneralSettingsService>();
        _renderService = Ioc.Default.GetService<OverlayRenderService>();
        BuildScreenOptions();
    }

    private void BuildScreenOptions()
    {
        _selectedScreen = OverlayScreenOptions.BuildInto(Screens, _renderService, _settings.ClockTargetScreen);
    }

    // ===== 显示设置（持久化 + 推送渲染层） =====

    public bool ClockOverlayEnabled
    {
        get => _settings.ClockOverlayEnabled;
        set { _settings.ClockOverlayEnabled = value; OnPropertyChanged(); PushConfig(); }
    }

    public int XPercent
    {
        get => _settings.ClockXPercent;
        set { _settings.ClockXPercent = value; OnPropertyChanged(); PushConfig(); }
    }

    public int YPercent
    {
        get => _settings.ClockYPercent;
        set { _settings.ClockYPercent = value; OnPropertyChanged(); PushConfig(); }
    }

    public string ClockColor
    {
        get => _settings.ClockColor;
        set { _settings.ClockColor = value; OnPropertyChanged(); PushConfig(); }
    }

    /// <summary>文本描边粗细（像素，0.1~3，0 为无描边）。</summary>
    public float ClockTextOutlineWidth
    {
        get => _settings.ClockTextOutlineWidth;
        set { _settings.ClockTextOutlineWidth = value; OnPropertyChanged(); PushConfig(); }
    }

    /// <summary>整体显示大小缩放（0.5~2）。</summary>
    public float ClockScale
    {
        get => _settings.ClockScale;
        set { _settings.ClockScale = value; OnPropertyChanged(); PushConfig(); }
    }

    /// <summary>是否显示秒。</summary>
    public bool ClockShowSeconds
    {
        get => _settings.ClockShowSeconds;
        set { _settings.ClockShowSeconds = value; OnPropertyChanged(); PushConfig(); }
    }

    /// <summary>是否使用 24 小时制。</summary>
    public bool ClockUse24Hour
    {
        get => _settings.ClockUse24Hour;
        set { _settings.ClockUse24Hour = value; OnPropertyChanged(); PushConfig(); }
    }

    /// <summary>把当前显示配置推送到渲染服务。</summary>
    private void PushConfig()
    {
        _renderService?.SetClockConfig(
            _settings.ClockOverlayEnabled,
            _settings.ClockTargetScreen,
            _settings.ClockXPercent,
            _settings.ClockYPercent,
            _settings.ClockColor,
            _settings.ClockTextOutlineWidth,
            _settings.ClockScale,
            _settings.ClockShowSeconds,
            _settings.ClockUse24Hour);
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
