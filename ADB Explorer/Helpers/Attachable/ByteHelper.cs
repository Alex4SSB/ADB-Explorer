namespace ADB_Explorer.Helpers;

public class ByteHelper
{
    public static int PatternAt(byte[] source, byte[] pattern, int startIndex = 0, bool evenAlign = false)
    {
        for (int i = startIndex; i < source.Length; i++)
        {
            if (source.Skip(i).Take(pattern.Length).SequenceEqual(pattern)
                && (int.IsEvenInteger(i) || !evenAlign))
            {
                return i;
            }
        }

        return -1;
    }
}
