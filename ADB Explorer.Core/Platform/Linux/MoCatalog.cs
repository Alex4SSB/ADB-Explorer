using System.Buffers.Binary;

namespace ADB_Explorer.Platform.Linux;

/// <summary>
/// Reads GNU gettext <c>.mo</c> files directly, so lookups don't depend on libc's gettext
/// or on which locales are generated on the machine.
/// </summary>
public static class MoCatalog
{
    private const uint Magic = 0x950412de;

    private const int HeaderSize = 20;

    /// <summary>The translation of <paramref name="msgid"/> in the file, or <see langword="null"/>.</summary>
    public static string? Translate(string path, string msgid)
    {
        try
        {
            return Translate(File.ReadAllBytes(path), msgid);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? Translate(byte[] data, string msgid)
    {
        if (data.Length < HeaderSize)
            return null;

        bool bigEndian;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) == Magic)
            bigEndian = false;
        else if (BinaryPrimitives.ReadUInt32BigEndian(data) == Magic)
            bigEndian = true;
        else
            return null;

        uint Read(long offset) => bigEndian
            ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset))
            : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)offset));

        var count = Read(8);
        var originals = Read(12);
        var translations = Read(16);
        var wanted = Encoding.UTF8.GetBytes(msgid);

        for (long i = 0; i < count; i++)
        {
            if (!TryGetString(data, Read, originals + i * 8, out var original))
                return null;

            // A plural entry stores "singular\0plural"; the singular is the key
            var singular = original.IndexOf((byte)0);
            if (singular >= 0)
                original = original[..singular];

            if (!original.SequenceEqual(wanted))
                continue;

            if (!TryGetString(data, Read, translations + i * 8, out var translation))
                return null;

            var end = translation.IndexOf((byte)0);
            if (end >= 0)
                translation = translation[..end];

            return translation.IsEmpty ? null : Encoding.UTF8.GetString(translation);
        }

        return null;
    }

    private static bool TryGetString(byte[] data, Func<long, uint> read, long entry, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (entry + 8 > data.Length)
            return false;

        var length = read(entry);
        var offset = read(entry + 4);
        if ((long)offset + length > data.Length)
            return false;

        value = data.AsSpan((int)offset, (int)length);

        return true;
    }
}
