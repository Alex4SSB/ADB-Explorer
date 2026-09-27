namespace ADB_Explorer.ViewModels;

public sealed class PreviewSyntaxOption(string displayName, string? highlightingName, bool disableHighlighting = false, bool isHex = false)
{
    public string DisplayName { get; } = displayName;

    /// <summary>Null means Automatic (pick by file extension) unless <see cref="DisableHighlighting"/>.</summary>
    public string? HighlightingName { get; } = highlightingName;

    /// <summary>When true, no syntax highlighting is applied (None and Hex).</summary>
    public bool DisableHighlighting { get; } = disableHighlighting;

    public bool IsHex { get; } = isHex;
}
