using System.Runtime.InteropServices;

namespace NotifyRelay.Native;

/// <summary>
/// logi-battery Rust 库的 C ABI 绑定（cdylib: logi_battery.dll）。
/// 所有函数 CallingConvention 均为 Cdecl，与 Rust extern "C" 对应。
/// FFI 文档：NotifyRelay.Overlay/logi-battery/docs/ffi.md
/// </summary>
public static class LogiBatteryNative
{
    private const string DllName = "logi_battery";

    /// <summary>
    /// 枚举所有 Logitech HID++ 设备及电量快照。
    /// 成功：返回 0，out 指向已分配的设备列表；使用后必须调用 lb_free_devices。
    /// 失败：返回负数（-1 空指针 / -2 枚举失败），通过 lb_last_error 查看错误信息。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lb_enumerate_devices(out LbDeviceList outList);

    /// <summary>
    /// 重新读取指定下标的设备电量（每次都会重新探测，耗时较长，请勿在 UI 线程调用）。
    /// 返回 0=成功；-1=参数无效；-2=枚举失败；-3=设备无电量数据；-4=下标越界。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lb_read_battery(int index, out LbBatteryInfo outInfo);

    /// <summary>
    /// 释放 lb_enumerate_devices 返回的设备列表（Rust 侧分配的堆内存）。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lb_free_devices(LbDeviceList list);

    /// <summary>
    /// 返回最后一次调用的错误信息（NUL 结尾 UTF-8，线程局部存储）。
    /// 指针在下次调用任何 lb_* 函数前有效，无需释放。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr lb_last_error();
}

/// <summary>
/// C 兼容设备信息结构（含电量快照）。与 Rust LbDeviceInfo 内存布局一致。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
public struct LbDeviceInfo
{
    public ushort vendor_id;
    public ushort product_id;

    /// <summary>NUL 结尾 UTF-8，长度 256 字节（含终止符）。</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public byte[] name;

    public byte slot;
    public byte online;
    public byte has_battery;
    public byte percentage;
    public byte level;
    public byte status;
}

/// <summary>
/// C 兼容设备列表结构。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct LbDeviceList
{
    public IntPtr devices; // 指向 LbDeviceInfo 数组（Rust 堆分配）
    public int count;
}

/// <summary>
/// C 兼容电池信息结构（lb_read_battery 输出）。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LbBatteryInfo
{
    public byte percentage;
    public byte level;
    public byte status;
}

/// <summary>
/// lb_battery_level 枚举。
/// </summary>
public enum LbBatteryLevel : byte
{
    Critical = 0,
    Low = 1,
    Good = 2,
    Full = 3,
    Unknown = 4,
}

/// <summary>
/// lb_battery_status 枚举：与 GetGlyph/GetColorBytes 的 isCharging 参数对应关系
/// - Charging / ChargingSlow → isCharging=true
/// - Full / Discharging / Error / Unknown → isCharging=false
/// </summary>
public enum LbBatteryStatus : byte
{
    Discharging = 0,
    Charging = 1,
    ChargingSlow = 2,
    Full = 3,
    Error = 4,
    Unknown = 5,
}
