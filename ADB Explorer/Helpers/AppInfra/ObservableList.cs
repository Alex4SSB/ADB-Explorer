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

    public void AddRange(IEnumerable<T> items)
    {
        if (items.Count() < 2)
        {
            if (items.Any())
                Add(items.First());

            return;
        }

        suppressOnCollectionChanged = true;

        foreach (T item in items)
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

        if (this.Where(predicate) is IEnumerable<T> result && result.Any())
            return result.First();

        return default;
    }

    public bool RemoveAll(Func<T, bool> predicate)
    {
        if (this.Where(predicate) is IEnumerable<T> result && result.Any())
        {
            if (result.Count() == 1)
            {
                Remove(result.First());
                return true;
            }

            suppressOnCollectionChanged = true;

            foreach (T item in result.ToList())
            {
                Remove(item);
            }

            suppressOnCollectionChanged = false;

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

            return true;
        }

        return false;
    }

    public void RemoveAll(IEnumerable<T> items)
    {
        if (items.Count() < 2)
        {
            if (items.Any())
                Remove(items.First());

            return;
        }

        suppressOnCollectionChanged = true;

        foreach (var item in items)
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
