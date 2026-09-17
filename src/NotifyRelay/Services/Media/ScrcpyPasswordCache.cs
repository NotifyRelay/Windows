using CommunityToolkit.WinUI;
using NotifyRelay.Dialogs;

namespace NotifyRelay.Services.Media;

/// <summary>
/// scrcpy 解锁密码缓存与密码输入对话框（由 ScreenMirrorService 整体搬入）。
/// 持有 passwordCache 字典（deviceId -> 密码/缓存时间/超时分钟）。
/// 注意：该字典为普通 Dictionary（非线程安全），沿用原有并发假设，不得改为并发集合。
/// 不启动进程、不知道 scrcpy 参数。
/// </summary>
internal sealed class ScrcpyPasswordCache
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? dispatcher;

    // Password cache: deviceId -> (password, cachedTime, timeoutMinutes)
    private readonly Dictionary<string, (string Password, DateTime CachedAt, int TimeoutMinutes)> passwordCache = [];

    public ScrcpyPasswordCache(Microsoft.UI.Dispatching.DispatcherQueue? dispatcher)
    {
        // dispatcher 由主类在字段初始化时机求值后注入，保持与拆分前等价的取值时机
        this.dispatcher = dispatcher;
    }

    public string? GetCachedPassword(string deviceId, int currentTimeout)
    {
        if (passwordCache.TryGetValue(deviceId, out var cacheEntry))
        {
            var (password, cachedAt, cachedTimeout) = cacheEntry;

            if (currentTimeout == cachedTimeout && DateTime.Now <= cachedAt.AddMinutes(cachedTimeout))
            {
                return password;
            }
            passwordCache.Remove(deviceId);
        }

        return null;
    }

    public void CachePassword(string deviceId, string password, int timeoutMinutes)
    {
        passwordCache[deviceId] = (password, DateTime.Now, timeoutMinutes);
    }

    public async Task<string?> ShowPasswordInputDialog()
    {
        string? password = null;

        await dispatcher!.EnqueueAsync(async () =>
        {
            var dialog = new PasswordInputDialog
            {
                XamlRoot = App.MainWindow.Content!.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                password = dialog.Password;
            }
        });

        return password;
    }
}
