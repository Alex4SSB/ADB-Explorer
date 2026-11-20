#nullable enable

using System.Linq;

namespace ADB_Test;

/// <summary>
/// Pseudo-accented names for files tests create: every Latin letter swapped for an accented one, plus one emoji (a surrogate pair).
/// The extension (from the first dot) and a leading dot are kept, and no spaces are added, so shell commands need no extra quoting.
/// </summary>
internal static class TestNames
{
    private const string PLAIN = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private const string ACCENTED = "áƀçđéƒğĥíĵķĺḿñóƥɋŕšŧúṽŵẋýžÁɃÇĐÉƑĞĤÍĴĶĹḾÑÓƤɊŔŠŦÚṼŴẊÝŽ";

    public const string EMOJI = "😀";

    /// <summary><c>big.bin</c> → <c>ƀíğ😀.bin</c>; <c>.hidden.txt</c> → <c>.ĥíđđéñ😀.txt</c>.</summary>
    public static string Pseudo(string name)
    {
        var leadingDot = name.StartsWith('.') ? "." : "";
        var rest = name[leadingDot.Length..];
        var dot = rest.IndexOf('.');
        var stem = dot < 0 ? rest : rest[..dot];
        var extension = dot < 0 ? "" : rest[dot..];

        return leadingDot + Accent(stem) + EMOJI + extension;
    }

    /// <summary>Every segment of a relative path made pseudo-accented: <c>sub/b.txt</c> → <c>šúƀ😀/ƀ😀.txt</c>.</summary>
    public static string PseudoPath(string relativePath, char separator = '/')
        => string.Join(separator, relativePath.Split(separator).Select(Pseudo));

    /// <summary>Only the letters swapped, with no emoji: for names a device-side loop completes.</summary>
    public static string Accent(string text)
        => new([.. text.Select(c => PLAIN.IndexOf(c) is var i and >= 0 ? ACCENTED[i] : c)]);
}
