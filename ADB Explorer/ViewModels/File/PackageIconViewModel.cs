using System.Windows.Media.Imaging;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

/// <summary>
/// Supplies the same icon-view bindings as <see cref="FileIconViewModel"/> for <see cref="Package"/> items.
/// </summary>
public partial class PackageIconViewModel : ObservableObject
{
    private static readonly BitmapSource FallbackIcon = DefaultAndroidPackageIcon.Bitmap;

    private readonly Package _package;

    public PackageIconViewModel(Package package)
    {
        _package = package;
    }

    public BitmapSource LargeIcon
    {
        get
        {
            // Always ensure the label — icons may already be cached while the name is still empty.
            ApkIconService.BeginEnsureLabelForPackage(_package, priority: true);

            if (_package.Icon is null)
                ApkIconService.BeginLoadForPackage(_package, priority: true);

            return _package.Icon ?? FallbackIcon;
        }
    }

    public BitmapSource? LargeIconOverlay => null;

    public BitmapSource? VideoIconOverlay => null;

    public string? IconViewTooltip => _package.DisplayName;

    [ObservableProperty]
    public partial bool IsInEditMode { get; set; }

    public void OnIconChanged() => OnPropertyChanged(nameof(LargeIcon));

    public void OnDisplayNameChanged() => OnPropertyChanged(nameof(IconViewTooltip));
}
