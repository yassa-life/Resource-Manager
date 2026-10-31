using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ResourceManager.App;

/// <summary>Replaces a snapshot with one UI notification instead of one per row.</summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceWith(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
