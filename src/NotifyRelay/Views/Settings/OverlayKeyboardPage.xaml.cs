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

    public OverlayKeyboardPage()
    {
        InitializeComponent();
        LoadKeyOptions();
        UpdateEmptyMessage();
    }

    private void LoadKeyOptions()
    {
        var keys = KeyboardViewModel.GetAvailableKeys();

        void PopulateCombo(ComboBox combo)
        {
            combo.Items.Clear();
            foreach (var key in keys)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = key.DisplayName,
                    Tag = key.VkCode
                });
            }
        }

        PopulateCombo(SourceKey1Combo);
        PopulateCombo(SourceKey2Combo);
        PopulateCombo(SourceKey3Combo);
        PopulateCombo(TargetKeyCombo);
    }

    private void UpdateEmptyMessage()
    {
        EmptyMessage.Visibility = ViewModel.Mappings.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddMappingButton_Click(object sender, RoutedEventArgs e)
    {
        _editingMapping = null;
        MappingNameBox.Text = string.Empty;
        ActivationKeyCombo.SelectedIndex = 0;
        SourceKey1Combo.SelectedIndex = -1;
        SourceKey2Combo.SelectedIndex = -1;
        SourceKey3Combo.SelectedIndex = -1;
        TargetKeyCombo.SelectedIndex = -1;
        DisplayTextBox.Text = string.Empty;
        EnabledSwitch.IsOn = true;

        _ = MappingDialog.ShowAsync();
    }

    private void EditMappingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is KeyboardMappingConfig mapping)
        {
            _editingMapping = mapping;
            MappingNameBox.Text = mapping.Name;
            SetComboValue(ActivationKeyCombo, mapping.ActivationKey.ToString());
            SetComboValue(SourceKey1Combo, mapping.SourceKeys.ElementAtOrDefault(0).ToString());
            SetComboValue(SourceKey2Combo, mapping.SourceKeys.ElementAtOrDefault(1).ToString());
            SetComboValue(SourceKey3Combo, mapping.SourceKeys.ElementAtOrDefault(2).ToString());
            SetComboValue(TargetKeyCombo, mapping.TargetKey.ToString());
            DisplayTextBox.Text = mapping.DisplayText ?? string.Empty;
            EnabledSwitch.IsOn = mapping.Enabled;

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

    private void SetComboValue(ComboBox combo, string? vkCodeStr)
    {
        if (string.IsNullOrEmpty(vkCodeStr)) return;

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == vkCodeStr)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private int? GetComboValue(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is int vkCode)
        {
            return vkCode;
        }
        return null;
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
        if (GetComboValue(SourceKey1Combo) is int key1)
            sourceKeys.Add(key1);
        if (GetComboValue(SourceKey2Combo) is int key2)
            sourceKeys.Add(key2);
        if (GetComboValue(SourceKey3Combo) is int key3)
            sourceKeys.Add(key3);

        if (sourceKeys.Count == 0)
        {
            args.Cancel = true;
            return;
        }

        if (GetComboValue(TargetKeyCombo) is not int targetKey)
        {
            args.Cancel = true;
            return;
        }

        var activationKey = GetComboValue(ActivationKeyCombo) ?? -1;
        var displayText = DisplayTextBox.Text.Trim();
        var enabled = EnabledSwitch.IsOn;

        var mapping = new KeyboardMappingConfig
        {
            Id = _editingMapping?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Name = name,
            ActivationKey = activationKey,
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
}
