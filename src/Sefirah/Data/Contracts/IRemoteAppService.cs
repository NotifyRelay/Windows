using System.Threading.Tasks;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Data.Contracts;

public interface IRemoteAppService
{
    /// <summary>
    /// 处理应用列表响应消息
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="payload">JSON 负载</param>
    Task ProcessAppListResponseAsync(PairedDevice device, string payload);

    /// <summary>
    /// 发送应用列表请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    void SendAppListRequest(string deviceId);

    /// <summary>
    /// 发送图标请求
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="packageNames">应用包名列表</param>
    void SendIconRequest(string deviceId, List<string> packageNames);
}
