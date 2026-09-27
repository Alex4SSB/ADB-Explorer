namespace ADB_Explorer.ViewModels;

public partial class PdfPageItem(BitmapSource image, int index) : ObservableObject
{
    public BitmapSource Image { get; } = image;

    [ObservableProperty]
    public partial string Label { get; set; } = $"{index}/…";
}
