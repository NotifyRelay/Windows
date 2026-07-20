using Microsoft.UI.Xaml.Input;
using NotifyRelay.Data.Models;
using NotifyRelay.ViewModels;

namespace NotifyRelay.UserControls;

public sealed partial class NotificationsListControl : UserControl
{
    public MainPageViewModel ViewModel { get; set; } = null!;
    private double lastScrollOffset = 0;
    private bool isScrolling = false;
    private System.Threading.Timer? scrollTimer;
    private Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue;
    private const int SCROLL_DEBOUNCE_MS = 100;
    private const int SCROLL_THRESHOLD = 10;

    public NotificationsListControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // 保存UI线程的DispatcherQueue，用于定时器回调
        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // 初始化滚动定时器
        scrollTimer = new System.Threading.Timer(OnScrollTimerElapsed, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 订阅分组通知变化事件
        if (ViewModel?.NotificationService != null)
        {
            ViewModel.NotificationService.GroupedNotificationsChanged += OnGroupedNotificationsChanged;

            // 为所有现有分组订阅PropertyChanged事件
            foreach (var group in ViewModel.GroupedNotifications)
            {
                group.PropertyChanged += OnGroupPropertyChanged;
            }
        }
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 取消订阅分组通知变化事件，避免内存泄漏
        if (ViewModel?.NotificationService != null)
        {
            ViewModel.NotificationService.GroupedNotificationsChanged -= OnGroupedNotificationsChanged;
        }

        // 取消订阅所有分组的PropertyChanged事件，避免内存泄漏
        foreach (var group in ViewModel?.GroupedNotifications ?? Enumerable.Empty<Data.Models.GroupedNotification>())
        {
            group.PropertyChanged -= OnGroupPropertyChanged;
        }

        // 释放定时器资源
        scrollTimer?.Dispose();
        scrollTimer = null;
    }

    private void OnGroupedNotificationsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            // 只有当用户不在主动滚动时才处理滚动恢复
            if (!isScrolling)
            {
                // 保存当前滚动位置
                var currentOffset = NotificationsScrollViewer.VerticalOffset;
                var scrollableHeight = NotificationsScrollViewer.ScrollableHeight;

                // 检查是否接近底部 (例如在最后 20 像素内)，考虑到浮点数误差和可能的Padding影响
                bool isAtBottom = scrollableHeight > 0 && currentOffset >= (scrollableHeight - 20);

                // 延迟恢复滚动位置，确保UI已完全更新
                var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    // 如果之前在底部，则更新后也保持在底部
                    if (isAtBottom)
                    {
                        // 滚动到最新的底部
                        NotificationsScrollViewer.ChangeView(null, NotificationsScrollViewer.ScrollableHeight, null, false);
                    }
                    // 否则，只有当不在顶部时才恢复位置
                    else if (currentOffset > SCROLL_THRESHOLD)
                    {
                        // 使用ChangeView恢复滚动位置，不触发动画
                        // 确保不超过新的 ScrollableHeight
                        var targetOffset = Math.Min(currentOffset, NotificationsScrollViewer.ScrollableHeight);
                        NotificationsScrollViewer.ChangeView(null, targetOffset, null, false);
                    }
                });
            }
        }
        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            // 为新添加的分组订阅IsCollapsed属性变化事件
            if (e.NewItems != null)
            {
                foreach (var newItem in e.NewItems)
                {
                    if (newItem is Data.Models.GroupedNotification newGroup)
                    {
                        newGroup.PropertyChanged += OnGroupPropertyChanged;
                    }
                }
            }
        }
    }

    private void OnScrollViewerViewChanged(object sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e)
    {
        // 只在用户主动滚动时保存位置
        if (e.IsIntermediate)
        {
            // 用户正在滚动，保存当前位置
            isScrolling = true;
            lastScrollOffset = NotificationsScrollViewer.VerticalOffset;

            // 重置滚动定时器
            scrollTimer?.Change(SCROLL_DEBOUNCE_MS, System.Threading.Timeout.Infinite);
        }
        else
        {
            // 滚动已完成，保存最终位置
            lastScrollOffset = NotificationsScrollViewer.VerticalOffset;

            // 使用定时器来处理滚动结束后的状态
            isScrolling = true;
            scrollTimer?.Change(SCROLL_DEBOUNCE_MS, System.Threading.Timeout.Infinite);
        }
    }

    private void OnScrollTimerElapsed(object? state)
    {
        // 滚动已停止一段时间，重置标志
        dispatcherQueue?.TryEnqueue(() =>
        {
            isScrolling = false;
        });
    }

    private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Data.Models.GroupedNotification.IsCollapsed) && sender is Data.Models.GroupedNotification group)
        {
            // 当分组从折叠变为展开时，保存当前滚动位置
            if (!group.IsCollapsed)
            {
                // 保存当前滚动位置
                lastScrollOffset = NotificationsScrollViewer.VerticalOffset;

                // 延迟执行，确保UI已完成高度调整
                var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                dispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    // 恢复滚动位置，确保用户不会被意外滚动
                    NotificationsScrollViewer.ChangeView(null, lastScrollOffset, null, false);
                });
            }
        }
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
