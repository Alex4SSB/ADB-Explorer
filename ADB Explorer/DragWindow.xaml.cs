using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Windows.Automation;
using Vanara.Windows.Shell;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private readonly DispatcherTimer DragTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private HANDLE windowHandle;

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
            GetPathUnderMouse();
    }

    private DateTime lastUpdate;
    private bool waitingForUpdate = false;

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

            string explorerTarget = "";

            if (ElementUnderMouse is not null && !MouseWithinApp)
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

                    var path = ExplorerHelper.GetPathFromElement(ElementUnderMouse);
                    if (string.IsNullOrEmpty(path))
                    {
                        explorerTarget = "";
                        IsDropAllowed = false;
                    }
                    else
                    {
                        Data.RuntimeSettings.PathUnderMouse = new(path);
                        IsDropAllowed = Data.RuntimeSettings.PathUnderMouse.IsFolder;

                        explorerTarget = " to " + FileHelper.GetShortFileName(Data.RuntimeSettings.PathUnderMouse.GetDisplayName(ShellItemDisplayString.NormalDisplay), 30);
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

    private string processUnderMouse = "";

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

        windowHandle = new WindowInteropHelper(this).Handle;

        Services.WindowStyle.SetWindowHidden(windowHandle);

#if DEBUG
        MouseWithinApp = true;
#else
        NativeMethods.InterceptMouse.Init(NativeMethods.MouseMessages.WM_MOUSEMOVE,
            point =>
            {
                if (Data.RuntimeSettings.DragBitmap is null)
                    return;

                var actualPoint = NativeMethods.MonitorInfo.MousePositionToDpi(point, windowHandle);

                Top = actualPoint.Y - DragImage.ActualHeight - 2;
                Left = actualPoint.X - DragImage.ActualWidth / 2;

                if (Data.Settings.AdvancedDrag)
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
                
                MouseWithinApp = NativeMethods.MonitorInfo.IsPointInMainWin(point);
                if (!MouseWithinApp)
                {
                    if (Data.CopyPaste.DragStatus is CopyPasteService.DragState.None)
                        Data.RuntimeSettings.DragBitmap = null;

                    if (NativeMethods.InterceptMouse.GetPidFromPoint() is int pid
                        && Process.GetProcessById(pid) is Process proc)
                    {
                        processUnderMouse = proc.ProcessName;
                        Data.RuntimeSettings.DragWithinSlave = processUnderMouse == Properties.Resources.AppDisplayName;

                        if (processUnderMouse is not "" and not "explorer" && !Data.RuntimeSettings.DragWithinSlave)
                            Data.RuntimeSettings.DragBitmap = null;
                    }
                    else
                        processUnderMouse = "";
                }
                else
                    Data.RuntimeSettings.DragWithinSlave = false;
            });

#endif

        DragTimer.Start();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
#if !DEBUG
        NativeMethods.InterceptMouse.Close();
#endif
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.RuntimeSettings.DragBitmap = null;
    }
}

