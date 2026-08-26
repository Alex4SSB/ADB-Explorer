using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

/// <summary>
/// Per-session file browser (explorer tab, tree context target, or a future extra tab).
/// Selection- and location-dependent file actions read <see cref="Data.Active"/>.
/// </summary>
public class FileList
{
    public LogicalDeviceViewModel? Device { get; set; }

    public string Path { get; set; } = "";

    public DriveViewModel? CurrentDrive { get; set; }

    public DirectoryLister? DirList { get; set; }

    public IEnumerable<FileClass> SelectedFiles { get; set; } = [];

    public IEnumerable<Package> SelectedPackages { get; set; } = [];

    public FileActionsEnable Actions { get; } = new();

    /// <summary>
    /// Drive node in the tree, or a folder under the Root (`/`) drive.
    /// Cut/delete of these items is not offered from the tree.
    /// </summary>
    public bool ForbidDestructive { get; set; }

    /// <summary>
    /// Root (`/`) drive card. Paste is not offered onto that node.
    /// Folders under it follow the covering mount's writability.
    /// </summary>
    public bool ForbidPaste { get; set; }

    /// <summary>
    /// When set, enablement uses this instead of <see cref="DirectoryLister.CurrentLocation"/>.
    /// Tree lists have no listing of the parent folder.
    /// </summary>
    public bool? CanWrite { get; set; }

    public static FileList? FromTreeNode(NavigationTreeNode node)
    {
        if (node.Device is not null)
            return null;

        var device = node.OwnerDevice;
        var drive = node.Drive;
        if (drive is null && !string.IsNullOrEmpty(node.Path))
            drive = DriveHelper.GetCurrentDrive(node.Path, device);

        var list = new FileList
        {
            Device = device,
            CurrentDrive = drive,
            ForbidDestructive = node.Drive is not null
                || drive?.Type is AbstractDrive.DriveType.Root,
            ForbidPaste = node.Drive?.Type is AbstractDrive.DriveType.Root,
        };

        if (drive?.Type is AbstractDrive.DriveType.Package)
        {
            list.Path = node.Path;
            list.SelectedPackages = [];
            list.Actions.IsExplorerVisible = true;
            list.Actions.IsAppDrive = true;
            return list;
        }

        if (drive?.Type is AbstractDrive.DriveType.Trash)
        {
            list.Path = AdbExplorerConst.RECYCLE_PATH;
            list.Actions.IsExplorerVisible = true;
            list.Actions.IsRecycleBin = true;
            return list;
        }

        var folder = FindListedFile(node.Path, device)
            ?? new FileClass(FileHelper.GetFullName(node.Path), node.Path, AbstractFile.FileType.Folder);

        list.SelectedFiles = [folder];
        list.Path = FileHelper.GetParentPath(node.Path);
        list.Actions.IsExplorerVisible = true;
        list.CanWrite = DriveHelper.GetRestrictions(node.Path, device).ReadOnly is not true;
        return list;
    }

    private static FileClass? FindListedFile(string path, LogicalDeviceViewModel? device)
    {
        if (device is not null && Data.Files.Device is { } open && open.ID != device.ID)
            return null;

        return Data.Files.DirList?.FileList?.FirstOrDefault(file =>
            NavigationTreeNode.PathsEqual(file.FullPath, path));
    }
}
