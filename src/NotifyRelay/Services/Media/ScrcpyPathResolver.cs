using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Extensions;
using NotifyRelay.Utils;

namespace NotifyRelay.Services.Media;

/// <summary>
/// scrcpy 可执行文件路径解析：读取设置 → 校验存在性 → 缺失时弹窗引导用户手动选择。
/// </summary>
/// <remarks>
/// 承接原 <c>ScreenMirrorService</c> 中的 <c>ResolveScrcpyPathAsync</c>，
/// 以及 <c>SelectScrcpyLocationClick</c> 的实体（该方法为公开 API，主类保留同名转发）。
/// </remarks>
internal sealed class ScrcpyPathResolver(
    ILogger<ScreenMirrorService> logger,
    IUserSettingsService userSettingsService,
    IAdbService adbService,
    Microsoft.UI.Dispatching.DispatcherQueue? dispatcher)
{
    /// <summary>
    /// 解析 scrcpy 可执行文件路径；未找到时弹窗让用户选择，仍无效则返回 null（调用方短路返回 false）。
    /// </summary>
    public async Task<string?> ResolveAsync()
    {
        var scrcpyPath = userSettingsService.GeneralSettingsService.ScrcpyPath;
        if (!File.Exists(scrcpyPath))
        {
            logger.LogError("未在路径找到 scrcpy：{ScrcpyPath}", scrcpyPath);
            var result = await dispatcher!.EnqueueAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = App.MainWindow.Content!.XamlRoot,
                    Title = "ScrcpyNotFound".GetLocalizedResource(),
                    Content = "ScrcpyNotFoundDescription".GetLocalizedResource(),
                    PrimaryButtonText = "SelectLocation".GetLocalizedResource(),
                    DefaultButton = ContentDialogButton.Primary,
                    CloseButtonText = "Dismiss".GetLocalizedResource()
                };

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult is ContentDialogResult.Primary)
                {
                    scrcpyPath = await PickLocationAsync();
                    return !string.IsNullOrEmpty(scrcpyPath) && File.Exists(scrcpyPath);
                }
                return false;
            });

            if (!result) return null;
        }

        return scrcpyPath;
    }

    /// <summary>
    /// 弹窗让用户选择 scrcpy 可执行文件，并同步 adb.exe 伴生工具路径。
    /// </summary>
    public async Task<string> PickLocationAsync()
    {
        var file = await PickerHelper.PickFileAsync();
        if (file?.Path is string path)
        {
            userSettingsService.GeneralSettingsService.ScrcpyPath = path;
            ToolPathHelper.TrySetCompanionTool(path, "adb.exe", p => userSettingsService.GeneralSettingsService.AdbPath = p);
            await adbService.StartAsync();
            return path;
        }
        return string.Empty;
    }
}
