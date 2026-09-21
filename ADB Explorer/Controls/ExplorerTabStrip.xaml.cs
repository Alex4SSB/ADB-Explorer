using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for ExplorerTabStrip.xaml
/// </summary>
public partial class ExplorerTabStrip : UserControl
{
    /// <summary>How long a drag must hover over a tab before that tab is switched to.</summary>
    private static readonly TimeSpan DragHoverDelay = TimeSpan.FromMilliseconds(600);

    private readonly DispatcherTimer _dragHoverTimer = new() { Interval = DragHoverDelay };

    private ExplorerInstance? _dragHoverTab;

    /// <summary>Data format of a tab header being dragged out to become a split view's pane.</summary>
    private const string TabDragFormat = "ADBExplorer.TabHeader";

    /// <summary>Raised around a tab header drag, so the window can show where the tab would be dropped.</summary>
    public event Action<ExplorerInstance>? TabDragStarted;

    public event Action? TabDragEnded;

    private ExplorerInstance? _pressedTab;

    private Point _pressPoint;

    private Point _pressPointInItem;

    public ExplorerTabStrip()
    {
        DataContext = App.Services.GetService<ExplorerTabsViewModel>();

        _dragHoverTimer.Tick += DragHoverTimer_Tick;

        InitializeComponent();

        // Deferred: the shortcut text comes from AppActions, which isn't ready while the main window is still being built.
        Loaded += (_, _) => AddTabButton.ToolTip = ((ExplorerTabsViewModel)DataContext).NewTabTooltip;
    }

    private void AddTab_Click(object sender, RoutedEventArgs e) =>
        ((ExplorerTabsViewModel)DataContext).AddTab();

    /// <summary>The button has no menu of its own, and the title bar behind it would show the system one.</summary>
    private void AddTab_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    /// <summary>Opens the tab's own menu by hand: the title bar swallows the right click before WPF would, and would show the system menu.</summary>
    private void TabsList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // The release that cancelled a tab drag isn't a click.
        if (Data.CopyPaste.WasDragging)
        {
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(TabsList, source) is not ListBoxItem { DataContext: ExplorerInstance tab, ContextMenu: { } menu } item)
            return;

        TabContextMenu.SetFor(tab);
        menu.PlacementTarget = item;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>A right click opens the tab's menu without switching to it, which pressing it would do.</summary>
    private void TabItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        => e.Handled = true;

    /// <summary>A tab is switched to when the mouse is released on it, so it can also be pressed and dragged away.</summary>
    private void TabItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Data.CopyPaste.WasDragging = false;

        if (HitTestHelper.FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
            return;

        if (sender is not FrameworkElement { DataContext: ExplorerInstance tab } item)
            return;

        _pressedTab = tab;
        _pressPoint = e.GetPosition(this);
        _pressPointInItem = e.GetPosition(item);
        item.CaptureMouse();
        e.Handled = true;
    }

