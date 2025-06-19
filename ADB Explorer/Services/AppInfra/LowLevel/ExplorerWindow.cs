using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System.Windows.Automation;

namespace ADB_Explorer.Services;

public class ExplorerWindow : IComparable
{
    public HANDLE Hwnd { get; }

    private AutomationElement _rootElement = null;
    public AutomationElement RootElement
    {
        get
        {
            if (_rootElement is null)
            {
                _rootElement = AutomationElement.FromHandle(Hwnd);
            }

            return _rootElement;
        }
    }

    private AutomationElement _fileList = null;
    public AutomationElement FileList
    {
        get
        {
            if (_fileList is null)
            {
                _fileList = RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List));
            }
            return _fileList;
        }
    }

    private Process _process = null;
    public Process Process
    {
        get
        {
            if (_process is null)
            {
                try
                {
                    // Don't use automation just to get pid if it isn't ready yet
                    int processId = _fileList is null
                        ? ProcessHandling.GetProcessIdFromWindowHandle(Hwnd)
                        : RootElement.Current.ProcessId;

                    _process = Process.GetProcessById(processId);
                }
                catch (Exception e)
                {
#if !DEPLOY
                    DebugLog.PrintLine($"Process.get: {e}");
#endif
                }
            }

            return _process;
        }
    }

    public string[] Paths { get; private set; }

    /// <summary>
    /// Gets the resolved file system path based on the current state and context.
    /// </summary>
    /// <remarks>If multiple paths are available, the method attempts to match using window title.</remarks>
    public string Path
    {
        get
        {
            if (Paths.Length == 0)
                return null;
            else if (Paths.Length == 1)
                return Paths[0];
            else
            // 22H2 with more than one tab
            {
                var pathDict = Paths.ToDictionary(FileHelper.GetFullName, path => path);

                var query = pathDict.Where(item => RootElement.Current.Name.Contains(item.Key));

                return query.Any() ? query.First().Value : null;
            }
        }
    }

    public void UpdateWin10Path()
    {
        if (Data.RuntimeSettings.Is22H2 || Hwnd == NativeMethods.InterceptClipboard.ExplorerWatcher.DesktopWindow.Hwnd)
            return;

        Paths = [ExplorerHelper.GetPathFromWindow(RootElement)];
    }

    public ExplorerWindow(IGrouping<HANDLE, string> paths)
    {
        Hwnd = paths.Key;
        Paths = [.. paths];
    }

    public ExplorerWindow(nint hwnd)
    {
        Hwnd = hwnd;
    }

    public ExplorerWindow(nint hwnd, string path)
    {
        Hwnd = hwnd;
        Paths = [ path ];
    }

    public override bool Equals(object obj)
    {
        return obj is ExplorerWindow window &&
               Hwnd.Equals(window.Hwnd);
    }

    public override int GetHashCode() => Hwnd.GetHashCode();

    public int CompareTo(object obj) => Hwnd.CompareTo(obj);
}
