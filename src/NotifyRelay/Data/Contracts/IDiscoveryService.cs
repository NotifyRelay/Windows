using System.ComponentModel;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IDiscoveryService : INotifyPropertyChanged
{
    /// <summary>
    /// 可配对设备列表。
    ///
    /// 每次内容变化时<b>整体替换为新集合实例</b>（而非逐项 Add/Remove/Replace）：
    /// <c>ItemsRepeater</c> 在布局未完成时收到多次增量集合变更会崩在原生处理器上
    /// （0x80004005），替换实例只会产生一次 ItemsSource 变更，规避该问题。
    /// 订阅方需监听 <see cref="INotifyPropertyChanged.PropertyChanged"/> 以获取新实例。
    /// </summary>
    ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; }

    /// <summary>
    /// Starts the discovery process.
    /// </summary>
    Task StartDiscoveryAsync();

    void StopDiscovery();
}
