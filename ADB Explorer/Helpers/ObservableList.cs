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

    public void AddRange(IEnumerable<T> collection)
    {
        suppressOnCollectionChanged = true;

        foreach (T item in collection)
        {
            Add(item);
        }

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void RemoveAll()
    {
        suppressOnCollectionChanged = true;

        while (base.Count > 0)
        {
            RemoveAt(0);
        }

        suppressOnCollectionChanged = false;

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public T Find(Func<T, bool> predicate)
    {
        if (!this.Any() || predicate is null)
            return default;

        if (this.Where(predicate) is IEnumerable<T> result && result.Any())
            return result.First();

        return default;
    }

    public void RemoveAll(Func<T, bool> predicate)
    {
        suppressOnCollectionChanged = true;

        if (this.Where(predicate) is IEnumerable<T> result && result.Any())
        {
            foreach (T item in result.ToList())
            {
                Remove(item);
            }
        }

        suppressOnCollectionChanged = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void RemoveAll(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Remove(item);
        }
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

    public void Set(IEnumerable<T> other)
    {
        RemoveAll();
        AddRange(other);
    }
}
