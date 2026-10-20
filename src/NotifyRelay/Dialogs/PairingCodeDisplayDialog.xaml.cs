namespace NotifyRelay.Dialogs;

/// <summary>
/// 配对发起端的配对码展示对话框：本机生成配对码，由对端设备输入。
/// </summary>
public sealed partial class PairingCodeDisplayDialog : ContentDialog
{
    /// <summary>目标设备名称</summary>
    public string DeviceName { get; }

    /// <summary>本机生成、需在对端输入的 6 位配对码</summary>
    public string PairingCode { get; }

    public PairingCodeDisplayDialog(string deviceName, string pairingCode)
    {
        DeviceName = deviceName;
        PairingCode = pairingCode;
        InitializeComponent();
    }
}
