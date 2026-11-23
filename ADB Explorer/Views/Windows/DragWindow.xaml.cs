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

    /// <summary>The drag window's own scaling: read when it opens and at each drag, then kept current by its DpiChanged.</summary>
    private float _windowScaling = 1;

    public DragWindowViewModel ViewModel { get; } = new();

    public DragWindow()
    {
        InitializeComponent();

        DragTimer.Tick += DragTimer_Tick;
        _tooltipTimer.Tick += TooltipTimer_Tick;
        _tooltipHideTimer.Tick += TooltipHideTimer_Tick;

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
        MouseHook.Close();
#endif

        IsOpen = false;
    }

    private void DragTimer_Tick(object? sender, EventArgs e)
    {
#if DEBUG
        // No mouse hook in debug builds to follow the window under the cursor
        MouseHook.GetCursorPosition();
        Data.CopyPaste.WindowUnderMouse = MouseHook.GetWindowUnderMouse();
#endif

        if (Data.CopyPaste.DragBitmap is not null)
            GetPathUnderMouse();
    }

    private const int TOOLTIP_THROTTLE_MS = 50;
    private const int TOOLTIP_HIDE_HOLD_MS = 100;
    private const int DESCRIPTION_MISMATCH_HOLD_MS = 150;

    /// <summary>When the target's description started disagreeing with our effect, or 0 while they agree.</summary>
    private long _descriptionMismatchSince;
    private bool _isDescriptionMismatch;
    private bool _isTooltipHeld;

    private readonly DispatcherTimer _tooltipHideTimer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(TOOLTIP_HIDE_HOLD_MS) };

    /// <summary>
    /// A tooltip as a drop description has it: a message drawn in the accent color, around one insert
    /// (the target, or a tab's action) that keeps the default color.
    /// </summary>
    private readonly record struct TooltipText(string Prefix, string? Insert, string Suffix);
    private readonly DispatcherTimer _tooltipTimer = new(DispatcherPriority.Input);
    private long _lastTooltipUpdate;
    private bool _isBuildingTooltip;
    private bool _isTooltipRequestedWhileBuilding;
    private TooltipText? _tooltipText;
    private TooltipText? _shownTooltipText;

    /// <summary>What the tooltip being built describes, which picks its glyph.</summary>
    private DragEffect _tooltipEffect;

    private const int PILL_NAME_LENGTH = 30;

    /// <summary>
    /// Requests a tooltip update. Updates are throttled, and a request within the throttle
    /// window schedules one trailing update instead of being dropped.
    /// </summary>
    private void GetPathUnderMouse()
    {
        if (!Dispatcher.CheckAccess())
        {
            App.SafeBeginInvoke(GetPathUnderMouse);
            return;
        }

        // A property set while building (e.g. the device drop effect) is read by another pass
        if (_isBuildingTooltip)
        {
            _isTooltipRequestedWhileBuilding = true;
            return;
        }

        if (_tooltipTimer.IsEnabled)
            return;

        var wait = TOOLTIP_THROTTLE_MS - (Environment.TickCount64 - _lastTooltipUpdate);
        if (wait > 0)
        {
            _tooltipTimer.Interval = TimeSpan.FromMilliseconds(wait);
            _tooltipTimer.Start();
            return;
        }

        UpdateTooltip();
    }

    private void TooltipTimer_Tick(object? sender, EventArgs e)
    {
        _tooltipTimer.Stop();

        if (Data.CopyPaste.DragBitmap is not null)
            UpdateTooltip();
    }

    /// <summary>Builds the tooltip, and only replaces its inlines when the text has changed.</summary>
    private void UpdateTooltip()
    {
        _tooltipTimer.Stop();

        // A second pass is enough, as the first one already set what it needed
        for (int pass = 0; pass < 2; pass++)
        {
            _isTooltipRequestedWhileBuilding = false;
            _isDescriptionMismatch = false;
            _isTooltipHeld = false;
            _isBuildingTooltip = true;
            _tooltipText = null;
            try
            {
                BuildTooltip();
            }
            finally
            {
                _isBuildingTooltip = false;
            }

            if (!_isTooltipRequestedWhileBuilding)
                break;
        }

        if (!_isDescriptionMismatch)
            _descriptionMismatchSince = 0;

        ViewModel.SourcePill = GetSourcePill();
        ShowTooltip();
        _lastTooltipUpdate = Environment.TickCount64;
    }

    /// <summary>
    /// Shows what was built at once, but hides a shown tooltip only if it's still empty after <see cref="TOOLTIP_HIDE_HOLD_MS"/>.
    /// Between two targets the effect is briefly None, and hiding right away made it flicker.
    /// </summary>
    private void ShowTooltip()
    {
        var effect = Data.CopyPaste.CurrentDropEffect;
        var hide = _tooltipText is null || effect is DragDropEffects.None;

        if (hide && ViewModel.IsTooltipShown)
        {
            if (!_tooltipHideTimer.IsEnabled)
                _tooltipHideTimer.Start();

            return;
        }

        _tooltipHideTimer.Stop();

        if (_tooltipText is { } text && text != _shownTooltipText)
        {
            var insert = text.Insert ?? "";

            // The ranges first, so the text is laid out once with them
            ViewModel.TooltipInsertRanges = insert.Length > 0 ? [(text.Prefix.Length, insert.Length)] : null;
            ViewModel.TooltipText = text.Prefix + insert + text.Suffix;
            _shownTooltipText = text;
        }

        // A held text keeps its glyph too
        if (!hide && !_isTooltipHeld)
            ViewModel.TooltipEffect = _tooltipEffect;

        ViewModel.IsTooltipShown = !hide;
    }

    private void TooltipHideTimer_Tick(object? sender, EventArgs e)
    {
        _tooltipHideTimer.Stop();
        ViewModel.IsTooltipShown = false;
    }

    /// <summary>Sets the tooltip to <paramref name="message"/>, whose first <paramref name="placeholder"/> is replaced by <paramref name="insert"/>.</summary>
    private void SetTooltip(string message, string? insert = null, string placeholder = "{0}")
    {
        var index = message.IndexOf(placeholder, StringComparison.Ordinal);
        if (index < 0)
            _tooltipText = new(message, null, "");
        else
            _tooltipText = new(message[..index], insert, message[(index + placeholder.Length)..]);
    }

    private void BuildTooltip()
    {
        var dropEffect = Data.CopyPaste.CurrentDropEffect;
        _tooltipEffect = (DragEffect)dropEffect;

        // A dragged tab header says what dropping it does, when it can be dropped.
        if (Data.CopyPaste.IsTabDrag)
        {
            if (dropEffect is not DragDropEffects.None && Data.CopyPaste.DragTabTooltip is { } tabTooltip)
            {
                _tooltipEffect = DragEffect.SplitPane;
                SetTooltip(tabTooltip);
            }

            return;
        }

        // A folder over the tab strip opens in a new tab
        if (Data.CopyPaste.IsNewTabDrop)
        {
            if (dropEffect is not DragDropEffects.None)
            {
                _tooltipEffect = DragEffect.NewTab;
                SetTooltip(Strings.Resources.S_DRAG_OPEN_IN_NEW_TAB);
            }

            return;
        }

        // A dragged device says where it opens, over File Explorer or the desktop
        if (Data.CopyPaste.IsDeviceDrag)
        {
            var openText = DeviceMountDrag.TargetOf(Data.CopyPaste.WindowUnderMouse) switch
            {
                DeviceMountDrag.DropTarget.ExplorerWindow => Strings.Resources.S_DRAG_OPEN_IN_NEW_TAB,
                DeviceMountDrag.DropTarget.Desktop => Strings.Formatted.S_DRAG_OPEN_IN_EXPLORER,
                _ => null,
            };

            // The tooltip only shows with an effect; a change of it draws the tooltip anew
            var effect = openText is null ? DragDropEffects.None : DragDropEffects.Link;
            if (dropEffect != effect)
            {
                Data.CopyPaste.CurrentDropEffect = effect;
                return;
            }

            if (openText is not null)
            {
                _tooltipEffect = DragEffect.OpenInExplorer;
                SetTooltip(openText);
            }

            return;
        }

        if (Data.CopyPaste.DragFiles.Length == 0 || dropEffect is DragDropEffects.None)
            return;

        // Shouldn't happen. But if it does, we don't want to do anything.
        if (Data.CopyPaste.WindowUnderMouse == _dragWindowHandle)
            return;

        string? target = null;
        if (!Data.CopyPaste.MouseWithinApp
            && Data.CopyPaste.ShellDropDescription is { IsValid: true } description)
        {
            // The target's own wording is shown unless its verb disagrees with our effect (e.g. it's from before a key change)
            if (description.Type is not (DROPIMAGETYPE.DROPIMAGE_COPY or DROPIMAGETYPE.DROPIMAGE_MOVE or DROPIMAGETYPE.DROPIMAGE_LINK)
                || (DragDropEffects)description.Type == dropEffect)
            {
                SetTooltip(description.Message, description.Insert, "%1");
                return;
            }

            // On a key change the description and our effect switch a moment apart, so the shown text is kept for a while
            // instead of flashing our own wording. A mismatch that lasts falls back to it.
            _isDescriptionMismatch = true;
            if (_shownTooltipText is not null)
            {
                var now = Environment.TickCount64;
                if (_descriptionMismatchSince == 0)
                    _descriptionMismatchSince = now;

                var wait = DESCRIPTION_MISMATCH_HOLD_MS - (now - _descriptionMismatchSince);
                if (wait > 0)
                {
                    _tooltipText = _shownTooltipText;
                    _isTooltipHeld = true;
                    _tooltipTimer.Interval = TimeSpan.FromMilliseconds(wait);
                    _tooltipTimer.Start();
                    return;
                }
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

            // Self-drag on app drive is not an install target (pull-only).
            if (Data.FileActions.IsAppDrive)
            {
                if (!Data.CopyPaste.IsSelf)
                {
                    _tooltipEffect = DragEffect.Install;
                    SetTooltip(Strings.Resources.S_DRAG_INSTALL);
                }

                return;
            }

            target = Data.CopyPaste.DropTargetName;
        }

        // Worded as File Explorer's drop descriptions: the source is shown by the pill on the image, not here
        var hasTarget = !string.IsNullOrEmpty(target);
        var format = dropEffect switch
        {
            DragDropEffects.Link => Strings.Resources.S_DRAGDROP_LINK,
            DragDropEffects.Move when hasTarget => Strings.Resources.S_DRAGDROP_MOVE_TO,
            DragDropEffects.Move => Strings.Resources.S_DRAGDROP_MOVE,
            DragDropEffects.Copy when hasTarget => Strings.Resources.S_DRAGDROP_COPY_TO,
            DragDropEffects.Copy => Strings.Resources.S_DRAGDROP_COPY,
            _ => null,
        };

        if (format is not null)
            SetTooltip(format, target);
    }

    /// <summary>
    /// The source's name, or how many items are dragged, shown on the drag image. Null when there's no pill:
    /// for a content drag, and for a single item whose image is its thumbnail, which already shows what it is.
    /// </summary>
    private static string? GetSourcePill()
    {
        var copyPaste = Data.CopyPaste;
        if (copyPaste.IsContentDrag || copyPaste.DragFiles.Length == 0)
            return null;

        // 7-Zip and the new Outlook name their files only at the drop, so they're counted as "??"
        if (copyPaste.HasUnnamedFiles)
            return Strings.Plurals.ItemCount(null);

        if (copyPaste.DragFiles.Length > 1)
            return Strings.Plurals.ItemCount(copyPaste.DragFiles.Length);

        if (IsThumbnailDragImage())
            return null;

        // Trash items are dragged by their indexer name; show the name they were deleted with,
        // and a dragged drive by its own name, not its root folder's
        var sourceName = copyPaste.DragFiles[0];
        if (copyPaste.IsFromTrash && copyPaste.CurrentFiles.FirstOrDefault() is { TrashIndex: not null } trashItem)
            sourceName = trashItem.FullName;
        else if (copyPaste.SelfDragDrive is { } drive)
            sourceName = drive.DisplayName;

        return FileHelper.GetShortFileName(sourceName, PILL_NAME_LENGTH);
    }

    private static bool IsThumbnailDragImage()
    {
        var copyPaste = Data.CopyPaste;
        if (copyPaste.DragBitmap is not { } bitmap)
            return false;

        if (ReferenceEquals(bitmap, copyPaste.DragThumbnail))
            return true;

        return copyPaste.DragStatus is CopyPasteService.DragState.Active
            && copyPaste.SelfDragDrive is null
            && ReferenceEquals(bitmap, copyPaste.CurrentFiles.FirstOrDefault()?.CacheThumbnail?.Image);
    }

    private void Popup_Opened(object sender, EventArgs e)
    {
        FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (Data.RuntimeSettings.IsRTL)
        {
            HORIZONTAL_OFFSET = -2;

            ContentGrid.HorizontalAlignment = HorizontalAlignment.Left;
        }

        // Removed first, so opening the popup again never subscribes twice
        Data.CopyPaste.PropertyChanged -= CopyPaste_PropertyChanged;
        Data.CopyPaste.PropertyChanged += CopyPaste_PropertyChanged;

        if (Child is not null && PresentationSource.FromVisual(Child) is HwndSource hwndSource)
        {
            _dragWindowHandle = hwndSource.Handle;
            Services.WindowStyle.SetWindowHidden(_dragWindowHandle);

            hwndSource.DpiChanged -= HwndSource_DpiChanged;
            hwndSource.DpiChanged += HwndSource_DpiChanged;
        }

        RefreshScaling();
    }

    private void CopyPaste_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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
            || e.PropertyName == nameof(Data.CopyPaste.ShellDropDescription)
            || e.PropertyName == nameof(Data.CopyPaste.NewTabDropName))
            && Data.CopyPaste.DragBitmap is not null)
        {
            GetPathUnderMouse();
        }
    }

    private void HwndSource_DpiChanged(object sender, HwndDpiChangedEventArgs e)
        => _windowScaling = (float)(1 / e.NewDpi.DpiScaleX);

    /// <summary>
    /// Reads both scaling factors anew. Called when the window opens and at the start of each drag,
    /// so a scale changed in Windows' display settings while the app runs is picked up.
    /// </summary>
    private void RefreshScaling()
    {
        _startingScaling = MonitorInfo.DpiToScalingFactor(MonitorInfo.PrimaryMonitorDpi());

        if (!_dragWindowHandle.IsNull)
            _windowScaling = MonitorInfo.GetScalingFromWindow(_dragWindowHandle);
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
        RefreshScaling();
        UpdatePosition(MouseHook.GetCursorPosition());

        // The main window may have come to the front since the drag window was last used.
        Services.WindowStyle.BringToTop(_dragWindowHandle);

#if DEBUG
        Data.CopyPaste.MouseWithinApp = true;
#else
        MouseHook.Init(UpdateMouse, CancelDrag, DragAutoScroll.OnMouseWheel, EndOnEscape, WindowChanged);
#endif

        DragTimer.Start();

        // The tooltip still holds the last drag's text until it's drawn for this one, so it's not throttled or held
        ViewModel.IsTooltipShown = false;
        UpdateTooltip();
    }

    /// <summary>
    /// Ends drag tracking and removes the mouse hook so it no longer adds latency to every mouse
    /// message once the drag is over.
    /// </summary>
    private void EndDrag()
    {
        DragTimer.Stop();
        _tooltipTimer.Stop();
        _tooltipHideTimer.Stop();
        Data.CopyPaste.ClearShellDropDescription();
        Data.CopyPaste.NewTabDropName = null;
        MouseHook.ClearFollower();

#if !DEBUG
        MouseHook.Close();
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
    /// Runs on the hook thread. A description belongs to the window that wrote it, and this runs
    /// before the next target's DragEnter, so its clear is queued before that target's description.
    /// </summary>
    private void WindowChanged(HWND window)
    {
        if (window != _dragWindowHandle)
            Data.CopyPaste.PostClearShellDropDescription();
    }

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

        var windowScaling = _windowScaling;

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

            MoveTo(horizontalOffset, actualPoint.Y + rect.Y * _startingScaling, point);
            return;
        }

        double imageHeight = DragImage.ActualHeight >= 1
            ? DragImage.ActualHeight
            : ViewModel.DragImageHeight;

        double imageWidth;
        if (DragImage.ActualWidth >= 1)
            imageWidth = DragImage.ActualWidth;
        else if (bitmap.PixelHeight > 0)
            imageWidth = imageHeight * bitmap.PixelWidth / bitmap.PixelHeight;
        else
            imageWidth = imageHeight;

        MoveTo(actualPoint.X - imageWidth / HORIZONTAL_OFFSET, actualPoint.Y - imageHeight - VERTICAL_OFFSET, point);
    }

    /// <summary>
    /// Sets the offsets (unchanged ones don't move the popup), then hands the hook the window's offset from the cursor
    /// in physical pixels, so it keeps following while this thread is blocked (e.g. over Explorer).
    /// </summary>
    private void MoveTo(double x, double y, POINT cursor)
    {
        VerticalOffset = y;
        HorizontalOffset = x;

        if (!_dragWindowHandle.IsNull && Vanara.PInvoke.User32.GetWindowRect(_dragWindowHandle, out var bounds))
            MouseHook.SetFollower(_dragWindowHandle, new(bounds.left - cursor.X, bounds.top - cursor.Y));
    }

    private void UpdateMouse(POINT point)
    {
        if (Data.CopyPaste.DragBitmap is null)
            return;

        UpdatePosition(point);

        // The hook found it, and already cleared the last window's description (see WindowChanged)
        Data.CopyPaste.WindowUnderMouse = MouseHook.WindowUnderMouse;

        // Shouldn't happen. But if it does, we don't want to do anything.
        if (Data.CopyPaste.WindowUnderMouse == _dragWindowHandle)
            return;

        var wasWithinApp = Data.CopyPaste.MouseWithinApp;
        Data.CopyPaste.MouseWithinApp = Data.CopyPaste.WindowUnderMouse == ClipboardHook.MainWindowHandle;

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
