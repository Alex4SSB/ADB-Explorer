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
        
        public IEnumerable<ExplorerWindow> ExplorerWindows { get; private set; } = [];

        private ExplorerWindow _currentExplorerWindow = null;
        public ExplorerWindow CurrentExplorerWindow
        {
            get => _currentExplorerWindow;
            set
            {
                if (_currentExplorerWindow == value)
                    return;

                Task.Run(() => UnsubscribeFromTitleEvents(_titleChangeHandler, _currentExplorerWindow));

                _currentExplorerWindow = value;
                SubscribeToTitleEvents();
            }
        }

        private ExplorerWindow _desktopWindow = null;
        public ExplorerWindow DesktopWindow
        {
            get
            {
                if (_desktopWindow is null)
                {
                    _desktopWindow = new(GetShellWindow(),
                                         Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                }

                return _desktopWindow;
            }
        }

        public IEnumerable<ExplorerWindow> AllWindows => [.. ExplorerWindows, DesktopWindow];

        private readonly ManagementEventWatcher _driveWatcher;

        private string _focusedPath = "";
        public string FocusedPath
        {
            get => _focusedPath;
            set
            {
                if (_focusedPath == value)
                    return;

                _focusedPath = value;

                UpdateWatchers();
            }
        }

        public ExplorerWatcher()
        {
            UpdateWatchers();

            _delegate = new WinEventDelegate(WinEventProc);
            _hookId = SetWinEventHook(WindowsEvents.EVENT_SYSTEM_FOREGROUND, WindowsEvents.EVENT_SYSTEM_FOREGROUND, 
                IntPtr.Zero, _delegate, 0, 0, WinEventHookFlags.WINEVENT_OUTOFCONTEXT);

            // WMI query for removable drives (DriveType=2)
            var query = new WqlEventQuery(
                "SELECT * FROM __InstanceOperationEvent " +
                "WITHIN 2 " +
                "WHERE TargetInstance ISA 'Win32_LogicalDisk' " +
                "AND TargetInstance.DriveType = 2"
            );

            _driveWatcher = new(query);
            _driveWatcher.EventArrived += (sender, e) =>
            { 
                App.Current.Dispatcher.Invoke(() =>
                {
                    ExplorerHelper.ThisPcItems = ExplorerHelper.GetFolderItems(ExplorerHelper.ThisPc).ToDictionary();
                    UpdateWatchers();
                });
            };
            _driveWatcher.Start();
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

            if (hwnd == InterceptClipboard.MainWindowHandle)
                return;

            App.Current.Dispatcher.Invoke(() =>
            {
                ExplorerWindow window;
                if (hwnd == DesktopWindow.Hwnd)
                {
                    CurrentExplorerWindow = DesktopWindow;
                    FocusedPath = DesktopWindow.Path;
                }
                else
                {
                    window = new(hwnd);

                    if (window.Process is null)
                        return;

                    if (window.Process.ProcessName is not "explorer")
                    {
                        if (AdbExplorerConst.INCOMPATIBLE_APPS.Contains(window.Process.ProcessName))
                            ExplorerHelper.CheckConflictingApps();

                        return;
                    }

                    UpdateExplorerWindows();
                    CurrentExplorerWindow = ExplorerWindows.FirstOrDefault(w => w.Hwnd == hwnd);
                    FocusedPath = CurrentExplorerWindow?.Path;
                }
            });
        }

        private void SubscribeToTitleEvents()
        {
            // Hierarchy: Window -> Pane -> Tab -> List -> TabItem

            if (CurrentExplorerWindow?.RootElement is null)
                return;

            _titleChangeHandler = (sender, e) =>
            {
                if (CurrentExplorerWindow is null)
                    return;

                // e.NewValue does provide us the new window title, but we need to refresh the full path anyway
                UpdateExplorerWindows();
                FocusedPath = ExplorerWindows.FirstOrDefault(w => w.Hwnd == CurrentExplorerWindow.Hwnd)?.Path;
            };

            Automation.AddAutomationPropertyChangedEventHandler(
                CurrentExplorerWindow.RootElement,
                TreeScope.Element,
                _titleChangeHandler,
                AutomationElement.NameProperty
            );
        }

        private void UnsubscribeFromTitleEvents()
        {
            UnsubscribeFromTitleEvents(_titleChangeHandler, _currentExplorerWindow);

            _titleChangeHandler = null;
            CurrentExplorerWindow = null;
        }

        private static void UnsubscribeFromTitleEvents(AutomationPropertyChangedEventHandler handler, ExplorerWindow window)
        {
            if (handler is null || window?.RootElement is null)
                return;

            try
            {
                Automation.RemoveAutomationPropertyChangedEventHandler(
                window.RootElement,
                handler);
            }
            catch
            {
            }
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
            var newPaths = GetUniquePaths([FocusedPath, .. ExplorerWindows.Select(w => w.Path)]);
            
            // If the focused path is not the desktop - it is one of the explorer windows
            if (oldPaths.Order().SequenceEqual(newPaths.Order()))
                return;

            DisposeWatchers();
            if (Data.CopyPaste.IsSelfClipboard)
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

        private void UpdateExplorerWindows()
        {
            App.Current.Dispatcher.Invoke(() => ExplorerWindows = ExplorerHelper.GetExplorerPaths());
        }

        /// <summary>
        /// Returns a collection of unique file system paths after resolving special placeholders and verifying their
        /// existence.
        /// </summary>
        /// <remarks>If the input list contains the placeholder "This PC", it is replaced with the items
        /// corresponding to "This PC". Similarly, the placeholder "Libraries" is replaced with the items corresponding
        /// to "Libraries". Paths that do not exist or represent empty drives are excluded from the result.</remarks>
        /// <param name="paths">A list of file system paths, which may include special placeholders such as "This PC" or "Libraries".</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of unique file system paths that exist.  Special placeholders are expanded
        /// into their corresponding items, and non-existent paths are excluded.</returns>
        public static IEnumerable<string> GetUniquePaths(List<string> paths)
        {
            if (paths.RemoveAll(p => p == ExplorerHelper.ThisPcTitle) > 0)
                paths.AddRange(ExplorerHelper.ThisPcItems.Values);

            if (paths.RemoveAll(p => p == ExplorerHelper.LibrariesTitle) > 0)
                paths.AddRange(ExplorerHelper.LibrariesItems.Values);

            if (paths.RemoveAll(p => p == ExplorerHelper.QuickAccessTitle) > 0)
                paths.AddRange(ExplorerHelper.QuickAccessItems.Values);

            // Verify the paths exist and are not empty drives
            return paths.Distinct().Where(Directory.Exists);
        }

        private static FileSystemWatcher CreateFileSystemWatcher(string path)
        {

#if !DEPLOY
            DebugLog.PrintLine($"Watching {path}");
#endif

            return App.Current?.Dispatcher.Invoke(() =>
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
            DebugLog.PrintLine($"File {e.ChangeType}: {e.FullPath}");
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

        [DllImport("User32.dll")]
        private static extern HANDLE GetShellWindow();

        public void Dispose()
        {
            UnsubscribeFromTitleEvents();

            DisposeWatchers();

            _driveWatcher?.Stop();
            _driveWatcher?.Dispose();

            if (_hookId != IntPtr.Zero)
                UnhookWinEvent(_hookId);
        }
    }
}
