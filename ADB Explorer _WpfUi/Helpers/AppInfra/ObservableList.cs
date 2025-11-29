using System;

namespace ADB_Explorer.Helpers;

public class ObservableList<T> : ObservableCollection<T> where T : INotifyPropertyChanged
{
    private bool suppressOnCollectionChanged = false;

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!suppressOnCollectionChanged)
        {
            base.OnCollectionChanged(e);
        }
    }

    /// <summary>
    /// Adds a collection of items to the end of the list.
    /// </summary>
    /// <param name="items"></param>
    public void AddRange(IEnumerable<T> items)
    {
        var itemsList = items.ToArray();
        switch (itemsList.Length)
        {
            case < 1:
                return;
            case < 2:
                // When adding one item, we can skip the notification suppression mechanism
                Add(itemsList[0]);
                return;
        }

        // When adding more than one item, we suppress the notification mechanism while items are being added
        suppressOnCollectionChanged = true;

        foreach (T item in itemsList)
        {
            Add(item);
        }

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void RemoveAll()
    {
        suppressOnCollectionChanged = true;

        while (Count > 0)
        {
            RemoveAt(0);
        }

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public T Find(Func<T, bool> predicate)
    {
        if (Count == 0 || predicate is null)
            return default;

        var resultList = this.Where(predicate).ToArray();
        return resultList.Length > 0
            ? resultList[0]
            : default;
    }

    /// <summary>
    /// Removes all items that match the predicate.
    /// </summary>
    /// <param name="predicate"></param>
    /// <returns><see langword="true"/> if at least one item was removed, otherwise <see langword="false"/></returns>
    public bool RemoveAll(Func<T, bool> predicate)
    {
        var resultList = this.Where(predicate).ToArray();
        switch (resultList.Length)
        {
            case < 1:
                return false;
            case 1:
                // When removing one item, we can skip the notification suppression mechanism
                Remove(resultList[0]);
                return true;
        }

        // When removing more than one item, we suppress the notification mechanism while items are being removed
        suppressOnCollectionChanged = true;

        foreach (T item in resultList)
        {
            Remove(item);
        }

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

        return true;

    }

    public void RemoveAll(IEnumerable<T> items)
    {
        var resultList = items.ToArray();
        switch (resultList.Length)
        {
            case < 1:
                return;
            case 1:
                // When removing one item, we can skip the notification suppression mechanism
                Remove(resultList[0]);
                return;
        }

        // When removing more than one item, we suppress the notification mechanism while items are being removed
        suppressOnCollectionChanged = true;

        foreach (var item in resultList)
        {
            Remove(item);
        }

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void ForEach(Action<T> action)
    {
        suppressOnCollectionChanged = true;

        foreach (var item in this)
        {
            action(item);
        }

        suppressOnCollectionChanged = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
