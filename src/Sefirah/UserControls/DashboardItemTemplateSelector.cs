using NotifyRelay.Data.Models;

namespace NotifyRelay.UserControls;

public class DashboardItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate MusicMediaBlockTemplate { get; set; }
    public DataTemplate GroupedNotificationTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        if (item is MusicMediaBlock)
        {
            return MusicMediaBlockTemplate;
        }
        else if (item is GroupedNotification)
        {
            return GroupedNotificationTemplate;
        }

        return base.SelectTemplateCore(item, container);
    }
}
