using NotifyRelay.ViewModels.Settings;

namespace NotifyRelay.Views.Settings;

/// <summary>覆盖层 - Top 卡片子页（媒体卡片 / SuperIsland / Gamebar 转发）。</summary>
public sealed partial class OverlayTopCardsPage : Page
{
    public DanmakuViewModel ViewModel => (DanmakuViewModel)DataContext;

    public OverlayTopCardsPage()
    {
        InitializeComponent();
    }
}
