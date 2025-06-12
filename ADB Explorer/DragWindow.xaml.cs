using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Windows.Automation;
using Vanara.Windows.Shell;
using static ADB_Explorer.Services.NativeMethods;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly DispatcherTimer DragTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private HANDLE dragWindowHandle;

    public DragWindow()
    {
        InitializeComponent();

        DragTimer.Tick += DragTimer_Tick;

#if !DEPLOY
        MainBorder.BorderThickness = new Thickness(1);
        MainBorder.BorderBrush = Brushes.OrangeRed;
#endif
    }

    private void DragTimer_Tick(object sender, EventArgs e)
    {
        if (Data.RuntimeSettings.DragBitmap is not null)
        {
            if (imageEmpty)
                UpdateMouse(InterceptMouse.MousePosition);

            GetPathUnderMouse();
        }
    }

    private DateTime lastUpdate;
    private bool waitingForUpdate = false;
    private bool imageEmpty = false;

    private void GetPathUnderMouse()
    {
        if (waitingForUpdate)
            return;

        if (DateTime.Now - lastUpdate < TimeSpan.FromMilliseconds(50))
        {
            waitingForUpdate = true;
            Task.Delay(50);
            waitingForUpdate = false;
        }
        lastUpdate = DateTime.Now;

        App.Current.Dispatcher.Invoke(() =>
        {
            if (Data.CopyPaste.DragFiles.Length == 0 || Data.CopyPaste.CurrentDropEffect is DragDropEffects.None)
            {
                DragTooltip.Text = "";
                return;
            }

            if (WindowUnderMouse?.Hwnd == dragWindowHandle)
                return;

            string explorerTarget = "";
            
            if (ElementUnderMouse is not null && WindowUnderMouse is not null && !MouseWithinApp)
            {
                try
                {
                    if (ElementUnderMouse.Current.ControlType == ControlType.Pane
                        && ElementUnderMouse.Current.Name == "PopupHost")
                    {
                        IsDropAllowed = true;
                        IsObstructed = true;
                        DragTooltip.Text = "Area obstructed - reopen Explorer window.";
                        return;
                    }
                    IsObstructed = false;

                    var path = ExplorerHelper.GetPathFromElement(ElementUnderMouse, WindowUnderMouse);

#if !DEPLOY
                        DebugLog.PrintLine($"Path under mouse: {path}");
#endif
                    
                    if (string.IsNullOrEmpty(path))
                    {
                        explorerTarget = "";
                        IsDropAllowed = false;
                    }
                    else
                    {
                        Data.RuntimeSettings.PathUnderMouse = new(path);
                        IsDropAllowed = Data.RuntimeSettings.PathUnderMouse.IsFolder;

                        string displayName = Data.RuntimeSettings.PathUnderMouse.GetDisplayName(ShellItemDisplayString.NormalDisplay);
                        if (displayName.Count(c => c == '\\') > 1)
                            displayName = Data.RuntimeSettings.PathUnderMouse.ParsingName;

                        explorerTarget = " to " + FileHelper.GetShortFileName(displayName, 30);
                    }
                }
                catch
                {
                    Data.RuntimeSettings.PathUnderMouse = null;
                    IsDropAllowed = false;
                }
            }
            else
            {
                IsObstructed = false;
                Data.RuntimeSettings.PathUnderMouse = null;
                if (MouseWithinApp)
                    IsDropAllowed = true;
            }

            var target = MouseWithinApp
                ? " to " + FileHelper.GetFullName(Data.CopyPaste.DropTarget)
                : explorerTarget;

            DragTooltip.Text = $" {Data.CopyPaste.DragFiles.Length} item(s){target}";
        });
    }

    private bool isObstructed = false;
    public bool IsObstructed
    {
        get => isObstructed;
        set
        {
            if (isObstructed != value)
            {
                isObstructed = value;
                OnPropertyChanged();
            }
        }
    }

    private bool isDropAllowed = false;
    public bool IsDropAllowed
    {
        get => isDropAllowed;
        set
        {
            if (isDropAllowed != value)
            {
                isDropAllowed = value;
                OnPropertyChanged();
            }
        }
    }

    private bool mouseWithinApp = true;
    public bool MouseWithinApp
    {
        get => mouseWithinApp;
        set
        {
            if (mouseWithinApp == value)
                return;

            mouseWithinApp = value;
            GetPathUnderMouse();
        }
    }

    private ExplorerWindow WindowUnderMouse = null;

    private AutomationElement elementUnderMouse = null;
    public AutomationElement ElementUnderMouse
    {
        get => elementUnderMouse;
        set
        {
            if (elementUnderMouse is not null && value is not null && Automation.Compare(elementUnderMouse, value))
                return;

            elementUnderMouse = value;
            GetPathUnderMouse();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Data.CopyPaste.PropertyChanged += (s, e) =>
        {
            if ((e.PropertyName == nameof(Data.CopyPaste.DragFiles)
                || e.PropertyName == nameof(Data.CopyPaste.DropTarget))
                && Data.RuntimeSettings.DragBitmap is not null)
            {
                GetPathUnderMouse();
            }
        };

        dragWindowHandle = new WindowInteropHelper(this).Handle;

        Services.WindowStyle.SetWindowHidden(dragWindowHandle);

#if DEBUG
        MouseWithinApp = true;
#else
        InterceptMouse.Init(MouseMessages.WM_MOUSEMOVE, UpdateMouse);
#endif

        DragTimer.Start();
    }

    private void UpdateMouse(POINT point)
    {
        if (Data.RuntimeSettings.DragBitmap is null)
            return;

        var actualPoint = MonitorInfo.MousePositionToDpi(point, dragWindowHandle);

        imageEmpty = DragImage.ActualHeight < 1;
        if (!imageEmpty)
        {
            Top = actualPoint.Y - DragImage.ActualHeight - 2;
            Left = actualPoint.X - DragImage.ActualWidth / 2;
        }

        if (Data.RuntimeSettings.IsAdvancedDragEnabled)
        {
            try
            {
                ElementUnderMouse = AutomationElement.FromPoint(actualPoint);
            }
            catch
            {
                ElementUnderMouse = null;
            }
        }

        var hwndUnderMouse = InterceptMouse.GetWindowUnderMouse();
        MouseWithinApp = hwndUnderMouse == InterceptClipboard.MainWindowHandle;

        if (WindowUnderMouse?.Hwnd == hwndUnderMouse)
            return;

        if (!MouseWithinApp)
        {
            if (hwndUnderMouse == dragWindowHandle)
                return;

            if (Data.CopyPaste.DragStatus is CopyPasteService.DragState.None)
                Data.RuntimeSettings.DragBitmap = null;

            var explorerWin = InterceptClipboard.ExplorerWatcher?.AllWindows.FirstOrDefault(win => win.Hwnd == hwndUnderMouse);
            if (explorerWin is null)
                return;

            WindowUnderMouse = explorerWin;

            var procName = WindowUnderMouse.Process?.ProcessName;

            Data.RuntimeSettings.DragWithinSlave = procName == Properties.AppGlobal.AppDisplayName;

            if (procName is not "" and not "explorer" && !Data.RuntimeSettings.DragWithinSlave)
                Data.RuntimeSettings.DragBitmap = null;
        }
        else
            Data.RuntimeSettings.DragWithinSlave = false;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
#if !DEBUG
        InterceptMouse.Close();
#endif
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.RuntimeSettings.DragBitmap = null;
    }
}

