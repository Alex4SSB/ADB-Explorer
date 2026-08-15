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
    private BitmapSource? _displayedIcon;
    private BitmapSource? _displayedFace;

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
            // A finished miss (green Bugdroid) must not enqueue another device pull.
            if (_package.Icon is null)
            {
                if (!ApkIconService.IsLoadingStopped && !_package.IconLoadCompleted)
                    ApkIconService.BeginLoadForPackage(_package, ApkIconService.ApkLoadPriority.Visible);
            }
            else
            {
                ApkIconService.BeginEnsureLabelForPackage(_package, ApkIconService.ApkLoadPriority.Visible);
            }

            if (_package.Icon is not null)
            {
                if (ReferenceEquals(_displayedFace, _package.Icon) && _displayedIcon is not null)
                    return _displayedIcon;

                _displayedFace = _package.Icon;
                _displayedIcon = ApkIconService.ForIconView(_package.Icon, _package.Name, _package.DeviceSerial);
                return _displayedIcon;
            }

            // Keep the last tile while a replacement load is in flight (calendar date refresh).
            if (_displayedIcon is not null)
                return _displayedIcon;

            // Grayscale while in-flight; green Bugdroid once loading finished with no icon.
            if (_package.IconLoadCompleted)
                return MissingIconPlaceholder;

            return LoadingPlaceholder;
        }
    }

    public BitmapSource? LargeIconOverlay => null;

    public BitmapSource? VideoIconOverlay => null;

    public string? IconViewTooltip => _package.DisplayName;

    public void OnIconChanged()
    {
        if (_package.Icon is null && !_package.IconLoadCompleted)
        {
            _displayedFace = null;
            _displayedIcon = null;
        }
        else if (_package.Icon is not null && !ReferenceEquals(_displayedFace, _package.Icon))
        {
            _displayedFace = null;
            _displayedIcon = null;
        }

        OnPropertyChanged(nameof(LargeIcon));
    }

    public void OnDisplayNameChanged() => OnPropertyChanged(nameof(IconViewTooltip));

    /// <summary>
    /// Drop the recycled overlay so the next bind can apply clock hands after a cache inspect.
    /// </summary>
    public void InvalidateDisplayedIcon()
    {
        _displayedFace = null;
        _displayedIcon = null;
        OnPropertyChanged(nameof(LargeIcon));
    }
}
