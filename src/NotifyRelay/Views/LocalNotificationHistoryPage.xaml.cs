using NotifyRelay.ViewModels;

namespace NotifyRelay.Views;

public sealed partial class LocalNotificationHistoryPage : Page
{
    public LocalNotificationHistoryViewModel ViewModel { get; }

    public LocalNotificationHistoryPage()
    {
        InitializeComponent();
        ViewModel = Ioc.Default.GetRequiredService<LocalNotificationHistoryViewModel>();
    }
}
