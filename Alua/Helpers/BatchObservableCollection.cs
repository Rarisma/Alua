using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Alua.Helpers;

/// <summary>
/// An ObservableCollection that supports batch operations with a single notification.
/// </summary>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    /// <summary>
    /// Replaces all items with the specified collection, firing a single Reset notification.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        _suppressNotifications = true;

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        _suppressNotifications = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Adds multiple items with a single Reset notification.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        _suppressNotifications = true;

        foreach (var item in items)
        {
            Items.Add(item);
        }

        _suppressNotifications = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Clears all items and adds new items with a single Reset notification.
    /// </summary>
    public void Reset(IEnumerable<T> items)
    {
        ReplaceAll(items);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
            base.OnCollectionChanged(e);
    }
}
