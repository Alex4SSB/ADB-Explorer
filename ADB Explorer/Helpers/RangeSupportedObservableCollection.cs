using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ADB_Explorer.Helpers
{
    public class RangeSupportedObservableCollection<T> : ObservableCollection<T>
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
    }
}
