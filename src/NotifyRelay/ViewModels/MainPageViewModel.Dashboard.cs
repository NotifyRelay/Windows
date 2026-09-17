namespace NotifyRelay.ViewModels;

public sealed partial class MainPageViewModel
{
    // 合并后的仪表盘项目集合（包含媒体块和通知）
    public ObservableCollection<object> DashboardItems { get; } = new ObservableCollection<object>();

    // 混合集合，包含所有通知（分组和单个）
    public ObservableCollection<object> MixedNotifications
    {
        get
        {
            var mixed = new ObservableCollection<object>();

            // 获取所有分组通知
            var grouped = GroupedNotifications.ToList();

            // 获取所有分组使用的应用包名
            var groupedPackageNames = new HashSet<string>(grouped.Select(g => g.AppPackage ?? "UnknownApp"));

            // 添加所有分组通知
            foreach (var group in grouped)
            {
                mixed.Add(group);
            }

            // 添加未分组的单个通知
            foreach (var notification in Notifications)
            {
                string packageName = notification.AppPackage ?? "UnknownApp";
                if (!groupedPackageNames.Contains(packageName))
                {
                    mixed.Add(notification);
                }
            }

            return mixed;
        }
    }

    private void InitializeDashboardItems()
    {
        // 初始填充
        UpdateDashboardItems();

        // 监听 GroupedNotifications 的变化
        if (GroupedNotifications is System.Collections.Specialized.INotifyCollectionChanged groupedNcc)
        {
            groupedNcc.CollectionChanged += (s, e) => UpdateDashboardItems();
        }

        // 注意：CurrentMusicMediaBlocks 是 ReadOnlyObservableCollection，我们需要监听其内部集合的变化
        // 这里简化处理：如果在 NotificationService 中 CurrentMusicMediaBlocks 的实例被替换，我们在上面的 PropertyChanged 中处理
        // 如果只是内容变化，我们也需要监听。
        // 由于 NotificationService.CurrentMusicMediaBlocks 可能已经在上面被监听了 PropertyChanged，
        // 这里我们尝试监听 CollectionChanged。
        if (CurrentMusicMediaBlocks is System.Collections.Specialized.INotifyCollectionChanged mediaNcc)
        {
            mediaNcc.CollectionChanged += (s, e) => UpdateDashboardItems();
        }
    }

    /// <summary>
    /// 更新 DashboardItems 集合
    /// 为了保证性能，这里尽量做增量更新，但为了实现简单和稳健，先采用智能重置策略
    /// </summary>
    private void UpdateDashboardItems()
    {
        // 如果是在非 UI 线程调用，可能需要 Dispatcher，但通常 ViewModel 的 PropertyChanged 会由 UI 框架处理
        // 这里假设是在 UI 线程或框架能处理 ObservableCollection 的跨线程操作（WinUI 3 通常需要 DispatcherQueue，但这里先直接操作）

        // 简单策略：清空并重新添加。为了减少闪烁，可以比较差异。
        // 但 ItemsRepeater 处理 Clear + Add 可能会导致滚动位置丢失。
        // 优化策略：
        // 1. 确保 MediaBlocks 在最前
        // 2. 确保 Notifications 在后

        // 由于 MediaBlocks 很少变动，Notifications 变动频繁，我们分别处理。

        // 现在的简单实现：完全重建。
        // TODO: 后续优化为增量更新以保持滚动位置和性能

        // 实际上，为了避免 ItemsRepeater 闪烁，我们应该尽量复用现有的集合

        var newItems = new List<object>();
        if (CurrentMusicMediaBlocks != null)
        {
            newItems.AddRange(CurrentMusicMediaBlocks);
        }
        if (GroupedNotifications != null)
        {
            newItems.AddRange(GroupedNotifications);
        }

        // 简单的 Diff 算法：如果数量差距不大，且大部分元素相同

        // 如果 DashboardItems 为空，直接添加
        if (DashboardItems.Count == 0)
        {
            foreach (var item in newItems)
            {
                DashboardItems.Add(item);
            }
            return;
        }

        // 粗暴的同步方法：
        // 1. 移除多余的
        // 2. 添加新增的
        // 3. 移动顺序不对的（这里暂不处理顺序移动，假设顺序相对稳定）

        // 为了简单起见，我们使用一个临时列表来同步
        // 注意：这种同步在大量数据下可能效率不高，但在通知列表场景下（通常几十条）是可以接受的

        DashboardItems.Clear();
        foreach (var item in newItems)
        {
            DashboardItems.Add(item);
        }
    }
}
