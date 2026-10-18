using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

/// <summary>
/// 应用角标服务：设置与清除任务栏角标数字。
/// </summary>
public interface INotificationBadgeService
{
    /// <summary>
    /// 按当前活动设备设置角标数字；该设备未开启 ShowBadge 时不做任何操作
    /// </summary>
    Task UpdateBadgeAsync(int count, PairedDevice? activeDevice);

    /// <summary>
    /// 清除应用角标
    /// </summary>
    Task ClearBadgeAsync();
}
