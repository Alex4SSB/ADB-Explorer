using System.Windows.Documents;
using Wpf.Ui.Appearance;
using static ADB_Explorer.Services.NativeMethods;
using DROPIMAGETYPE = Vanara.PInvoke.Shell32.DROPIMAGETYPE;
using HWND = Vanara.PInvoke.HWND;
using POINT = Vanara.PInvoke.POINT;

namespace ADB_Explorer.Views.Windows;

/// <summary>
/// Interaction logic for DragWindow.xaml
/// </summary>
public partial class DragWindow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

#if RELEASE
    private const int VERTICAL_OFFSET = 10;
#else
    private const int VERTICAL_OFFSET = 5;
#endif

    private int HORIZONTAL_OFFSET = 2;

    private readonly DispatcherTimer DragTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private HWND _dragWindowHandle;

    /// <summary>
    /// The scaling factor of the primary monitor when the window is loaded. 
    /// Used to calculate the offset of the drag image from the mouse cursor.
    /// </summary>
    private float _startingScaling;

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

    /// <summary>The active tab's actions; re-read on every <see cref="Show"/>.</summary>
    public FileActionsEnable Actions => Data.FileActions;

    public void Show()
    {
        OnPropertyChanged(nameof(Actions));
        IsOpen = true;
    }

    public void Close()
    {
#if !DEBUG
        InterceptMouse.Close();
#endif

        IsOpen = false;
    }

    private void DragTimer_Tick(object? sender, EventArgs e)
    {
#if DEBUG
        // No mouse hook in debug builds to follow the window under the cursor
        InterceptMouse.GetCursorPosition();
        Data.CopyPaste.WindowUnderMouse = InterceptMouse.GetWindowUnderMouse();
#endif

        if (Data.CopyPaste.DragBitmap is not null)
            GetPathUnderMouse();
    }

    private DateTime _lastUpdate;
    private bool _waitingForUpdate = false;
    private readonly SolidColorBrush _blueBrush = new(Colors.DodgerBlue);

    private void GetPathUnderMouse()
    {
        if (_waitingForUpdate)
            return;

        if (DateTime.Now - _lastUpdate < TimeSpan.FromMilliseconds(50))
        {
            _waitingForUpdate = true;
            Task.Delay(50);
            _waitingForUpdate = false;
        }
        _lastUpdate = DateTime.Now;

        App.SafeInvoke(() =>
        {
            DragTooltip.Inlines.Clear();

            // A dragged tab header says what dropping it does, when it can be dropped.
            if (Data.CopyPaste.IsTabDrag)
            {
                if (Data.CopyPaste.CurrentDropEffect is not DragDropEffects.None && Data.CopyPaste.DragTabTooltip is { } tabTooltip)
                    DragTooltip.Inlines.Add(new Run(tabTooltip));

                return;
            }

            // A dragged device says where it opens, over File Explorer or the desktop
            if (Data.CopyPaste.DragContent is LogicalDeviceViewModel dragDevice)
            {
                var openFormat = DeviceMountDrag.TargetOf(Data.CopyPaste.WindowUnderMouse) switch
                {
                    DeviceMountDrag.DropTarget.ExplorerWindow => Strings.Resources.S_DRAG_OPEN_MOUNT_TAB,
                    DeviceMountDrag.DropTarget.Desktop => Strings.Resources.S_DRAG_OPEN_MOUNT,
                    _ => null,
                };

                // The tooltip only shows with an effect; a change of it draws the tooltip anew
                var effect = openFormat is null ? DragDropEffects.None : DragDropEffects.Link;
                if (Data.CopyPaste.CurrentDropEffect != effect)
                {
                    Data.CopyPaste.CurrentDropEffect = effect;
                    return;
                }

                if (openFormat is not null)
                    AddWithInsert(openFormat, dragDevice.Name);

                return;
            }

            if (Data.CopyPaste.DragFiles.Length == 0 || Data.CopyPaste.CurrentDropEffect is DragDropEffects.None)
            {
                return;
            }

            // Shouldn't happen. But if it does, we don't want to do anything.
            if (Data.CopyPaste.WindowUnderMouse == _dragWindowHandle)
                return;

            string target = "";
            if (!Data.CopyPaste.MouseWithinApp
                && Data.CopyPaste.ShellDropDescription is { IsValid: true } description)
            {
                // The target's own wording is shown unless its verb disagrees with our effect (e.g. it's from before a key change)
                if (description.Type is not (DROPIMAGETYPE.DROPIMAGE_COPY or DROPIMAGETYPE.DROPIMAGE_MOVE or DROPIMAGETYPE.DROPIMAGE_LINK)
                    || (DragDropEffects)description.Type == Data.CopyPaste.CurrentDropEffect)
                {
                    AddShellDescription(description);
                    return;
                }

                target = description.Insert;
            }
            else if (Data.CopyPaste.MouseWithinApp)
            {
                if (Data.CopyPaste.IsFromDevice(Data.CopyPaste.DropTargetDevice)
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

            // 7-Zip and the new Outlook name their files only at the drop, so they're shown as "??"
            long? shownCount = count;
            if (Data.CopyPaste.HasUnnamedFiles)
                shownCount = null;

            if (count == 1 && shownCount is not null)
            {
                int sourceLength = target is null ? 30 : Math.Max(15, 45 - target.Length);
                // Trash items are dragged by their indexer name; show the name they were deleted with
                var sourceName = Data.CopyPaste.DragFiles[0];
                if (Data.CopyPaste.IsFromTrash && Data.CopyPaste.CurrentFiles.FirstOrDefault() is { TrashIndex: not null } trashItem)
                    sourceName = trashItem.FullName;

                var source = FileHelper.GetShortFileName(sourceName, sourceLength);

                if (Data.FileActions.IsAppDrive && Data.CopyPaste.MouseWithinApp)
                {
                    // Self-drag on app drive is not an install target (pull-only).
                    if (Data.CopyPaste.IsSelf
                        || Data.CopyPaste.CurrentDropEffect is DragDropEffects.None)
                        return;

                    result = string.Format(Strings.Resources.S_DRAG_INSTALL_SINGLE, source);
                    var apkSplit = result.Split(source);

                    DragTooltip.Inlines.Add(new Run(apkSplit[0]) { Foreground = _blueBrush });
                    DragTooltip.Inlines.Add(source);
                    DragTooltip.Inlines.Add(new Run(apkSplit[1]) { Foreground = _blueBrush });

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

                    DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = _blueBrush });
                    DragTooltip.Inlines.Add(target);

                    if (split.Length > 1)
                        DragTooltip.Inlines.Add(new Run(split[1]) { Foreground = _blueBrush });
                }
                else
                {
                    split = result.Split(source);

                    DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = _blueBrush });
                    DragTooltip.Inlines.Add(source);

                    split = split[1].Split(target);

                    DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = _blueBrush });
                    if (split.Length > 1)
                    {
                        DragTooltip.Inlines.Add(target);
                        DragTooltip.Inlines.Add(new Run(split[1]) { Foreground = _blueBrush });
                    }
                }
            }
            else
            {
                if (Data.FileActions.IsAppDrive && Data.CopyPaste.MouseWithinApp)
                {
                    result = Strings.Plurals.InstallApks(count);
                    DragTooltip.Inlines.Add(new Run(result) { Foreground = _blueBrush });

                    return;
                }

                if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Move)
                {
                    result = string.IsNullOrEmpty(target)
                        ? Strings.Plurals.MoveItems(shownCount)
                        : Strings.Plurals.MoveItemsTo(shownCount, target);
                }
                else if (Data.CopyPaste.CurrentDropEffect is DragDropEffects.Copy)
                {
                    result = string.IsNullOrEmpty(target)
                        ? Strings.Plurals.CopyItems(shownCount)
                        : Strings.Plurals.CopyItemsTo(shownCount, target);
                }

                var split = result.Split(target);

                DragTooltip.Inlines.Add(new Run(split[0]) { Foreground = _blueBrush });
                if (split.Length > 1)
                    DragTooltip.Inlines.Add(target);
            }
        });
    }

    /// <summary>
    /// Shows a shell drop description - its %1 is replaced by the insert, which keeps the default color like our targets.
    /// </summary>
    private void AddShellDescription(DropDescription description)
    {
        var parts = description.Message.Split("%1");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0 && !string.IsNullOrEmpty(description.Insert))
                DragTooltip.Inlines.Add(description.Insert);

            if (parts[i].Length > 0)
                DragTooltip.Inlines.Add(new Run(parts[i]) { Foreground = _blueBrush });
        }
    }

    /// <summary>A tooltip whose {0} is replaced by <paramref name="insert"/>, which keeps the default color like our targets.</summary>
    private void AddWithInsert(string format, string insert)
    {
        var parts = format.Split("{0}");
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                DragTooltip.Inlines.Add(insert);

            if (parts[i].Length > 0)
                DragTooltip.Inlines.Add(new Run(parts[i]) { Foreground = _blueBrush });
        }
    }

    private void Popup_Opened(object sender, EventArgs e)
    {
        FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (Data.RuntimeSettings.IsRTL)
        {
            HORIZONTAL_OFFSET = -2;

            ContentGrid.HorizontalAlignment = HorizontalAlignment.Left;
        }

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
                || e.PropertyName == nameof(Data.CopyPaste.CurrentDropEffect)
                || e.PropertyName == nameof(Data.CopyPaste.MouseWithinApp)
                || e.PropertyName == nameof(Data.CopyPaste.ShellDropDescription))
                && Data.CopyPaste.DragBitmap is not null)
            {
                GetPathUnderMouse();
            }
        };

        if (Child is not null && PresentationSource.FromVisual(Child) is HwndSource hwndSource)
        {
            _dragWindowHandle = hwndSource.Handle;
            Services.WindowStyle.SetWindowHidden(_dragWindowHandle);
        }

        _startingScaling = MonitorInfo.DpiToScalingFactor(MonitorInfo.PrimaryMonitorDpi());
    }

    /// <summary>
    /// Begins tracking a drag. The window is positioned at the cursor before becoming visible so it
    /// never appears at its previous location first, then the mouse hook is installed to follow the
    /// cursor for the duration of the drag.
    /// </summary>
    private void BeginDrag()
    {
        Data.CopyPaste.ClearShellDropDescription();
        Data.CopyPaste.WindowUnderMouse = HWND.NULL;
        UpdatePosition(InterceptMouse.GetCursorPosition());

        // The main window may have come to the front since the drag window was last used.
        Services.WindowStyle.BringToTop(_dragWindowHandle);

#if DEBUG
        Data.CopyPaste.MouseWithinApp = true;
#else
        InterceptMouse.Init(UpdateMouse, CancelDrag, DragAutoScroll.OnMouseWheel, EndOnEscape);
#endif

        DragTimer.Start();

        // The tooltip still holds the last drag's text until it's drawn for this one
        GetPathUnderMouse();
    }

    /// <summary>
    /// Ends drag tracking and removes the mouse hook so it no longer adds latency to every mouse
    /// message once the drag is over.
    /// </summary>
    private void EndDrag()
    {
        DragTimer.Stop();
        Data.CopyPaste.ClearShellDropDescription();

#if !DEBUG
        InterceptMouse.Close();
#endif
    }

    private void CancelDrag()
    {
        Data.CopyPaste.CancelExternalDrag();
        Data.CopyPaste.DragBitmap = null;
    }

    // An external drag's source cancels on Esc without telling us, as the key reaches only the source
    private void EndOnEscape() => Data.CopyPaste.DragBitmap = null;

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

        var windowScaling = MonitorInfo.GetScalingFromWindow(_dragWindowHandle);

        if (Data.CopyPaste.DragImageRectPx is { } imageRect)
        {
            // An image with a set size is shown pixel for pixel; the scaling factor is 96 / dpi, so it converts pixels to DIPs.
            ViewModel.DragImageHeight = imageRect.Height * windowScaling;
            ViewModel.DragImageWidth = imageRect.Width * windowScaling;

            // A tab header or device is drawn partly see-through, as the drag image of anything else isn't.
            ViewModel.DragImageOpacity = Data.CopyPaste.IsContentDrag ? 0.9 : 1;
        }
        else
        {
            ViewModel.DragImageHeight = 96 * (1 / windowScaling);
            ViewModel.DragImageWidth = double.NaN;
            ViewModel.DragImageOpacity = 0.9;
        }

        var actualPoint = MonitorInfo.MousePositionToDpi(point, _startingScaling);

        // An image with a set rect stays where it was held, instead of hovering above the cursor.
        if (Data.CopyPaste.DragImageRectPx is { } rect)
        {
            var horizontalOffset = actualPoint.X + rect.X * _startingScaling;

            // A right-to-left popup is placed by its right edge, not its left.
            if (FlowDirection is FlowDirection.RightToLeft)
                horizontalOffset += ViewModel.DragImageWidth;

            // Each offset is set once, as every set moves the popup and an intermediate one would flicker.
            VerticalOffset = actualPoint.Y + rect.Y * _startingScaling;
            HorizontalOffset = horizontalOffset;

            return;
        }

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

        var previousHwnd = Data.CopyPaste.WindowUnderMouse;
        Data.CopyPaste.WindowUnderMouse = InterceptMouse.GetWindowUnderMouse();

        // Shouldn't happen. But if it does, we don't want to do anything.
        if (Data.CopyPaste.WindowUnderMouse == _dragWindowHandle)
            return;

        // A description belongs to the window that wrote it; the hook runs before the next target's DragEnter
        if (Data.CopyPaste.WindowUnderMouse != previousHwnd)
            Data.CopyPaste.ClearShellDropDescription();

        var wasWithinApp = Data.CopyPaste.MouseWithinApp;
        Data.CopyPaste.MouseWithinApp = Data.CopyPaste.WindowUnderMouse == InterceptClipboard.MainWindowHandle;

        // Only a foreign drag's image goes outside the app, where its source draws its own.
        // A dragged device is dropped out there, and a dragged tab keeps its header until the drag ends.
        if (!Data.CopyPaste.MouseWithinApp
            && Data.CopyPaste.DragStatus is CopyPasteService.DragState.None
            && !Data.CopyPaste.IsContentDrag)
            Data.CopyPaste.DragBitmap = null;

        if (!Data.CopyPaste.MouseWithinApp && wasWithinApp)
            Data.CopyPaste.PasteState = DragDropEffects.None;
    }

    private void Border_MouseUp(object sender, MouseButtonEventArgs e)
    {
        Data.CopyPaste.DragBitmap = null;
    }
}
