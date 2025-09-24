using System.Numerics;

namespace ADB_Explorer.Helpers;

public static class ByteHelper
{
    public static int PatternAt(Span<byte> source, ReadOnlySpan<byte> pattern, int startIndex = 0, bool evenAlign = false)
    {
        int length = source.Length;
        int patLength = pattern.Length;

        if (patLength == 0)
            return -1;

        // Preserve original empty-pattern behavior:
        // return the first index (respecting evenAlign) in [startIndex, source.Length)
        int limitExclusive = length - patLength + 1;

        for (int i = startIndex; i < limitExclusive; i++)
        {
            if (evenAlign && !int.IsEvenInteger(i))
                continue;

            int srcIndex = i < 0 ? 0 : i;

            // Avoid out-of-range slicing when pattern is longer than remaining source
            if (srcIndex <= length - patLength &&
                source.Slice(srcIndex, patLength).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }

    public static int Sum(this Span<byte> source)
    {
        int sum = 0;
        foreach (byte value in source)
        {
            sum += value;
        }

        return sum;
    }
}
