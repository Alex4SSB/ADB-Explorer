namespace ADB_Explorer.Helpers;

public static class DictionaryHelper
{
    public static Dictionary<TKey, TElement> TryToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector) where TKey : notnull =>
            TryToDictionary(source, keySelector, elementSelector, null);

    public static Dictionary<TKey, TElement> TryToDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector, IEqualityComparer<TKey>? comparer) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);

        ArgumentNullException.ThrowIfNull(keySelector);

        ArgumentNullException.ThrowIfNull(elementSelector);

        int capacity = 0;
        if (source is ICollection<TSource> collection)
        {
            capacity = collection.Count;
            if (capacity == 0)
            {
                return new Dictionary<TKey, TElement>(comparer);
            }

            if (collection is TSource[] array)
            {
                return TryToDictionary(array, keySelector, elementSelector, comparer);
            }

            if (collection is List<TSource> list)
            {
                return TryToDictionary(list, keySelector, elementSelector, comparer);
            }
        }

        Dictionary<TKey, TElement> d = new Dictionary<TKey, TElement>(capacity, comparer);
        foreach (TSource element in source)
        {
            d.TryAdd(keySelector(element), elementSelector(element));
        }

        return d;
    }
}
