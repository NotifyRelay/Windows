using Microsoft.UI.Dispatching;

namespace NotifyRelay.ViewModels;

/// <summary>
/// 手写通知基建的 ViewModel 基类：提供 UI 线程封送与 <see cref="ObservableObject"/> 的 PropertyChanged 基建。
/// </summary>
public abstract class ObservableViewModelBase : ObservableObject
{
    /// <summary>UI 线程队列；为 null 时在首次 RunOnUi 调用处惰性获取并缓存。</summary>
    protected DispatcherQueue? Dispatcher { get; set; }

    protected void RunOnUi(Action action)
    {
        var dispatcher = Dispatcher ??= DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null && !dispatcher.HasThreadAccess)
            dispatcher.TryEnqueue(() => action());
        else if (dispatcher != null)
            action();
    }
}
