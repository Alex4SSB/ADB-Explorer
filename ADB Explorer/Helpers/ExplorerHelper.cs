using ADB_Explorer.Models;
using System.Windows.Automation;

namespace ADB_Explorer.Helpers;

public class ExplorerHelper
{
    private static readonly Dictionary<string, IKnownFolder> FoldersDict = [];

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
}
