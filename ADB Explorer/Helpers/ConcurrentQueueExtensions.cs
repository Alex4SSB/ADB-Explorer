namespace ADB_Explorer.Helpers;

public static class ConcurrentQueueExtensions
{
    public static IEnumerable<T> DequeueAllExisting<T>(this ConcurrentQueue<T> queue)
    {
        T item;
        while (queue.TryDequeue(out item))
            yield return item;
    }
}
