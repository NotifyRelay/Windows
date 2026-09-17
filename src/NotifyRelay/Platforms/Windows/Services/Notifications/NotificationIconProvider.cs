using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications.Builder;
using NotifyRelay.Utils;

namespace NotifyRelay.Platforms.Windows.Services.Notifications;

/// <summary>
/// 管理临时图标目录（清理过期图标）与远程通知图标的下载/落盘。
/// </summary>
internal sealed class NotificationIconProvider
{
    private static readonly TimeSpan TempIconMaxAge = TimeSpan.FromDays(1); // 清理 1 天以前的临时图标
    private const string TempIconsFolderName = "Sefirah-pc-icons";

    internal static string GetTempIconsDirectory()
    {
        string tempPath = Path.GetTempPath();
        string tempIconsDirectory = Path.Combine(tempPath, TempIconsFolderName);
        try
        {
            Directory.CreateDirectory(tempIconsDirectory);

            // 清理超过阈值的旧文件
            try
            {
                var files = Directory.GetFiles(tempIconsDirectory);
                var expireBefore = DateTime.UtcNow - TempIconMaxAge;
                foreach (var f in files)
                {
                    try
                    {
                        var info = new FileInfo(f);
                        if (info.Exists && info.LastWriteTimeUtc < expireBefore)
                        {
                            info.Delete();
                        }
                    }
                    catch
                    {
                        // 忽略单个文件删除错误
                    }
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
        catch
        {
            // ignore
        }

        return tempIconsDirectory;
    }

    /// <summary>
    /// 解析远程通知图标：优先使用应用包图标，否则将 largeIcon 落盘到临时目录，
    /// 并将解析到的图标设置到 <paramref name="builder"/> 上。
    /// </summary>
    internal static async Task ResolveIconAsync(
        AppNotificationBuilder builder,
        JsonElement root,
        string? appPackage,
        string? largeIcon,
        ILogger logger)
    {
        if (!string.IsNullOrEmpty(appPackage))
        {
            var iconUri = await IconUtils.GetAppIconUriAsync(appPackage);
            var appIconExists = IconUtils.AppIconExists(appPackage);


            if (iconUri is not null)
            {
                try
                {
                    if (iconUri.Scheme.Equals("ms-appdata", StringComparison.OrdinalIgnoreCase) || iconUri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var storageFile = await StorageFile.GetFileFromApplicationUriAsync(iconUri);

                            string tempIconsDirectory = GetTempIconsDirectory();

                            string tempFileName = $"{appPackage}_{DateTime.UtcNow.Ticks}.png";
                            string tempFilePath = Path.Combine(tempIconsDirectory, tempFileName);

                            // 复制图标文件到临时目录
                            var destFolder = await StorageFolder.GetFolderFromPathAsync(tempIconsDirectory);
                            await storageFile.CopyAsync(destFolder, tempFileName, NameCollisionOption.ReplaceExisting);

                            // 使用 file:// URI 引用临时图标文件
                            var fileUri = new Uri($"file://{tempFilePath}");

                            builder.SetAppLogoOverride(fileUri, AppNotificationImageCrop.Circle);
                        }
                        catch (COMException comExLocal)
                        {
                            logger.LogDebug(comExLocal, "WinRT COM异常：无法读取本地图标 URI，回退使用原始 URI：{IconUri}", iconUri);
                            builder.SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle);
                        }
                        catch (Exception exLocal)
                        {
                            logger.LogWarning(exLocal, "无法读取本地图标 URI，回退使用原始 URI：{IconUri}", iconUri);
                            builder.SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle);
                        }
                    }
                    else
                    {
                        logger.LogDebug("设置通知图标为 {IconUri}", iconUri);
                        builder.SetAppLogoOverride(iconUri, AppNotificationImageCrop.Circle);
                    }
                }
                catch (COMException comEx)
                {
                    logger.LogDebug(comEx, "WinRT COM异常：设置通知图标时出错");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "设置通知图标时出错");
                }
            }
            else
            {
                var notificationKey = root.TryGetProperty("notificationKey", out var nkProp) ? nkProp.GetString() : null;
                if (!string.IsNullOrEmpty(largeIcon))
                {
                    try
                    {
                        string tempIconsDirectory = GetTempIconsDirectory();

                        string tempFileName = $"largeIcon_{DateTime.UtcNow.Ticks}.png";
                        string tempFilePath = Path.Combine(tempIconsDirectory, tempFileName);

                        var bytes = Convert.FromBase64String(largeIcon);
                        await File.WriteAllBytesAsync(tempFilePath, bytes);

                        var fileUri = new Uri($"file://{tempFilePath}");
                        logger.LogDebug("包名图标不存在，已保存大图标到临时目录：{FileUri}，通知键：{NotificationKey}", fileUri, notificationKey);
                        builder.SetAppLogoOverride(fileUri, AppNotificationImageCrop.Circle);
                    }
                    catch (COMException comEx)
                    {
                        logger.LogDebug(comEx, "WinRT COM异常：保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
                    }
                }
                else
                {
                    logger.LogDebug("未找到应用图标或大图标，通知键：{NotificationKey}，包名：{AppPackage}", notificationKey, appPackage);
                }
            }
        }
        else if (!string.IsNullOrEmpty(largeIcon))
        {
            var notificationKey = root.TryGetProperty("notificationKey", out var nkProp) ? nkProp.GetString() : null;
            try
            {
                string tempIconsDirectory = GetTempIconsDirectory();

                string tempFileName = $"largeIcon_{notificationKey}_{DateTime.UtcNow.Ticks}.png";
                string tempFilePath = Path.Combine(tempIconsDirectory, tempFileName);

                var bytes = Convert.FromBase64String(largeIcon);
                await File.WriteAllBytesAsync(tempFilePath, bytes);

                var fileUri = new Uri($"file://{tempFilePath}");
                logger.LogDebug("未设置包名，已保存大图标到临时目录：{FileUri}，通知键：{NotificationKey}", fileUri, notificationKey);
                builder.SetAppLogoOverride(fileUri, AppNotificationImageCrop.Circle);
            }
            catch (COMException comEx)
            {
                logger.LogDebug(comEx, "WinRT COM异常：保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "保存大图标到临时目录时出错，通知键：{NotificationKey}", notificationKey);
            }
        }
        else
        {
            logger.LogDebug("未设置图标：LargeIcon 为空");
        }
    }
}
