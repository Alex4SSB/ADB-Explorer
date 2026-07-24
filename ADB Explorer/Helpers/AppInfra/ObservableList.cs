using System;

namespace ADB_Explorer.Helpers;

public class ObservableList<T> : ObservableCollection<T> where T : INotifyPropertyChanged
{
    private bool suppressOnCollectionChanged = false;

    /// <summary>
    /// WPF <see cref="System.Windows.Data.CollectionView"/> requires mutations on the UI dispatcher.
    /// Marshal all structural changes so background callers (device poll, drive refresh) cannot crash.
    /// </summary>
    private static void InvokeOnUi(Action action)
    {
        var dispatcher = App.AppDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    protected override void InsertItem(int index, T item) =>
        InvokeOnUi(() => base.InsertItem(index, item));

    protected override void RemoveItem(int index) =>
        InvokeOnUi(() => base.RemoveItem(index));

    protected override void ClearItems() =>
        InvokeOnUi(() => base.ClearItems());

    protected override void SetItem(int index, T item) =>
        InvokeOnUi(() => base.SetItem(index, item));

    protected override void MoveItem(int oldIndex, int newIndex) =>
        InvokeOnUi(() => base.MoveItem(oldIndex, newIndex));

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (suppressOnCollectionChanged)
            return;

        InvokeOnUi(() => base.OnCollectionChanged(e));
    }

    /// <summary>
    /// Adds a collection of items to the end of the list.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        var itemsList = items.ToArray();
        if (itemsList.Length == 0)
            return;

        InvokeOnUi(() => AddRangeCore(itemsList));
    }

    private void AddRangeCore(T[] itemsList)
    {
        if (itemsList.Length == 1)
        {
            Add(itemsList[0]);
            return;
        }

        suppressOnCollectionChanged = true;

        foreach (T item in itemsList)
            Add(item);

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void RemoveAll()
    {
        InvokeOnUi(RemoveAllCore);
    }

    private void RemoveAllCore()
    {
        suppressOnCollectionChanged = true;

        while (Count > 0)
            RemoveAt(0);

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
    /// <returns><see langword="true"/> if at least one item was removed, otherwise <see langword="false"/></returns>
    public bool RemoveAll(Func<T, bool> predicate)
    {
        var resultList = this.Where(predicate).ToArray();
        if (resultList.Length == 0)
            return false;

        InvokeOnUi(() => RemoveAllCore(resultList));
        return true;
    }

    public void RemoveAll(IEnumerable<T> items)
    {
        var resultList = items.ToArray();
        if (resultList.Length == 0)
            return;

        InvokeOnUi(() => RemoveAllCore(resultList));
    }

    private void RemoveAllCore(T[] resultList)
    {
        if (resultList.Length == 1)
        {
            Remove(resultList[0]);
            return;
        }

        suppressOnCollectionChanged = true;

        foreach (T item in resultList)
            Remove(item);

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void ForEach(Action<T> action)
    {
        InvokeOnUi(() =>
        {
            suppressOnCollectionChanged = true;

            foreach (var item in this)
                action(item);

            suppressOnCollectionChanged = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        });
    }
}
