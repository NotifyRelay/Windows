using Microsoft.UI.Xaml.Controls;

namespace NotifyRelay.Dialogs;

/// <summary>内联 <see cref="ContentDialog"/> 的公共装配。</summary>
public static class DialogHelper
{
    /// <summary>显示一个仅含关闭按钮的错误提示。</summary>
    public static async Task ShowErrorAsync(XamlRoot root, string content)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = root
        };
        await dialog.ShowAsync();
    }
}
