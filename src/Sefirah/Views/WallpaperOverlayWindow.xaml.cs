using Microsoft.UI.Windowing;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Helpers;
using WinRT.Interop;

namespace NotifyRelay.Views;

public sealed partial class WallpaperOverlayWindow : Window
{
    private static WallpaperOverlayWindow? _instance;
    public static WallpaperOverlayWindow? Instance => _instance;
    private bool _isInitialized;
    private IGeneralSettingsService? _generalSettingsService;
    private Brush _textForeground = new SolidColorBrush(Microsoft.UI.Colors.White);

    public Brush TextForeground
    {
        get => _textForeground;
        private set
        {
            _textForeground = value;
        }
    }

    private string _displayText = string.Empty;

    public string DisplayText
    {
        get => _displayText;
        set
        {
            _displayText = value;
            RenderMarkdown();
        }
    }

    public int FontSize
    {
        get => (int)DisplayTextBlock.FontSize;
        set => DisplayTextBlock.FontSize = value;
    }

    public HorizontalAlignment HorizontalAlignment
    {
        get => DisplayTextBlock.HorizontalAlignment;
        set => DisplayTextBlock.HorizontalAlignment = value;
    }

    public TextAlignment TextAlignment
    {
        get => DisplayTextBlock.TextAlignment;
        set => DisplayTextBlock.TextAlignment = value;
    }

    public WallpaperOverlayWindow()
    {
        InitializeComponent();
        _instance = this;
#if WINDOWS
        _generalSettingsService = Ioc.Default.GetService<IGeneralSettingsService>();
#endif
        InitializeWindow();
    }

    private void InitializeWindow()
    {
#if WINDOWS
        var hWnd = WindowNative.GetWindowHandle(this);

        OverlappedPresenter overlappedPresenter = (AppWindow.Presenter as OverlappedPresenter) ?? OverlappedPresenter.Create();
        overlappedPresenter.IsMaximizable = false;
        overlappedPresenter.IsMinimizable = false;
        overlappedPresenter.IsResizable = true;
        overlappedPresenter.SetBorderAndTitleBar(false, false);

        if (AppWindow != null)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 400, Height = 200 });
        }

        LoadSettings();
        RestoreWindowPosition(hWnd);

        AppWindow?.Changed += AppWindow_Changed;
#endif
    }

    private void LoadSettings()
    {
        if (_generalSettingsService == null) return;

        _displayText = _generalSettingsService.WallpaperOverlayText ?? "**壁纸层显示内容**";

        if (_generalSettingsService.WallpaperOverlayFontSize > 0)
        {
            FontSize = _generalSettingsService.WallpaperOverlayFontSize;
        }

        _textForeground = ColorHelper.CreateBrush(_generalSettingsService.WallpaperOverlayTextColor ?? "#FFFFFF");
        DisplayTextBlock.Foreground = _textForeground;

        var alignment = _generalSettingsService.WallpaperOverlayTextAlignment ?? "居中";
        HorizontalAlignment = alignment switch
        {
            "左对齐" => Microsoft.UI.Xaml.HorizontalAlignment.Left,
            "右对齐" => Microsoft.UI.Xaml.HorizontalAlignment.Right,
            _ => Microsoft.UI.Xaml.HorizontalAlignment.Center
        };
        TextAlignment = alignment switch
        {
            "左对齐" => TextAlignment.Left,
            "右对齐" => TextAlignment.Right,
            _ => TextAlignment.Center
        };

        RenderMarkdown();
    }

    private void RenderMarkdown()
    {
        MarkdownRenderer.RenderToInlines(_displayText, DisplayTextBlock.Inlines, _textForeground, FontSize);
    }

    private void RestoreWindowPosition(nint hWnd)
    {
#if WINDOWS
        if (_generalSettingsService == null) return;
        if (AppWindow == null) return;

        int x = _generalSettingsService.WallpaperOverlayX;
        int y = _generalSettingsService.WallpaperOverlayY;
        int width = _generalSettingsService.WallpaperOverlayWidth;
        int height = _generalSettingsService.WallpaperOverlayHeight;

        if (width > 0 && height > 0)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        }

        if (x != 0 || y != 0)
        {
            Platforms.Windows.Interop.InteropHelpers.SetWindowPos(hWnd, IntPtr.Zero, x, y, 0, 0,
                Platforms.Windows.Interop.InteropHelpers.SWP_NOSIZE | Platforms.Windows.Interop.InteropHelpers.SWP_NOACTIVATE | Platforms.Windows.Interop.InteropHelpers.SWP_NOZORDER);
        }

        _isInitialized = true;
#endif
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
#if WINDOWS
        if (!_isInitialized) return;

        if (args.DidPositionChange || args.DidSizeChange)
        {
            SaveWindowPosition();
        }
#endif
    }

    private void SaveWindowPosition()
    {
#if WINDOWS
        if (_generalSettingsService == null) return;

        var position = AppWindow.Position;
        var size = AppWindow.Size;

        _generalSettingsService.WallpaperOverlayX = position.X;
        _generalSettingsService.WallpaperOverlayY = position.Y;
        _generalSettingsService.WallpaperOverlayWidth = size.Width;
        _generalSettingsService.WallpaperOverlayHeight = size.Height;
#endif
    }

    public void ShowOverlayInternal()
    {
#if WINDOWS
        var hWnd = WindowNative.GetWindowHandle(this);

        AppWindow?.Show();
        Platforms.Windows.Interop.InteropHelpers.SetWindowToWallpaperLayer(hWnd);

        RestoreWindowPosition(hWnd);
#endif
    }

    public static void ShowOverlay(string text = "")
    {
        if (_instance == null)
        {
            _instance = new WallpaperOverlayWindow();
        }

        if (!string.IsNullOrEmpty(text))
        {
            _instance.DisplayText = text;
        }

        _instance.ShowOverlayInternal();
    }

    public static void HideOverlay()
    {
        _instance?.AppWindow?.Hide();
    }

    public static bool IsVisible()
    {
        return _instance != null && _instance.AppWindow?.IsVisible == true;
    }

    public static void UpdateDisplayText(string text)
    {
        if (_instance != null)
        {
            _instance.DisplayText = text;
        }
    }

    public static void UpdateFontSize(int fontSize)
    {
        if (_instance != null)
        {
            _instance.FontSize = fontSize;
            _instance.RenderMarkdown();
        }
    }

    public static void UpdateTextColor(string colorHex)
    {
        if (_instance != null)
        {
            _instance._textForeground = ColorHelper.CreateBrush(colorHex);
            _instance.DisplayTextBlock.Foreground = _instance._textForeground;
            _instance.RenderMarkdown();
        }
    }

    public static void UpdateTextAlignment(string alignment)
    {
        if (_instance != null)
        {
            _instance.HorizontalAlignment = alignment switch
            {
                "左对齐" => Microsoft.UI.Xaml.HorizontalAlignment.Left,
                "右对齐" => Microsoft.UI.Xaml.HorizontalAlignment.Right,
                _ => Microsoft.UI.Xaml.HorizontalAlignment.Center
            };
            _instance.TextAlignment = alignment switch
            {
                "左对齐" => TextAlignment.Left,
                "右对齐" => TextAlignment.Right,
                _ => TextAlignment.Center
            };
        }
    }
}
