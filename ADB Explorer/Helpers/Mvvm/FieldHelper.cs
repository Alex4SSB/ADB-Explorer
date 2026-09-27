namespace ADB_Explorer.Helpers;

public static class FieldHelper
{
    /// <summary>
    /// Stores <paramref name="value"/> without raising PropertyChanged - for properties nothing observes
    /// but whose setter reacts to actual changes. Returns whether the value changed.
    /// </summary>
    public static bool TrySet<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        return true;
    }
}
