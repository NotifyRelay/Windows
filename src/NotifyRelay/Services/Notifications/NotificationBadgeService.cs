using CommunityToolkit.WinUI;
using NotifyRelay.Data.Contracts;
using NotifyRelay.Data.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace NotifyRelay.Services.Notifications;

/// <summary>
/// 应用角标服务：设置与清除任务栏角标数字
/// </summary>
public class NotificationBadgeService(
    ILogger logger,
    Microsoft.UI.Dispatching.DispatcherQueue dispatcher) : INotificationBadgeService
{
    /// <summary>
    /// 按当前活动设备设置角标数字；该设备未开启 ShowBadge 时不做任何操作
    /// </summary>
    public async Task UpdateBadgeAsync(int count, PairedDevice? activeDevice)
    {
        if (activeDevice?.DeviceSettings.ShowBadge != true) return;

        try
        {
            await dispatcher.EnqueueAsync(() =>
            {
                XmlDocument badgeXml = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeNumber);
                XmlElement? badgeElement = badgeXml.SelectSingleNode("/badge") as XmlElement;
                badgeElement?.SetAttribute("value", count.ToString());
                BadgeNotification badge = new(badgeXml);
                BadgeUpdater badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
                badgeUpdater.Update(badge);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "设置角标失败");
        }
    }

    /// <summary>
    /// 清除应用角标（F2 修复：await 使 lambda 内异常可被捕获并记录）
    /// </summary>
    public async Task ClearBadgeAsync()
    {
        try
        {
            await dispatcher.EnqueueAsync(() =>
            {
                BadgeUpdater badgeUpdater = BadgeUpdateManager.CreateBadgeUpdaterForApplication();
                badgeUpdater.Clear();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "清除角标失败");
        }
    }
}