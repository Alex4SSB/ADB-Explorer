using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public partial class NavigationTreeNode : ObservableObject
{
    private static readonly FileClass ShellDrive = new("Drive", "/Drive", AbstractFile.FileType.Drive);
    private static readonly FileClass EmptyTrash = new("RecycleBin", "/RecycleBin", AbstractFile.FileType.EmptyTrash);
    private static readonly FileClass FullTrash = new("RecycleBin", "/RecycleBin", AbstractFile.FileType.FullTrash);
    private static readonly FileClass ApkIcon = new("app.apk", "/app.apk", AbstractFile.FileType.File);
    private static readonly FileClass PhoneIcon = new("Phone", "/Phone", AbstractFile.FileType.Phone);

    private readonly Action<NavigationTreeNode>? _onUserSelect;
    private readonly Action<NavigationTreeNode>? _onExpanded;
    private bool _suppressSelectionCallback;

    internal static int SuppressUserSelectFromExpander;

    public DriveViewModel? Drive { get; }

    public LogicalDeviceViewModel? Device { get; }

    public LogicalDeviceViewModel? OwnerDevice { get; }

    public bool AlwaysExpandable { get; }

    public bool ChildrenLoaded { get; set; }

    public bool ChildrenLoading { get; set; }

    public string Path { get; private set; }

    public ObservableList<NavigationTreeNode> Children { get; } = [];

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial BitmapSource? Icon { get; set; }

    [ObservableProperty]
    public partial BitmapSource? IconOverlay { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool ShowSeparator { get; set; }

    [ObservableProperty]
    public partial bool CanExpand { get; set; }

    [ObservableProperty]
    public partial bool IsContextTarget { get; set; }

    [ObservableProperty]
    public partial DragDropEffects CutState { get; set; }

    public NavigationTreeNode(
        string path,
        string displayName,
        BitmapSource? icon,
        Action<NavigationTreeNode>? onUserSelect,
        DriveViewModel? drive = null,
        LogicalDeviceViewModel? device = null,
        LogicalDeviceViewModel? ownerDevice = null,
        Action<NavigationTreeNode>? onExpanded = null)
    {
        Path = path;
        DisplayName = displayName;
        Icon = icon;
        Drive = drive;
        Device = device;
        OwnerDevice = ownerDevice ?? device;
        _onUserSelect = onUserSelect;
        _onExpanded = onExpanded;
        AlwaysExpandable = drive?.Type is AbstractDrive.DriveType.Root or AbstractDrive.DriveType.Internal;

        if (AlwaysExpandable)
            CanExpand = true;
        else if (drive is not null
                 && drive.Type is not AbstractDrive.DriveType.Package
                 && drive.Type is not AbstractDrive.DriveType.Trash)
            CanExpand = true;
        else if (drive is null && device is null)
            CanExpand = true;

        if (drive is not null)
            drive.PropertyChanged += Drive_PropertyChanged;

        if (device is not null)
            device.PropertyChanged += Device_PropertyChanged;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            _onExpanded?.Invoke(this);
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (_suppressSelectionCallback)
            return;

        if (SuppressUserSelectFromExpander > 0)
        {
            if (value)
                SetSelected(false);
            return;
        }

        if (value)
            _onUserSelect?.Invoke(this);
    }

    public void SetSelected(bool selected)
    {
        _suppressSelectionCallback = true;
        IsSelected = selected;
        _suppressSelectionCallback = false;
    }

    public void UpdatePath(string path) => Path = path;

    public void Detach()
    {
        if (Drive is not null)
            Drive.PropertyChanged -= Drive_PropertyChanged;

        if (Device is not null)
            Device.PropertyChanged -= Device_PropertyChanged;

        foreach (var child in Children)
            child.Detach();
    }

    public NavigationTreeNode? FindChild(string path)
        => Children.FirstOrDefault(child => PathsEqual(child.Path, path));

    public bool IsDirectChildPath(string childPath)
    {
        var parentPath = FileHelper.GetParentPath(childPath);

        if (Drive is not null)
            return IsDriveRootPath(parentPath, Drive) || PathsEqual(parentPath, Path);

        return PathsEqual(parentPath, Path);
    }

    public static BitmapSource? DriveIcon(DriveViewModel drive)
    {
        if (drive.Type is AbstractDrive.DriveType.Package)
            return ApkIcon.Icon;

        if (drive.Type is AbstractDrive.DriveType.Trash)
        {
            var empty = drive is VirtualDriveViewModel { ItemsCount: 0 };
            return empty ? EmptyTrash.Icon : FullTrash.Icon;
        }

        return ShellDrive.Icon;
    }

    public static BitmapSource? DeviceIcon() => PhoneIcon.Icon;

    public static BitmapSource? FolderIcon(string path)
    {
        var name = ArchivePath.IsArchivePath(path, Data.DevicesObject?.Current?.ID)
            ? ArchivePath.GetBreadcrumbLabel(path, Data.DevicesObject?.Current?.ID)
            : FileHelper.GetFullName(path);

        var type = ArchivePath.TryParse(path, out _, out var internalPath, Data.DevicesObject?.Current?.ID)
            && string.IsNullOrEmpty(internalPath)
            ? AbstractFile.FileType.File
            : AbstractFile.FileType.Folder;

        return new FileClass(name, path, type).Icon;
    }

    public static string FolderDisplayName(string path)
    {
        if (ArchivePath.IsArchivePath(path, Data.DevicesObject?.Current?.ID))
            return ArchivePath.GetBreadcrumbLabel(path, Data.DevicesObject?.Current?.ID);

        return FileHelper.GetFullName(path);
    }

    public static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
            return false;

        if (left == right)
            return true;

        var a = NormalizePath(left);
        var b = NormalizePath(right);
        if (a == b)
            return true;

        var leftRelative = RelativeToInternalRoot(a);
        var rightRelative = RelativeToInternalRoot(b);
        if (leftRelative is null || rightRelative is null)
            return false;

        return leftRelative == rightRelative;
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return path;

        if (ArchivePath.IsArchivePath(path, Data.DevicesObject?.Current?.ID))
            return path;

        return path.TrimEnd('/');
    }

    public static bool IsDriveRootPath(string path, DriveViewModel drive)
    {
        if (PathsEqual(path, drive.Path))
            return true;

        if (drive.LinkTargetPath is string link && PathsEqual(path, link))
            return true;

        if (drive.Type is AbstractDrive.DriveType.Internal)
        {
            return AdbExplorerConst.DRIVE_TYPES.Any(kv =>
                kv.Value is AbstractDrive.DriveType.Internal && path == kv.Key);
        }

        return false;
    }

    private void Device_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Device is null)
            return;

        if (e.PropertyName is nameof(LogicalDeviceViewModel.Name)
            or nameof(LogicalDeviceViewModel.UseIdForName)
            or nameof(LogicalDeviceViewModel.BrandName))
        {
            DisplayName = Device.Name;
        }
    }

    private void Drive_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Drive is null)
            return;

        if (e.PropertyName is nameof(DriveViewModel.Type) or nameof(DriveViewModel.DriveIcon))
        {
            DisplayName = Drive.DisplayName;
            Icon = DriveIcon(Drive);
        }
        else if (e.PropertyName is nameof(VirtualDriveViewModel.ItemsCount)
                 && Drive.Type is AbstractDrive.DriveType.Trash)
        {
            Icon = DriveIcon(Drive);
        }
    }

    private static string? RelativeToInternalRoot(string path)
    {
        if (!AdbExplorerConst.IsInternalStoragePath(path))
            return null;

        var roots = AdbExplorerConst.DRIVE_TYPES
            .Where(kv => kv.Value is AbstractDrive.DriveType.Internal)
            .Select(kv => kv.Key)
            .OrderByDescending(key => key.Length);

        foreach (var root in roots)
        {
            if (path == root)
                return "";

            if (path.StartsWith($"{root}/", StringComparison.Ordinal))
                return path[(root.Length + 1)..];
        }

        return null;
    }
}
