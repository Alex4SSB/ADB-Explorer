using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System.Windows.Automation;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time

public static partial class NativeMethods
{
    public sealed class ExplorerWatcher : IDisposable
    {
        private readonly HANDLE _hookId = IntPtr.Zero;
        private readonly WinEventDelegate _delegate;
        
        private AutomationPropertyChangedEventHandler _titleChangeHandler;
        private ExplorerWindow currentExplorerWindow;
        private IEnumerable<ExplorerWindow> explorerWindows = [];

        private static readonly string _desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        private string _path = "";
        public string FocusedPath
        {
            get => _path;
            set
            {
                if (_path == value)
                    return;

                _path = value;

                UpdateWatchers();
            }
        }

        public ExplorerWatcher()
        {
            UpdateWatchers();

            _delegate = new WinEventDelegate(WinEventProc);
            _hookId = SetWinEventHook(WindowsEvents.EVENT_SYSTEM_FOREGROUND, WindowsEvents.EVENT_SYSTEM_FOREGROUND, 
                IntPtr.Zero, _delegate, 0, 0, WinEventHookFlags.WINEVENT_OUTOFCONTEXT);
        }

        /// <summary>
        /// Handles Windows event notifications for changes in the foreground window.
        /// </summary>
        /// <remarks>This method is invoked as a callback for Windows event hooks. It processes events
        /// related to changes in the foreground window, specifically when the foreground window changes to an Explorer
        /// window or a desktop window. If the event is triggered by an incompatible application, additional checks are
        /// performed to handle potential conflicts.  The method updates the currently focused path and manages
        /// subscriptions to title change events for Explorer windows.</remarks>
        /// <param name="hWinEventHook">A handle to the event hook that triggered the callback.</param>
        /// <param name="eventType">The type of Windows event that occurred. Only <see cref="WindowsEvents.EVENT_SYSTEM_FOREGROUND"/> is
        /// processed.</param>
        private void WinEventProc(
            HANDLE hWinEventHook, WindowsEvents eventType,
            HANDLE hwnd, int idObject,
            int idChild, uint dwEventThread,
            uint dwmsEventTime)
        {
            if (eventType != WindowsEvents.EVENT_SYSTEM_FOREGROUND
                || hwnd == IntPtr.Zero)
                return;

            Task.Run(UnsubscribeFromTitleEvents);
            if (hwnd == InterceptClipboard.MainWindowHandle)
                return;

            var window = new ExplorerWindow(hwnd);
            if (window.Process.ProcessName is not "explorer")
            {
                if (AdbExplorerConst.INCOMPATIBLE_APPS.Contains(window.Process.ProcessName))
                    ExplorerHelper.CheckConflictingApps();

                return;
            }

            currentExplorerWindow = window;
            if (currentExplorerWindow.IsDesktop())
            {
                FocusedPath = _desktopPath;
            }
            else
            {
                UpdateExplorerWindows();
                FocusedPath = explorerWindows.FirstOrDefault(w => w.Hwnd == hwnd)?.Path;

                SubscribeToTitleEvents();
            }
        }

        private void SubscribeToTitleEvents()
        {
            // Hierarchy: Window -> Pane -> Tab -> List -> TabItem

            if (currentExplorerWindow.RootElement is null)
                return;

            _titleChangeHandler = (sender, e) =>
            {
                // e.NewValue does provide us the new window title, but we need to refresh the full path anyway
                if (e.Property != AutomationElement.NameProperty)
                    return;

                UpdateExplorerWindows();
                FocusedPath = explorerWindows.FirstOrDefault(w => w.Hwnd == currentExplorerWindow.Hwnd)?.Path;
            };

            Automation.AddAutomationPropertyChangedEventHandler(
                currentExplorerWindow.RootElement,
                TreeScope.Element,
                _titleChangeHandler,
                AutomationElement.NameProperty
            );
        }

        private void UnsubscribeFromTitleEvents()
        {
            if (_titleChangeHandler is null || currentExplorerWindow?.RootElement is null)
                return;

            try
            {
                Automation.RemoveAutomationPropertyChangedEventHandler(
                currentExplorerWindow.RootElement,
                _titleChangeHandler);
            }
            catch
            {
            }

            _titleChangeHandler = null;
            currentExplorerWindow = null;
        }

