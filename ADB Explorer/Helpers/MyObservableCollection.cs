using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ADB_Explorer.Helpers
{
    public class MyObservableCollection<T> : ObservableCollection<T> where T : INotifyPropertyChanged
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
    }
}
