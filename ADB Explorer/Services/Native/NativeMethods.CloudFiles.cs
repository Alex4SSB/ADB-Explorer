namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    /// <summary>Lets the calling thread see Cloud Files placeholders as they are, instead of disguised as plain files.</summary>
    public const sbyte PHCM_EXPOSE_PLACEHOLDERS = 2;

    /// <returns>The thread's previous mode, or a negative value on failure.</returns>
    [DllImport("ntdll.dll")]
    public static extern sbyte RtlSetThreadPlaceholderCompatibilityMode(sbyte mode);
}
