using NotifyRelay.Data.Items;

namespace NotifyRelay.Views.Settings;

/// <summary>
/// 面包屑公共装配：构造项集合、赋 <see cref="BreadcrumbBar.ItemsSource"/> 并挂接点击处理。
/// 目标页判定与导航方式由各页通过 <c>navigate</c> 回调自行提供（各页层级与判定不同）。
/// 回调参数为被点击项的目标页类型；取不到项时传 <c>null</c>，由各页按原有语义处理。
/// </summary>
internal static class BreadcrumbHelper
{
    public static void Setup(BreadcrumbBar bar, Action<Type?> navigate, params (string Name, Type PageType)[] items)
    {
        var collection = new ObservableCollection<BreadcrumbBarItemModel>();
        foreach (var item in items)
        {
            collection.Add(new BreadcrumbBarItemModel(item.Name, item.PageType));
        }

        bar.ItemsSource = collection;
        bar.ItemClicked += (sender, args) =>
        {
            var source = bar.ItemsSource as ObservableCollection<BreadcrumbBarItemModel>;
            navigate(source?[args.Index]?.PageType);
        };
    }
}
