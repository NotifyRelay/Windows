using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Sefirah.Data.Models;
using Sefirah.ViewModels;

namespace Sefirah.UserControls;
public sealed partial class NotificationsListControl : UserControl
{
    public MainPageViewModel ViewModel { get; set; }

    public NotificationsListControl()
    {
        InitializeComponent();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border &&
            border.FindName("CloseButton") is Button closeButton &&
            border.FindName("PinButton") is Button pinButton &&
            border.FindName("TimeStampTextBlock") is TextBlock timeStamp)
        {
            timeStamp.Visibility = Visibility.Collapsed;
            closeButton.Opacity = 1;
            closeButton.IsHitTestVisible = true;
            pinButton.Opacity = 1;
            pinButton.IsHitTestVisible = true;
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border &&
            border.FindName("PinIcon") is FontIcon pinIcon &&
            border.FindName("CloseButton") is Button closeButton &&
            border.FindName("PinButton") is Button pinButton &&
            border.FindName("TimeStampTextBlock") is TextBlock timeStamp)
        {
            timeStamp.Visibility = Visibility.Visible;
            closeButton.Opacity = 0;
            closeButton.IsHitTestVisible = false;
            
            // 检查是否已置顶，如果已置顶则保持显示，否则隐藏
            if (pinIcon.Tag is bool isPinned && isPinned)
            {
                pinButton.Opacity = 1;
                pinButton.IsHitTestVisible = true;
            }
            else
            {
                pinButton.Opacity = 0;
                pinButton.IsHitTestVisible = false;
            }
        }
    }

    private void ToggleGroupCollapse(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is GroupedNotification groupedNotification)
        {
            groupedNotification.ToggleCollapse();
        }
    }

    private async void OpenAppClick(object sender, RoutedEventArgs e)
    {
        Notification? notification = null;

        if (sender is MenuFlyoutItem menuItem)
        {
            Debug.WriteLine($"[调试] MenuFlyoutItem DataContext 类型={menuItem.DataContext?.GetType().Name}");
            if (menuItem.DataContext is Notification menuItemNotification)
            {
                notification = menuItemNotification;
                Debug.WriteLine($"[调试] MenuFlyoutItem 对应通知 Key={menuItemNotification.Key} AppPackage={menuItemNotification.AppPackage}");
            }
        }
        else if (sender is Button button)
        {
            Debug.WriteLine($"[调试] Button.CommandParameter 类型={button.CommandParameter?.GetType().Name} Tag 类型={button.Tag?.GetType().Name}");
            if (button.CommandParameter is Notification buttonNotification)
            {
                notification = buttonNotification;
                Debug.WriteLine($"[调试] Button.CommandParameter 对应通知 Key={buttonNotification.Key} AppPackage={buttonNotification.AppPackage}");

                // 如果 Tag 是 SourceDevice，则传递对应设备ID
                if (button.Tag is SourceDevice sd)
                {
                    Debug.WriteLine($"[调试] Button.Tag 为 SourceDevice: DeviceId={sd.DeviceId} DeviceName={sd.DeviceName}");
                    Debug.WriteLine($"[信息] 调用 ViewModel.OpenApp 通知 Key={buttonNotification.Key} deviceId={sd.DeviceId}");
                    await ViewModel.OpenApp(buttonNotification, sd.DeviceId);
                    return;
                }
            }
            else if (button.Tag is Notification tagNotification)
            {
                // 仅在极少数情况：Tag 被直接设置为 Notification
                notification = tagNotification;
                Debug.WriteLine($"[调试] Button.Tag 为 Notification, Key={tagNotification.Key}");
            }
            else if (button.Tag is SourceDevice sdOnly)
            {
                Debug.WriteLine($"[调试] Button.Tag 为 SourceDevice: DeviceId={sdOnly.DeviceId} DeviceName={sdOnly.DeviceName}");
                // 如果没有 CommandParameter，尝试从视觉树找到 Notification
                DependencyObject parent = button;
                while (parent != null)
                {
                    parent = VisualTreeHelper.GetParent(parent);
                    if (parent is FrameworkElement fe && fe.DataContext is Notification n)
                    {
                        notification = n;
                        Debug.WriteLine($"[调试] 从视觉树找到 Notification, Key={n.Key}");
                        break;
                    }
                }

                if (notification != null)
                {
                    Debug.WriteLine($"[信息] 调用 ViewModel.OpenApp 通知 Key={notification.Key} deviceId={sdOnly.DeviceId}");
                    await ViewModel.OpenApp(notification, sdOnly.DeviceId);
                    return;
                }
            }
        }

        if (notification == null)
        {
            Debug.WriteLine("[警告] 未能在 OpenAppClick 中解析到 Notification 对象，调用链中断。检查 DeviceButtonsRepeater 是否已为按钮设置 CommandParameter 或 DataContext 继承是否生效。");
            return;
        }

        Debug.WriteLine($"[信息] 调用 ViewModel.OpenApp 通知 Key={notification.Key}");
        await ViewModel.OpenApp(notification);
    }

    private void ToggleNotificationPinClick(object sender, RoutedEventArgs e)
    {
        Notification? notification = null;
        
        if (sender is MenuFlyoutItem menuItem)
        {
            notification = menuItem.DataContext as Notification;
        }
        else if (sender is Button button)
        {
            notification = button.DataContext as Notification;
        }
        
        if (notification != null)
        {
            ViewModel.ToggleNotificationPin(notification);
        }
    }

    private async void DeviceButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // 优先使用 CommandParameter（如果设置的话）
            if (button.CommandParameter is Notification paramNotification)
            {
                if (button.Tag is SourceDevice sourceDeviceParam)
                {
                    await ViewModel.OpenApp(paramNotification, sourceDeviceParam.DeviceId);
                }
                else
                {
                    await ViewModel.OpenApp(paramNotification);
                }

                return;
            }

            // 向上遍历视觉树以查找第一个其 DataContext 为 Notification 的父元素（比仅查找 Border 更稳健）
            DependencyObject parent = button;
            Notification? notification = null;
            while (parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                if (parent is FrameworkElement fe && fe.DataContext is Notification n)
                {
                    notification = n;
                    break;
                }
            }

            if (notification is null) return;

            // 获取按钮的 Tag，它包含设备ID和设备名称
            if (button.Tag is SourceDevice sourceDevice)
            {
                await ViewModel.OpenApp(notification, sourceDevice.DeviceId);
            }
        }
    }

    private void DeviceButtonsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is Button button)
        {
            //Debug.WriteLine($"[调试] DeviceButtonsRepeater 元素准备：元素类型={args.Element?.GetType().Name}");

            // 始终覆盖 CommandParameter（ItemsRepeater 可能会重用元素，旧值可能错误）

            // 优先使用 sender.Tag（我们在 XAML 将 ItemsRepeater.Tag 绑定为外层 Notification）
            if (sender.Tag is Notification notifFromTag)
            {
                button.CommandParameter = notifFromTag;
                //Debug.WriteLine($"[调试] 从 sender.Tag 设置 CommandParameter，Notification Key={notifFromTag.Key}");
                return;
            }

            // 更稳健地查找 Notification：检查父元素直到根，优先寻找 Border 或 ItemsRepeater 的 DataContext
            DependencyObject parent = button;
            while (parent != null)
            {
                parent = VisualTreeHelper.GetParent(parent);
                if (parent is FrameworkElement fe)
                {
                    if (fe.DataContext is Notification n)
                    {
                        button.CommandParameter = n;
                        //Debug.WriteLine($"[调试] 从视觉树找到父级 DataContext，设置 CommandParameter，Notification Key={n.Key}");
                        break;
                    }

                    // 如果遇到包含通知的 ItemsRepeater，并且它的 Tag/ DataContext 是 Notification，也使用它
                    if (fe is ItemsRepeater ir && ir.Tag is Notification tagNotif)
                    {
                        button.CommandParameter = tagNotif;
                        //Debug.WriteLine($"[调试] 从父 ItemsRepeater.Tag 找到 Notification，Key={tagNotif.Key}");
                        break;
                    }
                }
            }

            if (button.CommandParameter == null)
            {
                //Debug.WriteLine("[警告] 未能为按钮设置 CommandParameter（未找到对应 Notification）。");
            }
        }
    }
}
