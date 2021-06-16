using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace ADB_Explorer.Core.Helpers
{
    public static class ConcurrentQueueExtensions
    {
        public static IEnumerable<T> DequeueAllExisting<T>(this ConcurrentQueue<T> queue)
        {
            T item;
            while (queue.TryDequeue(out item))
                yield return item;
        }
    }
}
