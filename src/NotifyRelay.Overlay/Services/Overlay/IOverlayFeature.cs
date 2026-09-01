namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层功能契约：由本模块定义、主项目实现并登记到 DI 容器。
/// 模块主初始化时统一遍历所有已登记功能，按各自开关自动绑定与启动，
/// 新增功能只需实现本契约并登记，无需在启动流程中追加手写代码。
/// </summary>
public interface IOverlayFeature
{
    /// <summary>功能名，用于日志与问题定位。</summary>
    string Name { get; }

    /// <summary>
    /// 对应开关是否开启。取值来源由实现方决定
    /// （叠加层自身的开关走 <see cref="IOverlaySettings"/>，主程序侧的开关走主程序设置服务）。
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>把自身数据源/渲染器绑定到叠加层服务（主初始化时调用一次）。</summary>
    void Bind(OverlayRenderService renderService);

    /// <summary>开关开启时启动。必须幂等：重复调用不得产生重复启动或异常。</summary>
    void Start();

    /// <summary>开关关闭或模块停止时停止。必须幂等：未启动过时不得抛异常。</summary>
    void Stop();
}
