using System.Windows.Media.Imaging;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

/// <summary>
/// Supplies the same icon-view bindings as <see cref="FileIconViewModel"/> for <see cref="Package"/> items.
/// </summary>
public partial class PackageIconViewModel : ObservableObject
{
    private static readonly BitmapSource LoadingPlaceholder = DefaultAndroidPackageIcon.GrayscaleBitmap;
    private static readonly BitmapSource MissingIconPlaceholder = DefaultAndroidPackageIcon.Bitmap;

    private readonly Package _package;

    public PackageIconViewModel(Package package)
    {
        _package = package;
    }

    public BitmapSource LargeIcon
    {
        get
        {
            // Binding re-reads this on every recycle/scroll. Only kick a load when needed.
            // Skip while force-reload / stop has blocked the queue — keep grayscale placeholder.
            if (_package.Icon is null)
            {
                if (!ApkIconService.IsLoadingStopped)
                    ApkIconService.BeginLoadForPackage(_package, ApkIconService.ApkLoadPriority.Visible);
            }
            else
            {
                ApkIconService.BeginEnsureLabelForPackage(_package, ApkIconService.ApkLoadPriority.Visible);
            }

            if (_package.Icon is not null)
                return _package.Icon;

            // Grayscale while in-flight; green Bugdroid once loading finished with no icon.
            if (_package.IconLoadCompleted)
                return MissingIconPlaceholder;

            return LoadingPlaceholder;
        }
    }

    public BitmapSource? LargeIconOverlay => null;

    public BitmapSource? VideoIconOverlay => null;

    public string? IconViewTooltip => _package.DisplayName;

    public void OnIconChanged() => OnPropertyChanged(nameof(LargeIcon));

    public void OnDisplayNameChanged() => OnPropertyChanged(nameof(IconViewTooltip));
}