    private void TabItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTab is not { } tab || e.LeftButton is not MouseButtonState.Pressed || sender is not FrameworkElement item)
            return;

        var offset = e.GetPosition(this) - _pressPoint;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _pressedTab = null;
        item.ReleaseMouseCapture();

        TabDragStarted?.Invoke(tab);

        // The app's drag window follows the cursor, showing the tab's header where a drag image would be.
        Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
        Data.CopyPaste.IsTabDrag = true;
        Data.CopyPaste.DragTab = tab;

        var scale = VisualTreeHelper.GetDpi(item).DpiScaleX;

        // Held where the header was grabbed, at the size of its body.
        // In right-to-left layouts the press point is measured from the header's right edge.
        var pressX = _pressPointInItem.X;
        if (item.FlowDirection is FlowDirection.RightToLeft)
            pressX = item.ActualWidth - pressX;

        Data.CopyPaste.DragImageRectPx = new Rect(
            -pressX * scale,
            -_pressPointInItem.Y * scale,
            item.ActualWidth * scale,
            TabBodyHeight(item) * scale);

        // A drag is under way while a bitmap is set; a blank one stands in, as the header is drawn from the tab.
        Data.CopyPaste.DragBitmap = BlankDragBitmap;

        DragDrop.AddGiveFeedbackHandler(item, TabDrag_GiveFeedback);
        Data.CopyPaste.WasDragging = true;

        try
        {
            DragDrop.DoDragDrop(item, new DataObject(TabDragFormat, tab), DragDropEffects.Move);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(item, TabDrag_GiveFeedback);

            Data.CopyPaste.DragBitmap = null;
            Data.CopyPaste.DragTab = null;
            Data.CopyPaste.DragImageRectPx = null;
            Data.CopyPaste.DragTabTooltip = null;
            Data.CopyPaste.IsTabDrag = false;
            TabDragEnded?.Invoke();
        }

        ClearWasDraggingWhenIdle();
    }

    /// <summary>The drag window says what a drop does, so Windows' own move / copy cursor is replaced by the plain arrow.</summary>
    private static void TabDrag_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(Cursors.Arrow);
        e.Handled = true;
    }

    /// <summary>
    /// A drag cancelled with a button leaves buttons held, whose releases must not act as clicks, so
    /// the flag stays until none is down - checked after each release, once its menus had their chance.
    /// </summary>
    private void ClearWasDraggingWhenIdle()
    {
        if (Window.GetWindow(this) is not { } window)
        {
            Data.CopyPaste.WasDragging = false;
            return;
        }

        MouseButtonEventHandler? released = null;

        void Check()
        {
            if (MouseState.IsAnyButtonDown)
                return;

            window.RemoveHandler(UIElement.PreviewMouseUpEvent, released!);
            Data.CopyPaste.WasDragging = false;
        }

        released = (_, _) => App.SafeBeginInvoke(Check, DispatcherPriority.Background);
        window.AddHandler(UIElement.PreviewMouseUpEvent, released, true);

        // A drag that ended in a drop has no release still to come.
        App.SafeBeginInvoke(Check, DispatcherPriority.Input);
    }

    /// <summary>The release that cancelled a tab drag doesn't open the tab's own menu either.</summary>
    private void TabItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Data.CopyPaste.WasDragging)
            e.Handled = true;
    }

    private static readonly BitmapSource BlankDragBitmap = CreateBlankDragBitmap();

    private static BitmapSource CreateBlankDragBitmap()
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Pbgra32, null, new byte[4], 4);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>The tab's body leaves out the strip below it that the active tab fuses through.</summary>
    private static double TabBodyHeight(FrameworkElement item)
    {
        var body = (item as Control)?.Template?.FindName("Bd", item) as Border;
        if (body is { ActualHeight: > 0 })
            return body.ActualHeight;

        return item.ActualHeight;
    }

    private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressedTab is not { } tab || sender is not FrameworkElement item)
            return;

        _pressedTab = null;
        item.ReleaseMouseCapture();

        if (new Rect(0, 0, item.ActualWidth, item.ActualHeight).Contains(e.GetPosition(item)))
            ((ExplorerTabsViewModel)DataContext).ActiveTab = tab;

        e.Handled = true;
    }

    /// <summary>Dropping onto a tab isn't supported, but hovering a drag over it brings that tab forward.</summary>
    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        // A dragged tab header is only meant for the page area.
        if (e.Data.GetDataPresent(TabDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement { DataContext: ExplorerInstance tab } && !ReferenceEquals(_dragHoverTab, tab))
        {
            _dragHoverTab = tab;
            _dragHoverTimer.Stop();
            _dragHoverTimer.Start();
        }

        e.Effects = DragDropEffects.None;
        Data.CopyPaste.DropEffect = Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
        e.Handled = true;
    }

    private void TabItem_DragLeave(object sender, DragEventArgs e) => StopDragHover();

    private void TabItem_Drop(object sender, DragEventArgs e)
    {
        StopDragHover();
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void StopDragHover()
    {
        _dragHoverTimer.Stop();
        _dragHoverTab = null;
    }

    private void DragHoverTimer_Tick(object? sender, EventArgs e)
    {
        _dragHoverTimer.Stop();

        var tabs = (ExplorerTabsViewModel)DataContext;
        if (_dragHoverTab is { } tab && tabs.Tabs.Contains(tab) && !ReferenceEquals(tabs.ActiveTab, tab))
            tabs.ActiveTab = tab;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not ExplorerInstance tab)
            return;

        ((ExplorerTabsViewModel)DataContext).CloseTab(tab);
        e.Handled = true;
    }
}
