using System.Runtime.CompilerServices;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Views;

namespace NotifyRelay.ViewModels.Settings;

public class WallpaperOverlayViewModel : INotifyPropertyChanged
{
    private readonly IGeneralSettingsService _generalSettingsService;
    private bool _isEnabled;
    private string _displayText = "壁纸层显示内容";
    private int _fontSize = 24;
    private string _textColor = "#FFFFFF";
    private string _textAlignment = "Center";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            _generalSettingsService.WallpaperOverlayEnabled = value;
            OnPropertyChanged();
            if (value)
            {
                WallpaperOverlayWindow.ShowOverlay(DisplayText);
                WallpaperOverlayWindow.UpdateFontSize(FontSize);
                WallpaperOverlayWindow.UpdateTextColor(TextColor);
            }
            else
            {
                WallpaperOverlayWindow.HideOverlay();
            }
        }
    }

    public string DisplayText
    {
        get => _displayText;
        set
        {
            _displayText = value;
            _generalSettingsService.WallpaperOverlayText = value;
            OnPropertyChanged();
            WallpaperOverlayWindow.UpdateDisplayText(value);
        }
    }

    public int FontSize
    {
        get => _fontSize;
        set
        {
            if (_fontSize == value) return;
            _fontSize = value;
            _generalSettingsService.WallpaperOverlayFontSize = value;
            OnPropertyChanged();
            WallpaperOverlayWindow.UpdateFontSize(value);
        }
    }

    public string TextColor
    {
        get => _textColor;
        set
        {
            _textColor = value;
            _generalSettingsService.WallpaperOverlayTextColor = value;
            OnPropertyChanged();
            WallpaperOverlayWindow.UpdateTextColor(value);
        }
    }

    public string TextAlignment
    {
        get => _textAlignment;
        set
        {
            if (_textAlignment == value) return;
            _textAlignment = value;
            _generalSettingsService.WallpaperOverlayTextAlignment = value;
            OnPropertyChanged();
            WallpaperOverlayWindow.UpdateTextAlignment(value);
        }
    }

    public ObservableCollection<string> FontSizeOptions { get; } = new()
    {
        "12", "14", "16", "18", "20", "24", "28", "32", "36", "48", "72"
    };

    public ObservableCollection<string> TextAlignmentOptions { get; } = new()
    {
        "左对齐", "居中", "右对齐"
    };

    public WallpaperOverlayViewModel()
    {
        _generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>()!;
        LoadSettings();
    }

    private void LoadSettings()
    {
        IsEnabled = _generalSettingsService.WallpaperOverlayEnabled;
        DisplayText = _generalSettingsService.WallpaperOverlayText ?? "壁纸层显示内容";
        FontSize = _generalSettingsService.WallpaperOverlayFontSize;
        TextColor = _generalSettingsService.WallpaperOverlayTextColor ?? "#FFFFFF";
        TextAlignment = _generalSettingsService.WallpaperOverlayTextAlignment ?? "居中";
    }

    public void ShowOverlay()
    {
        WallpaperOverlayWindow.ShowOverlay(DisplayText);
        WallpaperOverlayWindow.UpdateFontSize(FontSize);
        WallpaperOverlayWindow.UpdateTextColor(TextColor);
    }

    public void HideOverlay()
    {
        WallpaperOverlayWindow.HideOverlay();
    }

    public bool IsOverlayVisible()
    {
        return WallpaperOverlayWindow.IsVisible();
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}