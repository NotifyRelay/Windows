using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotifyRelay.Data.Models;

namespace NotifyRelay.Views;

public class NotificationItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupedNotificationTemplate { get; set; }
    public DataTemplate? SingleNotificationTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        return item switch
        {
            GroupedNotification => GroupedNotificationTemplate ?? base.SelectTemplateCore(item, container),
            Notification => SingleNotificationTemplate ?? base.SelectTemplateCore(item, container),
            _ => base.SelectTemplateCore(item, container)
        };
    }
}