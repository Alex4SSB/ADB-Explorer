namespace ADB_Explorer.Controls;

/// <summary>
/// ic_fluent_rename_a_20_regular — document frames and caret as paths; letter from the UI language resources.
/// </summary>
public partial class RenameAIcon : ScaledPathIcon
{
    public string AlphabetLetter { get; }

    public Geometry AlphabetGlyph { get; }

    public RenameAIcon()
    {
        AlphabetLetter = Strings.Resources.S_RENAME_ICON_LETTER;
        AlphabetGlyph = CreateInkGlyph(AlphabetLetter);
        InitializeComponent();
    }

    /// <summary>
    /// Outline of <paramref name="letter"/> shifted so the ink box starts at (0,0).
    /// A Viewbox can then fit and center the visible glyph instead of the font em-box.
    /// </summary>
    private static Geometry CreateInkGlyph(string letter)
    {
        if (string.IsNullOrEmpty(letter))
            return Geometry.Empty;

        var culture = Strings.Resources.Culture ?? CultureInfo.InvariantCulture;
        var formatted = new FormattedText(
            letter,
            culture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            8,
            Brushes.Transparent,
            1);

        var geometry = formatted.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty)
            return geometry;

        if (geometry.IsFrozen)
            geometry = geometry.Clone();

        geometry.Transform = new TranslateTransform(-bounds.X, -bounds.Y);
        geometry.Freeze();
        return geometry;
    }
}
