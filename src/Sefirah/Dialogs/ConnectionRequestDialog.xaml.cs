using NotifyRelay.ViewModels.Dialogs;

namespace NotifyRelay.Dialogs;
public sealed partial class ConnectionRequestDialog : ContentDialog
{
    public ConnectionRequestViewModel ViewModel
    {
        get => (ConnectionRequestViewModel)DataContext;
        private set => DataContext = value;
    }

    public ConnectionRequestDialog(string deviceName, Frame frame)
    {
        InitializeComponent();
        ViewModel = new ConnectionRequestViewModel(deviceName, frame);
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.OnConnectClick();
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        base.Hide();
    }
}
