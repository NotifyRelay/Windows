using NotifyRelay.Worker.Services;

namespace NotifyRelay.Worker.Contracts;

/// <summary>显示器亮度同步工具契约：命令 + 状态 + 事件。</summary>
public interface IMonitorBrightnessTool
{
    /// <summary>同步是否运行中。</summary>
    bool IsRunning { get; }

    /// <summary>运行状态变化。</summary>
    event EventHandler? StatusChanged;

    /// <summary>启动亮度同步（读取系统亮度并持续跟随）。</summary>
    void StartSync();

    /// <summary>停止亮度同步。</summary>
    void StopSync();

    /// <summary>当前可用显示器列表（缓存，未加载时先加载）。</summary>
    List<MonitorInfo> GetAvailableMonitors();

    /// <summary>重新枚举显示器。</summary>
    void LoadMonitors();
}
