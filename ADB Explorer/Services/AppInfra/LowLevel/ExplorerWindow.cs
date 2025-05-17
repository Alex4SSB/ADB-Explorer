using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System.Windows.Automation;

namespace ADB_Explorer.Services;

public class ExplorerWindow
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
                _process = Process.GetProcessById(RootElement.Current.ProcessId);
            }

            return _process;
        }
    }

    public string[] Paths { get; }

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
                var match = AdbRegEx.RE_EXPLORER_WIN_PATH().Match(RootElement.Current.Name);
                if (!match.Success)
                    return null;

                string path = "";

                if (match.Groups["Drive"].Success)
                {
                    path = match.Groups["Drive"].Value;
                }
                else if (match.Groups["Path"].Success)
                {
                    var folderName = match.Groups["Path"].Value;

                    // We don't need the full path, just the name
                    path = FileHelper.GetFullName(folderName);
                }
                else
                    return null;

                return Paths.FirstOrDefault(p => p.EndsWith(path));
            }
        }
    }

    public bool IsDesktop() =>
        RootElement.Current.Name == "Program Manager"
        && FileList.Current.Name == "Desktop";

    public ExplorerWindow(IGrouping<HANDLE, string> paths)
    {
        Hwnd = paths.Key;
        Paths = [.. paths];
    }

    public ExplorerWindow(nint hwnd)
    {
        Hwnd = hwnd;
    }
}
