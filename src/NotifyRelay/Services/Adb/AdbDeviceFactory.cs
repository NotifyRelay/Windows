using AdvancedSharpAdbClient.Models;
using NotifyRelay.Data.Enums;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Services.Adb;

/// <summary>
/// <see cref="AdbDevice"/> 构造工厂：收敛设备对象初始化的字段映射与 USB/WIFI 判定规则。
/// </summary>
public static class AdbDeviceFactory
{
    /// <summary>
    /// 由 adb 设备数据构造基础设备对象（尚未解析 AndroidId，待设备上线后再回填）。
    /// </summary>
    /// <param name="deviceData">adb 设备数据。</param>
    /// <param name="attachDeviceData">
    /// 是否把 <paramref name="deviceData"/> 挂到结果对象上。
    /// 当该数据已从 adb devices 列表中消失（重查失败的回退分支）时必须传 false：
    /// 下游以 <c>DeviceData == null</c> 作为「该快照不可用于发起 adb 调用」的守卫
    /// （见 <c>AdbDeviceOperator.UninstallApp</c> / <c>ScreenMirrorService</c>），
    /// 挂上陈旧数据会解除该守卫。
    /// </param>
    public static AdbDevice CreateBasic(DeviceData deviceData, bool attachDeviceData = true)
    {
        var device = new AdbDevice
        {
            Serial = deviceData.Serial,
            Model = deviceData.Model ?? "Unknown",
            State = deviceData.State,
            Type = ResolveType(deviceData.Serial),
            AndroidId = ""
        };

        if (attachDeviceData)
        {
            device.DeviceData = deviceData;
        }

        return device;
    }

    /// <summary>
    /// 构造已完成 AndroidId 解析的设备对象。
    /// </summary>
    /// <param name="source">adb 设备数据（同时挂到结果的 DeviceData）。</param>
    /// <param name="model">写入结果的型号。</param>
    /// <param name="androidId">写入结果的 AndroidId。</param>
    public static AdbDevice CreateResolved(DeviceData source, string model, string androidId) => new()
    {
        Serial = source.Serial,
        Model = model,
        State = source.State,
        Type = ResolveType(source.Serial),
        AndroidId = androidId,
        DeviceData = source
    };

    /// <summary>
    /// 序列号判定：含 ':' 或 "tcp" 视为无线设备，否则为 USB 设备。
    /// </summary>
    public static DeviceType ResolveType(string serial)
        => serial.Contains(':') || serial.Contains("tcp") ? DeviceType.WIFI : DeviceType.USB;
}
