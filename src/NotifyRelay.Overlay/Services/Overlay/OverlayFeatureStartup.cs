namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层功能自动引导器：模块主初始化时遍历已登记的功能，
/// 先绑定再按各自开关启动/保持待机，单个功能失败互不影响。
/// </summary>
internal sealed class OverlayFeatureStartup
{
    private readonly IOverlayFeature[] _features;
    private readonly ILogger _logger;
    private bool _initialized;

    public OverlayFeatureStartup(IEnumerable<IOverlayFeature>? features, ILogger logger)
    {
        _features = features?.ToArray() ?? [];
        _logger = logger;
    }

    /// <summary>已登记功能数量（供启动日志使用）。</summary>
    public int Count => _features.Length;

    /// <summary>绑定所有功能并按开关自动启动（幂等）。</summary>
    public void Initialize(OverlayRenderService renderService)
    {
        if (_initialized) return;
        _initialized = true;

        foreach (var feature in _features)
        {
            // 绑定与启动分开 try：绑定失败的功能不再启动，但不影响其他功能
            try
            {
                feature.Bind(renderService);
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

    /// <summary>停止所有已启动的功能（幂等）。</summary>
    public void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;

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
