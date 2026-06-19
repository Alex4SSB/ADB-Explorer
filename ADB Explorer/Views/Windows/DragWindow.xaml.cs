using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Windows;
using System.Windows.Documents;
using Wpf.Ui.Appearance;
using static ADB_Explorer.Services.NativeMethods;

namespace ADB_Explorer.Views.Windows;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

#if RELEASE
    private const int VERTICAL_OFFSET = 10;
#else
    private const int VERTICAL_OFFSET = 5;
#endif

    private int HORIZONTAL_OFFSET = 2;

    private readonly DispatcherTimer DragTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private HANDLE dragWindowHandle;

    /// <summary>
    /// The scaling factor of the primary monitor when the window is loaded. 
    /// Used to calculate the offset of the drag image from the mouse cursor.
    /// </summary>
    private float startingScaling;

    public DragWindowViewModel ViewModel { get; } = new();

    public DragWindow()
    {
        InitializeComponent();

        DragTimer.Tick += DragTimer_Tick;

        ApplicationThemeManager.Changed += (_, _) =>
        {
            if (Child is FrameworkElement child)
                ApplicationThemeManager.Apply(child);
        };

#if !DEPLOY
        MainBorder.BorderThickness = new Thickness(1);
        MainBorder.BorderBrush = Brushes.OrangeRed;
#endif
    }

    public void Show() => IsOpen = true;

    public void Close()
    {
#if !DEBUG
        InterceptMouse.Close();
#endif

        IsOpen = false;
    }

    private void DragTimer_Tick(object sender, EventArgs e)
    {
        if (Data.CopyPaste.DragBitmap is not null)
            GetPathUnderMouse();
    }

    private DateTime lastUpdate;
    private bool waitingForUpdate = false;
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

        App.SafeInvoke(() =>
        {
            DragTooltip.Inlines.Clear();
            if (Data.CopyPaste.DragFiles.Length == 0 || Data.CopyPaste.CurrentDropEffect is DragDropEffects.None)
            {
                return;
            }

            // Shouldn't happen. But if it does, we don't want to do anything.
            if (hwndUnderMouse == dragWindowHandle)
                return;

            string target = "";
            if (Data.CopyPaste.MouseWithinApp)
            {
                if (Data.CopyPaste.IsSelf
                    && Data.CopyPaste.DropTarget == Data.CopyPaste.DragParent
                    && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                    && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    return;
                }

                target = Data.CopyPaste.DropTargetName;
            }

            var format = "";
            string result = "";
            var count = Data.CopyPaste.DragFiles.Length;

            if (count == 1)
            {
                int sourceLength = target is null ? 30 : 45 - target.Length;
                var source = FileHelper.GetShortFileName(Data.CopyPaste.DragFiles[0], sourceLength);

                if (Data.FileActions.IsAppDrive && Data.CopyPaste.MouseWithinApp)
                {
                    result = string.Format(Strings.Resources.S_DRAG_INSTALL_SINGLE, source);
                    var apkSplit = result.Split(source);

                    DragTooltip.Inlines.Add(new Run(apkSplit[0]) { Foreground = blueBrush });
                    DragTooltip.Inlines.Add(source);
                    DragTooltip.Inlines.Add(new Run(apkSplit[1]) { Foreground = blueBrush });

                    return;
                }

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

                string[] split;

                if (!result.Contains(source))
                {
                    split = result.Split(target);

                    DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = blueBrush });
                    DragTooltip.Inlines.Add(target);

                    if (split.Length > 1)
                        DragTooltip.Inlines.Add(new Run(split[1]) { Foreground = blueBrush });
                }
                else
                {
                    split = result.Split(source);

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
            }
            else
            {
                if (Data.FileActions.IsAppDrive && Data.CopyPaste.MouseWithinApp)
                {
                    result = string.Format(Strings.Resources.S_DRAG_INSTALL_MULTIPLE, count);
                    DragTooltip.Inlines.Add(new Run(result) { Foreground = blueBrush });

                    return;
                }

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

    private HANDLE hwndUnderMouse = IntPtr.Zero;

    private void Popup_Opened(object sender, EventArgs e)
    {
        FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (Data.RuntimeSettings.IsRTL)
            HORIZONTAL_OFFSET = -2;

        Data.CopyPaste.PropertyChanged += (s, e) =>
        {
            // The mouse hook and drag timer only run while a drag is in progress.
            // A drag begins when DragBitmap is set and ends when it is cleared.
            if (e.PropertyName == nameof(Data.CopyPaste.DragBitmap))
            {
                if (Data.CopyPaste.DragBitmap is not null)
                    BeginDrag();
                else
                    EndDrag();

                return;
            }

            if ((e.PropertyName == nameof(Data.CopyPaste.DragFiles)
                || e.PropertyName == nameof(Data.CopyPaste.DropTarget)
                || e.PropertyName == nameof(Data.CopyPaste.MouseWithinApp))
                && Data.CopyPaste.DragBitmap is not null)
            {
                GetPathUnderMouse();
            }
        };

        if (Child is not null && PresentationSource.FromVisual(Child) is HwndSource hwndSource)
        {
            dragWindowHandle = hwndSource.Handle;
            Services.WindowStyle.SetWindowHidden(dragWindowHandle);
        }

        startingScaling = MonitorInfo.DpiToScalingFactor(MonitorInfo.PrimaryMonitorDpi());
    }

    /// <summary>
    /// Begins tracking a drag. The window is positioned at the cursor before becoming visible so it
    /// never appears at its previous location first, then the mouse hook is installed to follow the
    /// cursor for the duration of the drag.
    /// </summary>
    private void BeginDrag()
    {
        UpdatePosition(InterceptMouse.GetCursorPosition());

#if DEBUG
        Data.CopyPaste.MouseWithinApp = true;
#else
        InterceptMouse.Init(UpdateMouse, CancelDrag);
#endif

        DragTimer.Start();
    }

    /// <summary>
    /// Ends drag tracking and removes the mouse hook so it no longer adds latency to every mouse
    /// message once the drag is over.
    /// </summary>
    private void EndDrag()
    {
        DragTimer.Stop();

#if !DEBUG
        InterceptMouse.Close();
#endif
    }

    private void CancelDrag() => Data.CopyPaste.DragBitmap = null;

    /// <summary>
    /// Places the drag window relative to the given screen point. Image dimensions are derived from
    /// the bitmap rather than the rendered element, so the window can be positioned correctly even
    /// before its first layout pass - i.e. the instant a drag begins, or when the cursor leaves the
    /// app before the image has had a chance to render.
    /// </summary>
    private void UpdatePosition(POINT point)
    {
        var bitmap = Data.CopyPaste.DragBitmap;
        if (bitmap is null)
            return;

        ViewModel.DragImageHeight = 96 * (1 / MonitorInfo.GetScalingFromWindow(dragWindowHandle));
        var actualPoint = MonitorInfo.MousePositionToDpi(point, startingScaling);

        double imageHeight = DragImage.ActualHeight >= 1
            ? DragImage.ActualHeight
            : ViewModel.DragImageHeight;

        double imageWidth = DragImage.ActualWidth >= 1
            ? DragImage.ActualWidth
            : bitmap.PixelHeight > 0
                ? imageHeight * bitmap.PixelWidth / bitmap.PixelHeight
                : imageHeight;

        VerticalOffset = actualPoint.Y - imageHeight - VERTICAL_OFFSET;
        HorizontalOffset = actualPoint.X - imageWidth / HORIZONTAL_OFFSET;
    }

    private void UpdateMouse(POINT point)
    {
        if (Data.CopyPaste.DragBitmap is null)
            return;

        UpdatePosition(point);

        hwndUnderMouse = InterceptMouse.GetWindowUnderMouse();

        // Shouldn't happen. But if it does, we don't want to do anything.
        if (hwndUnderMouse == dragWindowHandle)
            return;

        var wasWithinApp = Data.CopyPaste.MouseWithinApp;
        Data.CopyPaste.MouseWithinApp = hwndUnderMouse == InterceptClipboard.MainWindowHandle;

        if (!Data.CopyPaste.MouseWithinApp && Data.CopyPaste.DragStatus is CopyPasteService.DragState.None)
            Data.CopyPaste.DragBitmap = null;

        if (!Data.CopyPaste.MouseWithinApp)
        {
            if (wasWithinApp)
                Data.CopyPaste.PasteState = DragDropEffects.None;
        }
        else
            Data.CopyPaste.DragWithinSlave = false;
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.CopyPaste.DragBitmap = null;
    }
}

