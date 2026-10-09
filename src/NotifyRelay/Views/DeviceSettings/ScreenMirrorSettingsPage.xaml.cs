using Windows.System;

namespace NotifyRelay.Views.DeviceSettings;

public sealed partial class ScreenMirrorSettingsPage : DeviceSettingsSubPageBase
{
    public ScreenMirrorSettingsPage()
    {
        InitializeComponent();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Focus(FocusState.Pointer);
            e.Handled = true;
        }
    }
}
