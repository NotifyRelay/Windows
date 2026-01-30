using System.Runtime.InteropServices;
using Windows.Devices.Power;

namespace NotifyRelay.Services;

public interface ISystemInfoService
{
    int GetSystemBatteryLevel();
    bool GetSystemChargingStatus();
}

public class SystemInfoService(ILogger<SystemInfoService> logger) : ISystemInfoService
{
    /// <summary>
    /// 获取系统电量百分比
    /// </summary>
    public int GetSystemBatteryLevel()
    {
        try
        {
            // 使用Windows.Devices.Power API获取电量
            var batteryReport = Battery.AggregateBattery.GetReport();

            // 检查电量信息是否可用
            if (batteryReport.RemainingCapacityInMilliwattHours.HasValue &&
                batteryReport.FullChargeCapacityInMilliwattHours.HasValue &&
                batteryReport.FullChargeCapacityInMilliwattHours.Value > 0)
            {
                // 计算电量百分比
                var remainingCapacity = batteryReport.RemainingCapacityInMilliwattHours.Value;
                var fullCapacity = batteryReport.FullChargeCapacityInMilliwattHours.Value;
                var batteryLevel = (int)Math.Round((double)remainingCapacity / fullCapacity * 100);

                // 确保电量值在0-100之间
                return Math.Clamp(batteryLevel, 0, 100);
            }

            // 如果无法获取电量信息，返回默认值100%
            return 100;
        }
        catch (Exception ex)
        {
            logger.LogWarning("获取系统电量失败：{ex}", ex);
            // 异常情况下返回默认值100%
            return 100;
        }
    }

    /// <summary>
    /// 获取系统充电状态
    /// </summary>
    public bool GetSystemChargingStatus()
    {
        try
        {
            // 使用Windows API的GetSystemPowerStatus获取充电状态
            SYSTEM_POWER_STATUS powerStatus = new SYSTEM_POWER_STATUS();
            if (GetSystemPowerStatus(ref powerStatus))
            {
                // 如果交流电源连接或电池正在充电，则返回true
                return powerStatus.ACLineStatus == 1 || powerStatus.BatteryFlag == 8;
            }
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning("获取系统充电状态失败：{ex}", ex);
            // 异常情况下返回默认值false
            return false;
        }
    }

    // Windows API结构体和函数声明
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;          // AC电源状态：0=离线，1=在线，255=未知
        public byte BatteryFlag;           // 电池状态：1=高，2=低，4=临界，8=充电，128=无电池
        public byte BatteryLifePercent;    // 电池剩余百分比：0-100，255=未知
        public byte Reserved1;             // 保留
        public uint BatteryLifeTime;       // 电池剩余时间（秒），0xFFFFFFFF=未知
        public uint BatteryFullLifeTime;   // 电池满电时间（秒），0xFFFFFFFF=未知
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS lpSystemPowerStatus);
}
