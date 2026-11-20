namespace ADB_Explorer.ViewModels.Windows;

public partial class DragWindowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragImageHalfHeight))]
    public partial double DragImageHeight { get; set; } = 96;

    /// <summary>The source pill's top sits here, so a pill wrapped to more lines grows downwards only.</summary>
    public double DragImageHalfHeight => DragImageHeight / 2;

    /// <summary>The source's name or item count shown on the drag image, or null for none.</summary>
    [ObservableProperty]
    public partial string? SourcePill { get; set; }

    [ObservableProperty]
    public partial double DragImageOpacity { get; set; } = 0.9;

    /// <summary>NaN lets the image work out its own width from its height.</summary>
    [ObservableProperty]
    public partial double DragImageWidth { get; set; } = double.NaN;

    /// <summary>Hidden only after a short hold, so a target that briefly reports no effect doesn't make it flicker.</summary>
    [ObservableProperty]
    public partial bool IsTooltipShown { get; set; }

    /// <summary>The tooltip's message, with its insert (the target) in place.</summary>
    [ObservableProperty]
    public partial string? TooltipText { get; set; }

    /// <summary>Where the insert is in <see cref="TooltipText"/>; it keeps the default color, the message the accent one.</summary>
    [ObservableProperty]
    public partial IEnumerable<(int Start, int Length)>? TooltipInsertRanges { get; set; }

    /// <summary>What the shown tooltip describes, which picks its glyph.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TooltipGlyph))]
    public partial DragEffect TooltipEffect { get; set; }

    public string? TooltipGlyph => TooltipEffect switch
    {
        DragEffect.Copy => "\uE710",
        DragEffect.Move => "\uE72A",
        DragEffect.Link => "\uE8AD",
        DragEffect.Install => "\uE896",
        DragEffect.NewTab or DragEffect.SplitPane => "\uE8A0",
        DragEffect.OpenInExplorer => "\uE8A7",
        _ => null,
    };

    public DragWindowViewModel()
    {

    }
}
