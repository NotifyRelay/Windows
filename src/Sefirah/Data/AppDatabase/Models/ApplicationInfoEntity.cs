using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;
using NotifyRelay.Utils;
using SQLite;

namespace NotifyRelay.Data.AppDatabase.Models;

public partial class ApplicationInfoEntity
{
    [PrimaryKey]
    public string PackageName { get; set; } = string.Empty;

    public string AppName { get; set; } = string.Empty;

    [Column("AppDeviceInfo")]
    public string AppDeviceInfoJson { get; set; } = string.Empty;

    [Ignore]
    public List<AppDeviceInfo> AppDeviceInfoList
    {
        get => JsonSerializer.Deserialize<List<AppDeviceInfo>>(AppDeviceInfoJson) ?? new List<AppDeviceInfo>();
        set => AppDeviceInfoJson = JsonSerializer.Serialize(value);
    }

    #region Helpers
    internal ApplicationInfo ToApplicationInfo(string deviceId)
    {
        var deviceInfo = AppDeviceInfoList.FirstOrDefault(d => d.DeviceId == deviceId) ?? new AppDeviceInfo(deviceId, NotificationFilter.ToastFeed);
        return new ApplicationInfo(PackageName, AppName, IconUtils.GetAppIconPath(PackageName), deviceInfo);
    }

    internal static async Task<ApplicationInfoEntity> FromApplicationInfo(string packageName, string appName, string deviceId)
    {
        List<AppDeviceInfo> appDeviceInfoList = new List<AppDeviceInfo> { new(deviceId, NotificationFilter.ToastFeed) };
        return new ApplicationInfoEntity
        {
            PackageName = packageName,
            AppName = appName,
            AppDeviceInfoJson = JsonSerializer.Serialize(appDeviceInfoList)
        };
    }
    #endregion
}
