namespace NotifyRelay.Dialogs;

public sealed partial class PasswordInputDialog : ContentDialog
{
    public string Password => PasswordBox.Password;

    public PasswordInputDialog()
    {
        InitializeComponent();
    }
}