        /// <summary>
        /// Updates the file system watchers to monitor the current set of paths.
        /// </summary>
        /// <remarks>This method ensures that the file system watchers are synchronized with the current
        /// set of paths,  including the focused path and any paths from active explorer windows. If the set of paths
        /// has not  changed, the method exits without making any updates. Otherwise, it disposes of the existing
        /// watchers  and creates new ones for the updated paths.</remarks>
        public void UpdateWatchers()
        {
            UpdateExplorerWindows();

            var oldPaths = Data.RuntimeSettings.Watchers.Select(w => w.Path);
            var newPaths = GetUniquePaths([FocusedPath, .. explorerWindows.Select(w => w.Path)]);
            
            // If the focused path is not the desktop - it is one of the explorer windows
            if (oldPaths.Order().SequenceEqual(newPaths.Order()))
                return;

            DisposeWatchers();
            Data.RuntimeSettings.Watchers = [.. newPaths.Select(CreateFileSystemWatcher)];
        }

        private static void DisposeWatchers()
        {
            foreach (var watcher in Data.RuntimeSettings.Watchers)
            {
                watcher.Changed -= folderWatcher_Created;
                watcher.Created -= folderWatcher_Created;
                watcher.Dispose();
            }
            Data.RuntimeSettings.Watchers = [];
        }

        private void UpdateExplorerWindows() => explorerWindows = ExplorerHelper.GetExplorerPaths();

        /// <summary>
        /// Creates and returns a collection of <see cref="FileSystemWatcher"/> instances for the specified paths.
        /// </summary>
        /// <remarks>"This PC" is replaced with all available drive root paths (e.g., "C:\", "D:\") that are ready for use. 
        /// The resulting collection excludes invalid paths.</remarks>
        /// <param name="paths">A collection of file system paths for which <see cref="FileSystemWatcher"/> instances should be created.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="FileSystemWatcher"/> instances, one for each valid and
        /// distinct path.</returns>
        public static IEnumerable<string> GetUniquePaths(IEnumerable<string> paths)
        {
            var disctinct = paths.Distinct();
            if (disctinct.Except(["This PC"]) is var regular && regular.Count() < disctinct.Count())
            {
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name);
                disctinct = [.. regular, .. drives];
                disctinct = disctinct.Distinct();
            }

            return disctinct.Where(Directory.Exists);
        }

        private static FileSystemWatcher CreateFileSystemWatcher(string path)
        {

#if !DEPLOY
            DebugLog.PrintLine($"Watching {path}");
#endif

            return App.Current.Dispatcher.Invoke(() =>
            {
                FileSystemWatcher watcher = null;
                try
                {
                    watcher = new(path)
                    {

                        NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite,

                        EnableRaisingEvents = true
                    };
                }
                catch
                {
                    return null;
                }

                watcher.Created += folderWatcher_Created;
                watcher.Changed += folderWatcher_Created;
                return watcher;
            });
        }

        private static void folderWatcher_Created(object sender, FileSystemEventArgs e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"File {e.ChangeType} : {e.FullPath}");
#endif

            if (e.Name == VirtualFileDataObject.DummyFileName)
            {
                Data.RuntimeSettings.PasteDestination = ShellItem.Open(FileHelper.GetParentPath(e.FullPath));
            }
            else if (File.Exists($"{e.FullPath}\\{VirtualFileDataObject.DummyFileName}"))
            {
                Data.RuntimeSettings.PasteDestination = ShellItem.Open(e.FullPath);
            }
            else
                return;

#if !DEPLOY
            DebugLog.PrintLine($"Paste Destination: {Data.RuntimeSettings.PasteDestination.FileSystemPath}");
#endif
        }

        private delegate void WinEventDelegate(
            HANDLE hWinEventHook, WindowsEvents eventType,
            HANDLE hwnd, int idObject,
            int idChild, uint dwEventThread,
            uint dwmsEventTime);

        [DllImport("User32.dll")]
        private static extern HANDLE SetWinEventHook(
            WindowsEvents eventMin, WindowsEvents eventMax,
            HANDLE hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread,
            WinEventHookFlags dwFlags);

        [DllImport("User32.dll")]
        private static extern bool UnhookWinEvent(HANDLE hWinEventHook);

        public void Dispose()
        {
            UnsubscribeFromTitleEvents();

            DisposeWatchers();

            if (_hookId != IntPtr.Zero)
                UnhookWinEvent(_hookId);
        }
    }
}
