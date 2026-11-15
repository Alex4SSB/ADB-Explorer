using Microsoft.Win32;

namespace ADB_Explorer.Services;

// File Explorer shows a window opened on the mount's real path under its drive. Moved onto the sync root's
// navigation pane node instead, it shows the device selected there, with the device's icon.
public sealed partial class DeviceMount
{
    private const string SYNC_ROOT_MANAGER_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

    /// <summary>How long a folder may take to show the next part of the path, while it's being listed.</summary>
    private static readonly TimeSpan NAMESPACE_SEGMENT_WAIT = TimeSpan.FromSeconds(5);

    /// <summary>The CLSID of the navigation pane node the shell created for this sync root, new with each registration.</summary>
    private string? NamespaceClsid()
    {
        if (!_registeredWithShell)
            return null;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{SYNC_ROOT_MANAGER_KEY}\{SyncRootId}");
            return key?.GetValue("NamespaceCLSID") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves a File Explorer window on <paramref name="localPath"/> onto the same folder under the navigation pane node.
    /// Explorer only knows a new node some time after registering, and opening it before shows an error, so the
    /// window is moved only once Explorer itself resolves the node. Otherwise it stays on the real path.
    /// </summary>
    private void MoveToNamespaceNode(object window, string localPath)
    {
        if (NamespaceClsid() is not { } clsid)
            return;

        try
        {
            // Resolved through the window's own folder objects, so in Explorer's process: what it resolves, it can open
            dynamic desktop = ((dynamic)window).Document.Folder;
            while (desktop.ParentFolder is { } parent)
                desktop = parent;

            var target = desktop.ParseName($"::{clsid}");
            if (target is null || !target.IsFolder || !PathsEqual(target.GetFolder?.Self?.Path as string, LocalRoot))
                return;

            var relative = Path.GetRelativePath(LocalRoot, localPath);
            if (relative != ".")
            {
                foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
                {
                    target = WaitForItem(target.GetFolder, segment);
                    if (target is null)
                        return;
                }
            }

            ((dynamic)window).Navigate2(target);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Moving File Explorer onto the mount's node failed: {e.Message}");
#endif
        }
    }

    /// <summary>The named item of a folder, waiting while the folder is still being listed.</summary>
    private static dynamic? WaitForItem(dynamic folder, string name)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var item = folder.ParseName(name);
            if (item is not null || watch.Elapsed > NAMESPACE_SEGMENT_WAIT)
                return item;

            Thread.Sleep(100);
        }
    }
}
