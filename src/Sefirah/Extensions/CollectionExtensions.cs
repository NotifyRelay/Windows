using System.Collections.ObjectModel;

namespace NotifyRelay.Extensions;

public static class CollectionExtensions
{
    public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source)
    {
        if (source is null) return new ObservableCollection<T>();
        return new ObservableCollection<T>(source);
    }

    public static void AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        if (collection is null || items is null) return;
        foreach (var item in items)
            collection.Add(item);
    }
}
