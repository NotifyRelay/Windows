using NotifyRelay.Data.Contracts;
using NotifyRelay.Services.Overlay;
using NotifyRelay.Worker.Services;

namespace NotifyRelay.Services.OverlayFeatures;

/// <summary>
/// DeepSeek 余额叠加层功能：把余额服务的数据桥接到叠加层，
/// 并在开关开启时启动后台轮询（开关语义：开 = 轮询 + 叠加层显示）。
/// 登记到容器后由叠加层模块主初始化自动引导，无需在启动流程中手写。
/// </summary>
public sealed class DeepSeekBalanceOverlayFeature : IOverlayFeature
{
    private readonly DeepSeekBalanceService _balanceService;
    private readonly IGeneralSettingsService _settings;
    private OverlayRenderService? _renderService;

    public DeepSeekBalanceOverlayFeature(DeepSeekBalanceService balanceService, IGeneralSettingsService settings)
    {
        _balanceService = balanceService;
        _settings = settings;
    }

    public string Name => "DeepSeek 余额叠加层";

    public bool IsEnabled => _settings.EnableDeepSeekBalanceMonitor;

    /// <summary>
    /// 把余额数据推送到叠加层，并订阅后续更新；同时推送已保存的显示配置。
    /// 幂等：看门狗重启叠加层会再次调用本方法，先退订再订阅，避免事件重复挂钩。
    /// </summary>
    public void Bind(OverlayRenderService renderService)
    {
        _renderService = renderService;
        PushConfig();
        PushCurrentBalance();

        _balanceService.BalanceUpdated -= OnBalanceUpdated;
        _balanceService.StatusChanged -= OnStatusChanged;
        _balanceService.BalanceUpdated += OnBalanceUpdated;
        _balanceService.StatusChanged += OnStatusChanged;
    }

    public void Start() => _balanceService.StartPolling();

    public void Stop() => _balanceService.StopPolling();

    private void OnBalanceUpdated(double balance) => PushCurrentBalance();

    private void OnStatusChanged() => PushCurrentBalance();

    /// <summary>把设置中的显示配置（开关/目标屏/位置/缩放）推送到渲染层。</summary>
    private void PushConfig()
    {
        _renderService?.SetDeepSeekBalanceConfig(
            _settings.EnableDeepSeekBalanceMonitor,
            _settings.DeepSeekBalanceTargetScreen,
            _settings.DeepSeekBalanceXPercent,
            _settings.DeepSeekBalanceYPercent,
            _settings.DeepSeekBalanceScale);
    }

    /// <summary>
    /// 推送当前余额快照。变化量取历史末条记录的 Change（由余额服务在写入历史时算好），
    /// 与设置页历史列表显示的口径一致。无历史时推送 null，卡片显示占位「¥--」。
    /// </summary>
    private void PushCurrentBalance()
    {
        if (_renderService == null) return;

        double? balance;
        double change;
        lock (_balanceService.BalanceHistory)
        {
            var last = _balanceService.BalanceHistory.LastOrDefault();
            balance = last?.Balance;
            change = last?.Change ?? 0;
        }

        _renderService.UpdateDeepSeekBalance(balance, change);
    }
}
