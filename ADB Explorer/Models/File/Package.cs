using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public partial class Package : ObservableObject, IBrowserItem
{
    public enum PackageType
    {
        System,
        User,
    }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Path { get; set; }

    /// <summary>Serial of the device this package was listed from (clock-hand cache, icon loads).</summary>
    public string? DeviceSerial { get; set; }

    /// <summary>Localized application label when known; otherwise the package id.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Name : Label!;

    [ObservableProperty]
    public partial string? Label { get; set; }

    partial void OnLabelChanged(string? value)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(NameFlowDirection));
        _iconViewModel?.OnDisplayNameChanged();
        NeedsLiveSortCatchUp = true;
    }

    /// <summary>
    /// Set when <see cref="Label"/> changes; cleared after a live-sort catch-up nudge.
    /// </summary>
    public bool NeedsLiveSortCatchUp { get; private set; }

    /// <summary>
    /// Re-raises <see cref="DisplayName"/> so <see cref="ListCollectionView"/> live sorting can
    /// catch up after it was paused (label updates during the pause do not reorder).
    /// </summary>
    public void NotifyDisplayNameForSort()
    {
        NeedsLiveSortCatchUp = false;
        OnPropertyChanged(nameof(DisplayName));
    }

    public FolderViewModel FolderViewModel => null;

    private PackageIconViewModel? _iconViewModel;
    public PackageIconViewModel IconViewModel => _iconViewModel ??= new PackageIconViewModel(this);

    /// <summary>Unused for packages; kept so <see cref="Views.FileIconView"/> bindings resolve.</summary>
    public bool IsIconPlaceholder => false;

    /// <summary>Unused for packages; kept so <see cref="Views.FileIconView"/> bindings resolve.</summary>
    public DragDropEffects CutState => DragDropEffects.None;

    /// <summary>Unused for packages; kept so <see cref="Views.FileIconView"/> bindings resolve.</summary>
    public bool IsLink => false;

    /// <summary>Packages are not renamed from icon view.</summary>
    public bool SupportsIconRename => false;

    public FlowDirection NameFlowDirection =>
        TextHelper.ContainsRtl(DisplayName) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    [ObservableProperty]
    public partial PackageType Type { get; set; }

    [ObservableProperty]
    public partial long? Uid { get; set; } = null;

    [ObservableProperty]
    public partial long? Version { get; set; } = null;

    [ObservableProperty]
    public partial string VersionName { get; set; }

    [ObservableProperty]
    public partial DateTime? LastUpdateTime { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial BitmapSource? Icon { get; set; }

    /// <summary>
    /// True after an icon fetch finished (success or fail). While false and <see cref="Icon"/> is null,
    /// icon view shows the grayscale Bugdroid placeholder.
    /// </summary>
    [ObservableProperty]
    public partial bool IconLoadCompleted { get; set; }

    partial void OnIconChanged(BitmapSource? value)
    {
        if (value is not null)
            IconLoadCompleted = true;
        _iconViewModel?.OnIconChanged();
    }

    partial void OnIconLoadCompletedChanged(bool value) => _iconViewModel?.OnIconChanged();

    public static Package New(string package, PackageType type)
    {
        var match = AdbRegEx.RE_PACKAGE_LISTING().Match(package);
        if (!match.Success)
            return null;

        return new Package(match.Groups["Name"].Value, type, match.Groups["Uid"].Value, match.Groups["Version"].Value, match.Groups["Path"].Value);
    }

    public Package(string name, PackageType type, string uid, string version, string path)
    {
        Name = name;
        Type = type;
        Path = path;

        if (long.TryParse(uid, out long resU))
            Uid = resU;

        if (long.TryParse(version, out long resV))
            Version = resV;
    }

    public override string ToString()
    {
        return $"{Name}\n{Type}\n{Uid}\n{Version}\n{Path}";
    }
}
