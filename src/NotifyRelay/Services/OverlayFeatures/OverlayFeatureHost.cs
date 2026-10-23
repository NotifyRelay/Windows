using NotifyRelay.Services.Overlay;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// 叠加层宿主功能编排器：启动渲染服务，并在每次启动完成后（含看门狗重启）
/// 遍历已登记功能绑定数据源、按各自开关启停；单个功能失败互不影响。
/// 新增功能只需实现 <see cref="IOverlayHostFeature"/> 并在容器登记。
/// </summary>
internal sealed class OverlayFeatureHost
{
    private readonly IOverlayHostFeature[] _features;
    private readonly IOverlayDataSink _sink;
    private readonly ILogger<OverlayFeatureHost> _logger;

    public OverlayFeatureHost(IEnumerable<IOverlayHostFeature> features, IOverlayDataSink sink,
        ILogger<OverlayFeatureHost> logger)
    {
        _features = features.ToArray();
        _sink = sink;
        _logger = logger;
    }

    /// <summary>已登记功能数量（供启动日志使用）。</summary>
    public int Count => _features.Length;

    /// <summary>启动渲染服务并按开关引导功能；每次叠加层启动都会重推数据快照。</summary>
    public void StartAll()
    {
        // 幂等：先退订再订阅，避免重复 StartAll 导致重复引导
        _sink.Started -= OnSinkStarted;
        _sink.Started += OnSinkStarted;
        _sink.Start();
    }

    /// <summary>停止功能并停止渲染服务。</summary>
    public void StopAll()
    {
        _sink.Started -= OnSinkStarted;
        Shutdown();
        _sink.Stop();
    }

    private void OnSinkStarted(object? sender, EventArgs e) => Initialize();

    /// <summary>绑定所有功能并按开关自动启动（幂等）。</summary>
    private void Initialize()
    {
        foreach (var feature in _features)
        {
            // 绑定与启动分开 try：绑定失败的功能不再启动，但不影响其他功能
            try
            {
                feature.Attach(_sink);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "叠加层功能 {Feature} 绑定失败，已跳过启动", feature.Name);
                continue;
            }

            if (!feature.IsEnabled)
            {
                _logger.LogInformation("叠加层功能 {Feature} 开关未开启，保持待机", feature.Name);
                continue;
            }

            try
            {
                feature.Start();
                _logger.LogInformation("叠加层功能 {Feature} 已按开关自动启动", feature.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "叠加层功能 {Feature} 启动失败", feature.Name);
            }
        }
    }

    /// <summary>停止所有功能（幂等）。</summary>
    private void Shutdown()
    {
        foreach (var feature in _features)
        {
            try
            {
                feature.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "叠加层功能 {Feature} 停止失败", feature.Name);
            }
        }
    }
}
