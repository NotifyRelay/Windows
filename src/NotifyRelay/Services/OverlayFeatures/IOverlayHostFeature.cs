using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 宿主侧叠加层功能契约：由主程序实现并登记到容器，
/// 由 OverlayFeatureHost 按各自开关统一绑定与启停。
/// </summary>
public interface IOverlayHostFeature
{
    /// <summary>功能名，用于日志与问题定位。</summary>
    string Name { get; }

    /// <summary>对应开关是否开启。</summary>
    bool IsEnabled { get; }

    /// <summary>把自身数据源绑定到叠加层输入契约。必须幂等：重复调用不得产生重复订阅。</summary>
    void Attach(IOverlayDataSink sink);

    /// <summary>开关开启时启动。必须幂等：重复调用不得产生重复启动或异常。</summary>
    void Start();

    /// <summary>开关关闭或模块停止时停止。必须幂等：未启动过时不得抛异常。</summary>
    void Stop();
}
