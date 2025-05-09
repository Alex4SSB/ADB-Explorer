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

        private AutomationEventHandler tabSelectionHandler;
        private AutomationElement explorerWindow;

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
            _delegate = new WinEventDelegate(WinEventProc);
            _hookId = SetWinEventHook(WindowsEvents.EVENT_SYSTEM_FOREGROUND, WindowsEvents.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _delegate, 0, 0, WinEventHookFlags.WINEVENT_OUTOFCONTEXT);

            UpdateWatchers();
        }

        private void WinEventProc(
            HANDLE hWinEventHook, WindowsEvents eventType,
            HANDLE hwnd, int idObject,
            int idChild, uint dwEventThread,
            uint dwmsEventTime)
        {
            if (eventType is not WindowsEvents.EVENT_SYSTEM_FOREGROUND)
                return;

            UnsubscribeFromTabEvents();
            if (hwnd == InterceptClipboard.MainWindowHandle)
                return;

            var rootElement = AutomationElement.FromHandle(hwnd);
            var proc = Process.GetProcessById(rootElement.Current.ProcessId);

            if (proc.ProcessName is not "explorer")
                return;

            explorerWindow = rootElement;
            string path = null;
            if (explorerWindow.Current.Name == "Program Manager")
            {
                var list = explorerWindow.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
                if (list.Current.Name == "Desktop")
                {
                    path = "Desktop";
                }
            }
            else
            {
                path = ExplorerHelper.GetPathFromWindow(explorerWindow);
            }
            if (string.IsNullOrEmpty(path))
                return;

            if (Data.RuntimeSettings.Is22H2)
                SubscribeToTabEvents();

            if (path == "This PC")
            {
                FocusedPath = "This PC";
                return;
            }

            FocusedPath = ExplorerHelper.GetActualPath(path);
        }

        private void SubscribeToTabEvents()
        {
            // Hierarchy: Window -> Pane -> Tab -> List -> TabItem

            // Find the TabControl within the Explorer window
            var tabControl = explorerWindow.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab));
            
            if (tabControl is null)
                return;

            tabSelectionHandler = (object sender, AutomationEventArgs e) =>
            {
                // Unfortunately, automation events do not provide the change itself, in this case the selected item.
                // The event args only contain the event id, and the sender is the tab control
                var path = ExplorerHelper.GetPathFromWindow(explorerWindow);
                
                if (path == "This PC")
                {
                    // This PC isn't handled by GetActualPath
                    FocusedPath = "This PC";
                }
                else if (!string.IsNullOrEmpty(path))
                    FocusedPath = ExplorerHelper.GetActualPath(path);
            };

            Automation.AddAutomationEventHandler(
                SelectionItemPattern.ElementSelectedEvent,
                tabControl,
                TreeScope.Descendants,
                tabSelectionHandler
            );
        }

        private void UnsubscribeFromTabEvents()
        {
            if (tabSelectionHandler is null)
                return;

            Automation.RemoveAutomationEventHandler(
                SelectionItemPattern.ElementSelectedEvent,
                explorerWindow,
                tabSelectionHandler);

            tabSelectionHandler = null;
            explorerWindow = null;
        }

        public void UpdateWatchers()
        {
            foreach (var watcher in Data.RuntimeSettings.Watchers)
            {
                watcher.Changed -= folderWatcher_Created;
                watcher.Created -= folderWatcher_Created;
                watcher.Dispose();
            }

            var paths = GetPathsFromHandles(GetExplorerWindowHandles());
            Data.RuntimeSettings.Watchers = [.. GetFileSystemWatchers([FocusedPath, .. paths])];
        }

        public static IEnumerable<FileSystemWatcher> GetFileSystemWatchers(IEnumerable<string> paths)
        {
            foreach (var path in paths.Distinct())
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                if (path == "This PC")
                {
                    // This PC is a virtual folder, so we need to watch all drives
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        // For example an empty SD card reader
                        if (!drive.IsReady)
                            continue;

                        yield return CreateFileSystemWatcher(drive.Name);
                    }
                }
                
                yield return CreateFileSystemWatcher(path);
            }
        }

        private static FileSystemWatcher CreateFileSystemWatcher(string path) =>
            App.Current.Dispatcher.Invoke(() =>
        {
            FileSystemWatcher watcher = new(path)
            {
                
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                
                EnableRaisingEvents = true
            };

            watcher.Created += folderWatcher_Created;
            watcher.Changed += folderWatcher_Created;
            return watcher;
        });

        private static void folderWatcher_Created(object sender, FileSystemEventArgs e)
        {
#if !DEPLOY
            if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
                File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | File {e.ChangeType} : {e.FullPath}\n");
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
            if (!string.IsNullOrEmpty(Properties.Resources.DragDropLogPath))
                File.AppendAllText(Properties.Resources.DragDropLogPath, $"{DateTime.Now} | Paste Destination: {Data.RuntimeSettings.PasteDestination.FileSystemPath}\n");
#endif
        }

        public static IEnumerable<string> GetPathsFromHandles(IEnumerable<HANDLE> handles)
        {
            foreach (var handle in handles.Distinct())
            {
                AutomationElement rootElement;
                try
                {
                    // The element will be null if the handle is invalid or 0.
                    // The try here is due to concurrent access to the handle (with the Shell itself)
                    rootElement = AutomationElement.FromHandle(handle);
                    if (rootElement is null)
                        continue;
                }
                catch
                {
                    continue;
                }
                
                var path = ExplorerHelper.GetPathFromWindow(rootElement);
                if (string.IsNullOrEmpty(path))
                    continue;

                yield return path;
            }
        }

        /// <summary>
        /// Get the window handles of all open File Explorer windows
        /// </summary>
        public static IEnumerable<HANDLE> GetExplorerWindowHandles()
        {
            // Since everything here is a __ComObject, we have to use dynamic

            var shellType = Type.GetTypeFromProgID("Shell.Application");
            dynamic shellObject = Activator.CreateInstance(shellType);

            try
            {
                var shellWindows = shellObject.Windows();
                for (int i = 0; i < shellWindows.Count; i++)
                {
                    var window = shellWindows.Item(i);
                    if (window is null)
                        continue;

                    yield return new((long)window.hwnd);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellObject);
            }
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
            UnsubscribeFromTabEvents();
            if (_hookId != IntPtr.Zero)
                UnhookWinEvent(_hookId);
        }
    }
}
