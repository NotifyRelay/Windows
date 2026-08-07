namespace NotifyRelay.Dialogs;

public sealed partial class PairingCodeDialog : ContentDialog
{
    /// <summary>
    /// 发起端设备名称
    /// </summary>
    public string DeviceName { get; }

    /// <summary>
    /// 用户输入的配对码
    /// </summary>
    public string PairingCode => CodeTextBox.Text;

    /// <summary>
    /// 是否配对成功
    /// </summary>
    public bool IsPaired { get; private set; }

    public PairingCodeDialog(string deviceName = "未知设备")
    {
        DeviceName = deviceName;
        InitializeComponent();
    }

    private void OnCodeTextChanged(object sender, TextChangedEventArgs e)
    {
        // 限制只能输入数字
        var text = CodeTextBox.Text;
        var filtered = string.Empty;
        foreach (var c in text)
        {
            if (char.IsDigit(c))
                filtered += c;
        }
        if (filtered.Length > 6)
            filtered = filtered[..6];

        if (filtered != text)
        {
            CodeTextBox.Text = filtered;
            CodeTextBox.SelectionStart = filtered.Length;
        }

        // 清除错误提示
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var code = CodeTextBox.Text.Trim();
        if (code.Length != 6)
        {
            ErrorText.Text = "请输入完整的 6 位数字配对码";
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        IsPaired = true;
    }

    /// <summary>
    /// 显示错误信息
    /// </summary>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 重置对话框状态
    /// </summary>
    public void Reset()
    {
        CodeTextBox.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
        IsPaired = false;
    }
}
