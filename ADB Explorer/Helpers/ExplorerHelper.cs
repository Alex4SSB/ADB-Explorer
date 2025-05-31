using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using SHDocVw;
using System.Windows.Automation;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;

namespace ADB_Explorer.Helpers;

public class ExplorerHelper
{
    private static ShellFolder UserLibraries => new(KNOWNFOLDERID.FOLDERID_UsersLibraries);
    public static ShellFolder ThisPc => new(KNOWNFOLDERID.FOLDERID_ComputerFolder);
    private const string QuickAccessGUID = "::{679F85CB-0220-4080-B29B-5540CC05AAB6}";

    public static Dictionary<string, string> LibrariesItems { get; private set; } = [];
    public static Dictionary<string, string> ThisPcItems { get; set; } = [];
    public static Dictionary<string, string> QuickAccessItems { get; private set; } = [];

    public static string LibrariesTitle => UserLibraries.Name;
    public static string ThisPcTitle => ThisPc.Name;
    public static string QuickAccessTitle { get; private set; } = "Quick Access";


    /// <summary>
    /// Converts a user-friendly file path from a Windows Explorer-like format into a standard file system path.
    /// </summary>
    /// <remarks>This method handles paths that start with "This PC" or "Libraries" and attempts to convert
    /// them into their corresponding file system paths. For "This PC" paths, it extracts the drive and post-drive
    /// components. For "Libraries" paths, it maps the library name to its full path using a predefined
    /// mapping.</remarks>
    /// <param name="path">The input path to parse. This should be a string representing a file path in a format such as "This PC" or
    /// "Libraries".</param>
    /// <returns>A string representing the parsed file system path, or <see langword="null"/> if the input path cannot be parsed.</returns>
    public static string ParseTreePath(string path)
    {
        if (path.StartsWith(ThisPcTitle))
        {
            var match = Regex.Match(path, $@"{ThisPcTitle}\\.+\((?<Drive>[A-Z]:)\)(?<PostDrive>.*)");
            return match.Groups["Drive"].Success
                ? $"{match.Groups["Drive"].Value}{match.Groups["PostDrive"].Value}"
                : null;
        }
        else if (path.StartsWith(LibrariesTitle) || path.StartsWith(QuickAccessTitle))
        {
            var items = path.StartsWith(LibrariesTitle) ? LibrariesItems : QuickAccessItems;

            var subItem = path[9..].Trim('\\');
            if (subItem.Length == 0)
                return null;

            var index = subItem.IndexOf('\\');
            var originTop = index > -1 ? subItem[..index] : subItem;
            var remainder = index > -1 ? subItem[index..] : "";

            if (items.TryGetValue(originTop, out string fullPath))
                return $"{fullPath}{remainder}";
        }

        return null;
    }

