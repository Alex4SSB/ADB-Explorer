using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Windows.Documents;
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
    private readonly SolidColorBrush blueBrush = new(Colors.DodgerBlue);

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
            DragTooltip.Inlines.Clear();
            if (Data.CopyPaste.DragFiles.Length == 0 || Data.CopyPaste.CurrentDropEffect is DragDropEffects.None)
            {
                return;
            }

            if (WindowUnderMouse?.Hwnd == dragWindowHandle)
                return;

            string explorerTarget = "";

            IsObstructed = false;
            Data.RuntimeSettings.PathUnderMouse = null;
            if (MouseWithinApp)
                IsDropAllowed = true;

            string target = "";
            if (MouseWithinApp)
            {
                if (Data.CopyPaste.IsSelf
                    && Data.CopyPaste.DropTarget == Data.CopyPaste.DragParent
                    && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                    && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    return;
                }

                target = FileHelper.GetFullName(Data.CopyPaste.DropTarget);
            }
            else
            {
                target = explorerTarget;
            }

            var format = "";
            string result = "";
            var count = Data.CopyPaste.DragFiles.Length;

            if (count == 1)
            {
                int sourceLength = target is null ? 30 : 45 - target.Length;
                var source = FileHelper.GetShortFileName(Data.CopyPaste.DragFiles[0], sourceLength);

                if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Link)
                {
                    result = string.Format(Strings.Resources.S_DRAGDROP_LINK, target);
                }
                else if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Move)
                {
                    format = string.IsNullOrEmpty(target)
                        ? Strings.Resources.S_DRAGDROP_MOVE_SINGLE
                        : Strings.Resources.S_DRAGDROP_MOVE_TARGET_SINGLE;
                }
                else if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Copy)
                {
                    format = string.IsNullOrEmpty(target)
                        ? Strings.Resources.S_DRAGDROP_COPY_SINGLE
                        : Strings.Resources.S_DRAGDROP_COPY_TARGET_SINGLE;
                }

                if (result == "")
                {
                    result = string.Format(format, string.IsNullOrEmpty(target)
                        ? [source]
                        : [source, target]);
                }

                var split = result.Split(source);

                DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = blueBrush });
                DragTooltip.Inlines.Add(source);

                split = split[1].Split(target);

                DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = blueBrush });
                if (split.Length > 1)
                {
                    DragTooltip.Inlines.Add(target);
                    DragTooltip.Inlines.Add(new Run(split[1]) { Foreground = blueBrush });
                }
            }
            else
            {
                if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Move)
                {
                    format = string.IsNullOrEmpty(target)
                        ? Strings.Resources.S_DRAGDROP_MOVE
                        : Strings.Resources.S_DRAGDROP_MOVE_TARGET;
                }
                else if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Copy)
                {
                    format = string.IsNullOrEmpty(target)
                        ? Strings.Resources.S_DRAGDROP_COPY
                        : Strings.Resources.S_DRAGDROP_COPY_TARGET;
                }

                if (result == "")
                {
                    result = string.Format(format, string.IsNullOrEmpty(target)
                        ? [count]
                        : [count, target]);
                }

                var split = result.Split(target);

                DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = blueBrush });
                if (split.Length > 1)
                    DragTooltip.Inlines.Add(target);
            }
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
        InterceptMouse.Init(UpdateMouse, CancelDrag);
#endif

        DragTimer.Start();
    }

    private void CancelDrag() => Data.RuntimeSettings.DragBitmap = null;

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

        var hwndUnderMouse = InterceptMouse.GetWindowUnderMouse();

        // Shouldn't happen. But if it does, we don't want to do anything.
        if (hwndUnderMouse == dragWindowHandle)
            return;

        var wasWithinApp = MouseWithinApp;
        MouseWithinApp = hwndUnderMouse == InterceptClipboard.MainWindowHandle;

        if (!MouseWithinApp && Data.CopyPaste.DragStatus is CopyPasteService.DragState.None)
            Data.RuntimeSettings.DragBitmap = null;

        if (WindowUnderMouse?.Hwnd == hwndUnderMouse)
            return;

        if (!MouseWithinApp)
        {
            if (wasWithinApp)
                Data.CopyPaste.PasteState = DragDropEffects.None;
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

