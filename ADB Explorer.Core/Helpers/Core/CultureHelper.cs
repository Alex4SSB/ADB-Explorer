namespace ADB_Explorer.Helpers;

public static class CultureHelper
{
    /// <summary>
    /// Whether <paramref name="wanted"/> is <paramref name="available"/> or one of its parents (ru for ru-RU).
    /// Sibling regions don't match: zh-TW and zh-CN, or pt-BR and pt-PT, can word things differently.
    /// </summary>
    public static bool IsSameOrParent(CultureInfo wanted, CultureInfo available)
    {
        for (var culture = available; !culture.Equals(CultureInfo.InvariantCulture); culture = culture.Parent)
        {
            if (culture.Equals(wanted))
                return true;
        }

        return false;
    }
}