    /// <summary>
    /// Retrieves a collection of active Windows Explorer windows and their associated paths.
    /// </summary>
    /// <remarks>This method enumerates all currently open Windows Explorer windows and extracts their paths. 
    /// If a window is displaying a special view (e.g., "This PC"), the path will be represented accordingly.</remarks>
    /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="ExplorerWindow"/> objects, where each object represents an active
    /// Windows Explorer window and its corresponding path. The path may be a directory path or a special identifier
    /// such as "This PC" for certain views.</returns>
    public static IEnumerable<ExplorerWindow> GetExplorerPaths()
    {
        SHDocVw.ShellWindows shellWindows = new();
        ConcurrentBag<(HANDLE, string)> results = [];
        string librariesGuid = UserLibraries.ParsingName;
        string thisPcGuid = ThisPc.ParsingName;

        Parallel.ForEach(shellWindows.Cast<InternetExplorer>(), window =>
        {
            IShellFolderViewDual2 folderView;
            try
            {
                if (window.Document is not IShellFolderViewDual2 fv)
                    return;

                folderView = fv;
            }
            catch
            {
                return;
            }

            FolderItems items;
            string itemPath = "";
            HANDLE hwnd;

            try
            {
                items = folderView.Folder.Items();
                itemPath = ((dynamic)items).Item()?.Path;
                hwnd = (HANDLE)window.HWND;
            }
            catch
            {
                return;
            }

            string path = "";

            if (librariesGuid.Equals(itemPath, StringComparison.InvariantCultureIgnoreCase))
            {
                path = LibrariesTitle;
                if (LibrariesItems.Count == 0)
                    LibrariesItems = GetFolderItems(UserLibraries).ToDictionary();
            }
            else if (thisPcGuid.Equals(itemPath, StringComparison.InvariantCultureIgnoreCase))
            {
                path = ThisPcTitle;
                if (ThisPcItems.Count == 0)
                    ThisPcItems = GetFolderItems(ThisPc).ToDictionary();
            }
            else if (itemPath == QuickAccessGUID)
            {
                QuickAccessTitle = folderView.Folder.Title;
                path = QuickAccessTitle;

                // The frequent folders in Quick Access are not static, but it should be enough for now
                if (QuickAccessItems.Count == 0)
                    QuickAccessItems = GetFolderItems(items).Distinct().ToDictionary();
            }
            else if (items.Count == 0) // For an empty folder get folder itself
                path = itemPath;
            else
            {
                // Get the first item
                try
                {
                    FolderItem firstItem = ((dynamic)items).Item(0);
                    path = FileHelper.GetParentPath(firstItem?.Path);
                }
                catch
                {
                    return;
                }
            }

            results.Add((hwnd, path));
        });

        return results.GroupBy(e => e.Item1, e => e.Item2).Select(i => new ExplorerWindow(i));
    }

