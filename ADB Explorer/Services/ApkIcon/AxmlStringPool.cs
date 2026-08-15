using System.Text;

namespace ADB_Explorer.Services;

/// <summary>
/// Reads the string pool from a binary Android XML (AXML) document.
/// AlphaOmega truncates some long attribute values (e.g. pathData); the pool still has the full string.
/// </summary>
internal static class AxmlStringPool
{
    private const uint AxmlMagic = 0x00080003;
    private const ushort ResStringPoolType = 0x0001;
    private const int Utf8Flag = 1 << 8;

    /// <summary>
    /// When <paramref name="partial"/> looks truncated, returns the longest pool string that starts with it
    /// (or with a near-prefix — AlphaOmega sometimes cuts mid-number so the partial is not an exact prefix).
    /// </summary>
    /// <remarks>
    /// Sibling <c>pathData</c> values often share a short command prefix
    /// (<c>M70.6,72.7c-</c>, <c>M144.37,60.1</c>). Matching on 12–16 characters
    /// replaces a complete shorter path with a longer sibling (Phone handset
    /// body, Pango coral lobe). Only treat the pool string as an expansion when
    /// it is a true extension of <paramref name="partial"/>, allowing a short
    /// tail for a mid-token cut.
    /// </remarks>
    public static string? ExpandTruncated(byte[] axmlBytes, string? partial)
    {
        if (string.IsNullOrEmpty(partial) || axmlBytes.Length < 16)
            return partial;

        try
        {
            var strings = ReadAll(axmlBytes);
            if (strings.Count == 0)
                return partial;

            var exact = LongestStartingWith(strings, partial);
            if (exact is not null)
                return exact;

            // Mid-token cut (e.g. "22.543" vs "22.5438"): drop a short tail and
            // require a stem longer than typical sibling-path shared prefixes.
            const int maxTail = 24;
            var minStem = Math.Min(32, partial.Length);
            for (var tail = 1; tail <= maxTail; tail++)
            {
                if (partial.Length - tail < minStem)
                    break;

                var stem = partial[..^tail];
                var match = LongestStartingWith(strings, stem);
                if (match is not null)
                    return match;
            }

            return partial;
        }
        catch
        {
            return partial;
        }
    }

    private static string? LongestStartingWith(List<string> strings, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return null;

        string? best = null;
        foreach (var s in strings)
        {
            if (s.Length <= prefix.Length)
                continue;
            if (!s.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (best is null || s.Length > best.Length)
                best = s;
        }

        return best;
    }

    public static List<string> ReadAll(byte[] data)
    {
        var result = new List<string>();
        if (data.Length < 16 || BitConverter.ToUInt32(data, 0) != AxmlMagic)
            return result;

        var chunkType = BitConverter.ToUInt16(data, 8);
        if (chunkType != ResStringPoolType)
            return result;

        var headerSize = BitConverter.ToUInt16(data, 10);
        var chunkSize = BitConverter.ToInt32(data, 12);
        if (headerSize < 28 || chunkSize < headerSize || 8 + chunkSize > data.Length)
            return result;

        var stringCount = BitConverter.ToInt32(data, 16);
        var flags = BitConverter.ToInt32(data, 24);
        var stringsStart = BitConverter.ToInt32(data, 28);
        if (stringCount <= 0 || stringsStart < headerSize)
            return result;

        var utf8 = (flags & Utf8Flag) != 0;
        var poolBase = 8;
        var offsetsEnd = poolBase + headerSize + stringCount * 4;
        if (offsetsEnd > data.Length)
            return result;

        for (var i = 0; i < stringCount; i++)
        {
            var off = BitConverter.ToInt32(data, poolBase + headerSize + i * 4);
            var pos = poolBase + stringsStart + off;
            if (pos < 0 || pos >= data.Length)
                continue;

            var s = utf8 ? ReadUtf8(data, pos) : ReadUtf16(data, pos);
            if (!string.IsNullOrEmpty(s))
                result.Add(s);
        }

        return result;
    }

    private static string ReadUtf8(byte[] data, int pos)
    {
        if (pos >= data.Length)
            return "";

        ReadLength(data, ref pos, out _);
        ReadLength(data, ref pos, out var byteLen);
        if (byteLen < 0 || pos + byteLen > data.Length)
            return "";

        return Encoding.UTF8.GetString(data, pos, byteLen);
    }

    private static string ReadUtf16(byte[] data, int pos)
    {
        if (pos + 2 > data.Length)
            return "";

        var charLen = BitConverter.ToUInt16(data, pos);
        pos += 2;
        var byteLen = charLen * 2;
        if (pos + byteLen > data.Length)
            return "";

        return Encoding.Unicode.GetString(data, pos, byteLen);
    }

    private static void ReadLength(byte[] data, ref int pos, out int length)
    {
        length = data[pos++];
        if ((length & 0x80) == 0)
            return;

        if (pos >= data.Length)
        {
            length = 0;
            return;
        }

        length = ((length & 0x7F) << 8) | data[pos++];
    }
}
