using NotifyRelay.Platforms.Windows.Services;
using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

/// <summary>
/// 覆盖层 - 键盘快捷键子页。
/// </summary>
public sealed partial class OverlayKeyboardPage : Page
{
    public KeyboardViewModel ViewModel => (KeyboardViewModel)DataContext;

    private KeyboardMappingConfig? _editingMapping;
    private KeyboardHookService? _hookService;

    // 按键监听状态
    private enum ListeningTarget { None, Source, Target }
    private ListeningTarget _listeningTarget = ListeningTarget.None;
    private readonly List<int> _capturedKeys = new();
    private bool _modifiersOnly = true;

    // 修饰键码
    private static readonly HashSet<int> ModifierKeys = new()
    {
        0xA0, 0xA1, // LShift, RShift
        0xA2, 0xA3, // LCtrl, RCtrl
        0xA4, 0xA5  // LAlt, RAlt
    };

    public OverlayKeyboardPage()
    {
        InitializeComponent();
        _hookService = Ioc.Default.GetService<KeyboardHookService>();
        UpdateEmptyMessage();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_hookService != null)
            _hookService.KeyStateChanged += OnHookKeyStateChanged;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_hookService != null)
            _hookService.KeyStateChanged -= OnHookKeyStateChanged;
        StopListening();
    }

    private void UpdateEmptyMessage()
    {
        EmptyMessage.Visibility = ViewModel.Mappings.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    #region 按键监听（通过钩子事件）

    private void StartListening(ListeningTarget target)
    {
        _listeningTarget = target;
        _capturedKeys.Clear();
        _modifiersOnly = true;
        ListeningHint.Visibility = Visibility.Visible;

        var btn = target == ListeningTarget.Source ? SourceKeyButton : TargetKeyButton;
        var tb = target == ListeningTarget.Source ? SourceKeyText : TargetKeyText;
        tb.Text = "请按下按键...";
        tb.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 136, 136, 136));
        btn.Content = tb;
    }

    private void StopListening()
    {
        _listeningTarget = ListeningTarget.None;
        _capturedKeys.Clear();
        ListeningHint.Visibility = Visibility.Collapsed;

        SourceKeyText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        TargetKeyText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    private void FinishListening()
    {
        if (_listeningTarget == ListeningTarget.None) return;

        if (_capturedKeys.Count == 0)
        {
            StopListening();
            return;
        }

        var keys = _capturedKeys.ToList();
        var displayText = string.Join("+", keys.Select(KeyboardMappingConfig.GetKeyName));

        if (_listeningTarget == ListeningTarget.Source)
        {
            SourceKeyText.Text = displayText;
            SourceKeyButton.Content = SourceKeyText;
        }
        else
        {
            TargetKeyText.Text = displayText;
            TargetKeyButton.Content = TargetKeyText;
        }

        StopListening();
    }

    private void OnHookKeyStateChanged(object? sender, KeyStateChangedEventArgs e)
    {
        if (_listeningTarget == ListeningTarget.None) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            int vk = e.Key;

            if (e.IsPressed)
            {
                // 记录按键
                if (!_capturedKeys.Contains(vk))
                {
                    _capturedKeys.Add(vk);
                }

                // 如果是非修饰键，立即完成
                if (!ModifierKeys.Contains(vk))
                {
                    _modifiersOnly = false;
                    FinishListening();
                }
            }
            else
            {
                // 按键释放：如果只按了修饰键，延迟后完成
                if (ModifierKeys.Contains(vk) && _modifiersOnly && _capturedKeys.Count > 0)
                {
                    _ = DelayedFinish();
                }
            }
        });
    }

    private async Task DelayedFinish()
    {
        await System.Threading.Tasks.Task.Delay(150);
        if (_listeningTarget != ListeningTarget.None && _modifiersOnly && _capturedKeys.Count > 0)
        {
            FinishListening();
        }
    }

    private void SourceKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_listeningTarget != ListeningTarget.None) return;
        StartListening(ListeningTarget.Source);
    }

    private void TargetKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_listeningTarget != ListeningTarget.None) return;
        StartListening(ListeningTarget.Target);
    }

    #endregion

    #region 映射管理

    private void AddMappingButton_Click(object sender, RoutedEventArgs e)
    {
        _editingMapping = null;
        MappingNameBox.Text = string.Empty;
        SourceKeyText.Text = "点击设置";
        SourceKeyText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        SourceKeyButton.Content = SourceKeyText;
        TargetKeyText.Text = "点击设置";
        TargetKeyText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        TargetKeyButton.Content = TargetKeyText;
        DisplayTextBox.Text = string.Empty;
        EnabledSwitch.IsOn = true;

        MappingDialog.XamlRoot = this.XamlRoot;
        _ = MappingDialog.ShowAsync();
    }

    private void EditMappingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyboardMappingConfig mapping)
        {
            _editingMapping = mapping;
            MappingNameBox.Text = mapping.Name;
            SourceKeyText.Text = string.Join("+", mapping.SourceKeys.Select(KeyboardMappingConfig.GetKeyName));
            SourceKeyText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            SourceKeyButton.Content = SourceKeyText;
            TargetKeyText.Text = mapping.TargetKey > 0 ? KeyboardMappingConfig.GetKeyName(mapping.TargetKey) : "点击设置";
            TargetKeyText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            TargetKeyButton.Content = TargetKeyText;
            DisplayTextBox.Text = mapping.DisplayText ?? string.Empty;
            EnabledSwitch.IsOn = mapping.Enabled;

            MappingDialog.XamlRoot = this.XamlRoot;
            _ = MappingDialog.ShowAsync();
        }
    }

    private void DeleteMappingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyboardMappingConfig mapping)
        {
            ViewModel.RemoveMapping(mapping);
            UpdateEmptyMessage();
        }
    }

    private void MappingDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var name = MappingNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            args.Cancel = true;
            return;
        }

        var sourceKeys = new List<int>();
        if (SourceKeyText.Text != "点击设置")
        {
            foreach (var part in SourceKeyText.Text.Split('+'))
            {
                var vk = ParseKeyName(part.Trim());
                if (vk > 0) sourceKeys.Add(vk);
            }
        }

        if (sourceKeys.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        int targetKey = 0;
        if (TargetKeyText.Text != "点击设置")
        {
            targetKey = ParseKeyName(TargetKeyText.Text.Trim());
        }

        var displayText = DisplayTextBox.Text.Trim();
        var enabled = EnabledSwitch.IsOn;

        var mapping = new KeyboardMappingConfig
        {
            Id = _editingMapping?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Name = name,
            SourceKeys = sourceKeys,
            TargetKey = targetKey,
            DisplayText = string.IsNullOrEmpty(displayText) ? null : displayText,
            Enabled = enabled
        };

        if (_editingMapping != null)
        {
            ViewModel.UpdateMapping(mapping);
        }
        else
        {
            ViewModel.AddMapping(mapping);
        }

        UpdateEmptyMessage();
    }

    private void MappingDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        StopListening();
    }

    private static int ParseKeyName(string name)
    {
        return name switch
        {
            "Backspace" => 0x08,
            "Tab" => 0x09,
            "Enter" => 0x0D,
            "Shift" => 0x10,
            "Ctrl" => 0x11,
            "Alt" => 0x12,
            "CapsLock" => 0x14,
            "Escape" => 0x1B,
            "Space" => 0x20,
            "LShift" => 0xA0,
            "RShift" => 0xA1,
            "LCtrl" => 0xA2,
            "RCtrl" => 0xA3,
            "LAlt" => 0xA4,
            "RAlt" => 0xA5,
            _ when name.StartsWith("F") && int.TryParse(name[1..], out int fNum) && fNum is >= 1 and <= 24 => 0x6F + fNum,
            _ when name.Length == 1 && char.IsLetterOrDigit(name[0]) => name[0] switch
            {
                >= '0' and <= '9' => name[0],
                >= 'A' and <= 'Z' => name[0],
                >= 'a' and <= 'z' => char.ToUpper(name[0]),
                _ => 0
            },
            _ when name.StartsWith("0x") && int.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex) => hex,
            _ => 0
        };
    }

    #endregion
}