    public static IEnumerable<KeyValuePair<string, string>> GetFolderItems(FolderItems items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            string path = "";

            try
            {
                FolderItem item = ((dynamic)items).Item(i);
                path = item?.Path;
                if (string.IsNullOrEmpty(path) || !item.IsFileSystem || !item.IsFolder)
                    continue;
            }
            catch
            {
                continue;
            }
            
            yield return new(FileHelper.GetFullName(path), path);
        }
    }

    /// <summary>
    /// Retrieves a collection of key-value pairs representing the name and file system path of items within the
    /// specified folder.
    /// </summary>
    /// <remarks>This method iterates through the items in the specified <paramref name="parent"/> folder and
    /// filters out  non-file system items, such as virtual collections or inaccessible items. If an item is a <see
    /// cref="ShellLibrary"/>,  only those with a valid default save folder in the file system are included.</remarks>
    /// <param name="parent">The <see cref="ShellFolder"/> representing the parent folder whose items are to be retrieved.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="KeyValuePair{TKey, TValue}"/> where the key is the item's name 
    /// and the value is its file system path. Items that are not file system objects or cannot be accessed are
    /// excluded.</returns>
    public static IEnumerable<KeyValuePair<string, string>> GetFolderItems(ShellFolder parent)
    {
        foreach (var item in parent)
        {
            string name, path;

            try
            {
                if (item is ShellLibrary shLib)
                {
                    // Virtual collections like "Camera Roll" will throw a COMException here.
                    if (!shLib.DefaultSaveFolder.IsFileSystem)
                        continue;

                    path = shLib.DefaultSaveFolder.FileSystemPath;
                    name = shLib.DefaultSaveFolder.Name;
                }
                else // ShellFolder
                {
                    // This includes empty card readers that will be filtered out later.
                    // Other items like DLNA servers are not file system items.
                    if (!item.IsFileSystem)
                        continue;

                    path = item.FileSystemPath;
                    name = item.Name;
                }
            }
            catch
            {
                continue;
            }

            yield return new(name, path);
        }
    }

    /// <summary>
    /// Get the full path of a tree item in a Windows Explorer window
    /// </summary>
    /// <param name="element"></param>
    /// <returns></returns>
    public static string GetPathFromTree(AutomationElement element)
    {
        if (element.Current.IsOffscreen && element.Current.Name == "Desktop")
            return "";

        var parent = TreeWalker.ControlViewWalker.GetParent(element);
        string parentPath = GetPathFromTree(parent);
        if (parentPath == "")
            return element.Current.Name;

        return $"{parentPath}\\{element.Current.Name}";
    }

    /// <summary>
    /// Retrieves the file system path associated with the specified <see cref="AutomationElement"/> within the context
    /// of the provided <see cref="ExplorerWindow"/>.
    /// </summary>
    /// <remarks>This method attempts to resolve the path of the given <paramref name="element"/> based on its
    /// type and its relationship to the provided <paramref name="window"/>. Special handling is applied for elements
    /// under "This PC" and "Libraries" to map their names to actual file system paths.</remarks>
    /// <param name="element">The <see cref="AutomationElement"/> representing the UI element for which the path is to be determined.</param>
    /// <param name="window">The <see cref="ExplorerWindow"/> that provides context for resolving the path.</param>
    /// <returns>A string representing the resolved file system path of the specified element, or <see langword="null"/> if the
    /// path cannot be determined or the element does not correspond to a valid path.</returns>
    public static string GetPathFromElement(AutomationElement element, ExplorerWindow window)
    {
        var listItem = new TreeWalker(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Normalize(element);
        bool listExists() => new TreeWalker(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List)).Normalize(element) is not null;

        string elementName = listItem?.Current.Name;

        if (element.Current.ControlType == ControlType.TreeItem)
        {
            string treePath = GetPathFromTree(element);
            if (treePath.StartsWith(ThisPcTitle) || treePath.StartsWith(LibrariesTitle) || treePath.StartsWith(QuickAccessTitle))
                return ParseTreePath(treePath);

            return null;
        }

        if (window.Equals(NativeMethods.InterceptClipboard.ExplorerWatcher?.DesktopWindow)
            || listItem is not null || listExists())
        {
            if (elementName is not null)
            {
                if (window.Path == ThisPcTitle)
                {
                    if (ThisPcItems.TryGetValue(elementName, out string actualPath))
                        return actualPath;
                }
                else if (window.Path == LibrariesTitle)
                {
                    if (LibrariesItems.TryGetValue(elementName, out string actualPath))
                        return actualPath;
                }
                else if (window.Path == QuickAccessTitle)
                {
                    if (QuickAccessItems.TryGetValue(elementName, out string actualPath))
                        return actualPath;
                }
            }
            return $"{window.Path}\\{elementName}";
        }

        return null;
    }

    /// <summary>
    /// Delete a file and notify Windows Explorer about the change
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static bool DeleteNotifyFile(string path)
    {
        var hPath = (nuint)Marshal.StringToHGlobalUni(path);
        bool result = false;

        try
        {
            File.Delete(path);
            SHChangeNotify(SHCNE.SHCNE_DELETE, SHCNF.SHCNF_PATHW, hPath);
            result = true;
        }
        catch
        { }
        finally
        {
            Marshal.FreeHGlobal((nint)hPath);
        }

        return result;
    }

    public static bool NotifyFileCreated(string path)
    {
        var hPath = (nuint)Marshal.StringToHGlobalUni(path);
        bool result = false;

        try
        {
            SHChangeNotify(SHCNE.SHCNE_CREATE, SHCNF.SHCNF_PATHW, hPath);
            result = true;
        }
        catch
        { }
        finally
        {
            Marshal.FreeHGlobal((nint)hPath);
        }

        return result;
    }

    public static void CheckConflictingApps(bool showMessage = true)
    {
        if (Data.Settings.AdvancedDrag)
        {
            if (ProcessHandling.GetConflictingApps() is var apps && apps.Any())
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    Data.RuntimeSettings.IsAdvancedDragEnabled = false;

                    if (showMessage)
                        DialogService.ShowMessage($"{Strings.Resources.S_CONFLICTING_APPS}\n\n{string.Join('\n', apps)}",
                                                  Strings.Resources.S_CONFLICTING_APPS_TITLE,
                                                  DialogService.DialogIcon.Exclamation,
                                                  copyToClipboard: true);
                });
            }
            else
            {
                Data.RuntimeSettings.IsAdvancedDragEnabled = true;
            }
        }
        else
        {
            Data.RuntimeSettings.IsAdvancedDragEnabled = false;
        }
    }
}
