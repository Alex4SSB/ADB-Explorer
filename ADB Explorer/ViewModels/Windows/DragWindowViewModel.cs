namespace ADB_Explorer.ViewModels.Windows;

public partial class DragWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double DragImageHeight { get; set; } = 96;

    public DragWindowViewModel()
    {

    }
}
