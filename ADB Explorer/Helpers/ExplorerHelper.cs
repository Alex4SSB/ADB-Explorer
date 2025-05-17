using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using SHDocVw;
using System.Windows.Automation;
using static Vanara.PInvoke.Shell32;

namespace ADB_Explorer.Helpers;

public class ExplorerHelper
{
    private static readonly Dictionary<string, Microsoft.WindowsAPICodePack.Shell.IKnownFolder> FoldersDict = [];

    /// <summary>
    /// Get the actual path of a Windows known folder or its subfolders
    /// </summary>
    public static string GetActualPath(string path)
    {
        string[] virtualLocations = [ "Libraries", "Network", "This PC" ];
        IEqualityComparer<string> comparer = StringComparer.CurrentCultureIgnoreCase;

        var name = path;

        var match = AdbRegEx.RE_WIN_ROOT_PATH().Match(name);
        if (match.Groups["Drive"].Success)
            return $"{match.Groups["Drive"].Value}{match.Groups["PostDrive"].Value}";
        else if (match.Groups["Path"].Success)
        {
            name = match.Groups["Path"].Value;
            if (name.Contains(':'))
                return name;
        }

        if (virtualLocations.Contains(name, comparer)
            || name.StartsWith("Quick access", StringComparison.CurrentCultureIgnoreCase))
            return null;

        name = name.Trim('\\');
        if (name.Length == 0)
            return null;

        var index = name.IndexOf('\\');
        var originTop = index > -1 ? name[..index] : name;
        var remainder = index > -1 ? name[index..] : "";

        if (FoldersDict.Count == 0)
        {
            foreach (var item in KnownFolders.All)
            {
                if (item.LocalizedName is null)
                    continue;

                FoldersDict.TryAdd(item.LocalizedName, item);
            }
            
            // For Windows 11
            FoldersDict.TryAdd("Libraries", KnownFolders.Libraries);
        }

        if (originTop == "Libraries")
        {
            var child = remainder.Trim('\\');
            var indexL = child.IndexOf('\\');
            var originTopL = indexL > -1 ? child[..indexL] : child;
            var remainderL = indexL > -1 ? child[indexL..] : "";

            if (FoldersDict.TryGetValue(originTopL, out var folderL))
                return $"{folderL.Path}{remainderL}";
        }

        if (FoldersDict.TryGetValue(originTop, out var folder))
            return $"{folder.Path}{remainder}";

        return $"{originTop}{remainder}";
    }

    private static int win10ToolbarIndex = 0;

    /// <summary>
    /// Get the full path of a Windows Explorer window
    /// </summary>
    /// <param name="rootElement"></param>
    /// <returns></returns>
    public static string GetPathFromWindow(AutomationElement rootElement)
    {
        // File Explorer tabs were introduced in Windows 11 22H2
        if (Data.RuntimeSettings.Is22H2)
        {
            var match = AdbRegEx.RE_EXPLORER_WIN_PATH().Match(rootElement.Current.Name);
            if (!match.Success)
                return null;

            return match.Groups["Path"].Value;
        }
        else // Windows 10 & Windows 11 before 22H2
        {
            var toolbars = rootElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ToolBar));
            
            for (int i = -1; i < toolbars.Count; i++)
            {
                if (i == win10ToolbarIndex)
                    continue;

                var toolbar = i < 0 ? toolbars[win10ToolbarIndex] : toolbars[i];
                
                var splitName = toolbar.Current.Name.Split("Address: ");
                if (splitName.Length > 1 && splitName[1].Length > 0)
                {
                    win10ToolbarIndex = i;
                    return splitName[1].Trim();
                }
            }

            return null;
        }
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

        Parallel.ForEach(shellWindows.Cast<InternetExplorer>(), window =>
        {
            if (window.Document is not IShellFolderViewDual2 folderView)
                return;

            var folder = folderView.Folder;
            dynamic items = folder.Items();
            dynamic firstItem = items.Item(0);
            string itemPath = firstItem?.Path;

            string path = itemPath[^2] == ':'
                ? "This PC"
                : FileHelper.GetParentPath(itemPath);

            results.Add(((HANDLE)window.HWND, path));
        });

        return results.GroupBy(e => e.Item1, e => e.Item2).Select(i => new ExplorerWindow(i));
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
    /// Try to get the full path of an item in a Windows Explorer window
    /// </summary>
    /// <param name="element"></param>
    /// <returns></returns>
    public static string GetPathFromElement(AutomationElement element)
    {
        var listItem = new TreeWalker(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)).Normalize(element);
        var list = new TreeWalker(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List)).Normalize(element);
        var window = new TreeWalker(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)).Normalize(element);

        string elementName = "";

        if (element.Current.ControlType == ControlType.TreeItem)
        {
            string treePath = GetPathFromTree(element);
            if (treePath.StartsWith("This PC") || treePath.StartsWith("Libraries"))
                return GetActualPath(treePath);

            return null;
        }

        if (listItem is not null)
        {
            elementName = listItem.Current.Name;
        }

        if (window is null && list is not null && list.Current.Name == "Desktop")
        {
            return $"{GetActualPath("Desktop")}\\{elementName}";
        }

        if ((listItem is not null || list is not null) && window is not null)
        {
            string currentPath = GetPathFromWindow(window);

            return GetActualPath($"{currentPath}\\{elementName}");
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
            Vanara.PInvoke.Shell32.SHChangeNotify(Vanara.PInvoke.Shell32.SHCNE.SHCNE_DELETE, Vanara.PInvoke.Shell32.SHCNF.SHCNF_PATHW, hPath);
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
            Vanara.PInvoke.Shell32.SHChangeNotify(Vanara.PInvoke.Shell32.SHCNE.SHCNE_CREATE, Vanara.PInvoke.Shell32.SHCNF.SHCNF_PATHW, hPath);
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
                        DialogService.ShowMessage(Strings.S_CONFLICTING_APPS(apps), Strings.S_CONFLICTING_APPS_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true);
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
