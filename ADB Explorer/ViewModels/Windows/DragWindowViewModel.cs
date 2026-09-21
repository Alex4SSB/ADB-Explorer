namespace ADB_Explorer.ViewModels.Windows;

public partial class DragWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double DragImageHeight { get; set; } = 96;

    [ObservableProperty]
    public partial double DragImageOpacity { get; set; } = 0.9;

    /// <summary>NaN lets the image work out its own width from its height.</summary>
    [ObservableProperty]
    public partial double DragImageWidth { get; set; } = double.NaN;

    public DragWindowViewModel()
    {

    }
}
