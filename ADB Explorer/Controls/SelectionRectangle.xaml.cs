using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls;

/// <summary>
/// A reusable rubber-band selection rectangle overlay.
/// Place this control on top of a <see cref="System.Windows.Controls.Primitives.Selector"/>
/// (DataGrid or ListView) so that they share the same coordinate space.
/// </summary>
public partial class SelectionRectangle : UserControl
{
    private Point? _mouseDownPoint;
    private HashSet<object> _preRectSelectedItems = [];

    /// <summary>
    /// Raised when the mouse moves over the visible selection border,
    /// so that the parent can forward the event to the appropriate view's MouseMove handler.
    /// </summary>
    public event MouseEventHandler RectMouseMove;

    /// <summary>
    /// Whether the selection rectangle is currently visible (i.e. a drag-selection is in progress).
    /// </summary>
    public bool IsActive => Rect.IsVisible;

    public SelectionRectangle()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Collapses the selection rectangle and resets the internal mouse-down tracking point.
    /// </summary>
    public void Collapse()
    {
        Rect.Visibility = Visibility.Collapsed;
        _mouseDownPoint = null;
    }

    /// <summary>
    /// Updates the selection rectangle geometry and performs hit-testing against
    /// the items of <paramref name="activeView"/>.
    /// </summary>
    /// <param name="mousePosition">Current mouse position relative to this control.</param>
    /// <param name="mouseDownPoint">Original mouse-down position relative to this control (from the parent).</param>
    /// <param name="scroller">The <see cref="ScrollViewer"/> inside the active view.</param>
    /// <param name="activeView">The currently active <see cref="Selector"/> (DataGrid or ListView).</param>
    /// <param name="activeSelectedItems">The selected-items list of the active view.</param>
    /// <param name="viewModel">The <see cref="ExplorerViewModel"/> for updating selection indices.</param>
    public void Update(
        Point mousePosition,
        Point mouseDownPoint,
        ScrollViewer scroller,
        Selector activeView,
        System.Collections.IList activeSelectedItems,
        ExplorerViewModel viewModel)
    {
        if (scroller is null)
            return;

        // Use the stored internal point if we already started a rect, otherwise adopt the caller's point
        _mouseDownPoint ??= mouseDownPoint;
        var origin = _mouseDownPoint.Value;

        var horizontal = scroller.ComputedHorizontalScrollBarVisibility is Visibility.Visible ? 1 : 0;
        var vertical = scroller.ComputedVerticalScrollBarVisibility is Visibility.Visible ? 1 : 0;

        if (mousePosition.X < 0 || mousePosition.Y < 0
            || mousePosition.X > ActualWidth - SystemParameters.VerticalScrollBarWidth * vertical
            || mousePosition.Y > ActualHeight - SystemParameters.HorizontalScrollBarHeight * horizontal)
            return;

        if (!Rect.IsVisible)
            _preRectSelectedItems = [.. activeSelectedItems.Cast<object>()];

        Rect.Visibility = Visibility.Visible;

        if (mousePosition.Y > origin.Y)
            Canvas.SetTop(Rect, origin.Y);
        else
            Canvas.SetTop(Rect, mousePosition.Y);

        if (mousePosition.X > origin.X)
            Canvas.SetLeft(Rect, origin.X);
        else
            Canvas.SetLeft(Rect, mousePosition.X);

        Rect.Height = Math.Abs(origin.Y - mousePosition.Y);
        Rect.Width = Math.Abs(origin.X - mousePosition.X);

        SelectItemsByRect(mousePosition, activeView, activeSelectedItems, viewModel);
    }

    private void SelectItemsByRect(Point mousePosition, Selector view, System.Collections.IList activeSelectedItems, ExplorerViewModel viewModel)
    {
        Rect selection = new(Canvas.GetLeft(Rect),
                             Canvas.GetTop(Rect),
                             Rect.Width,
                             Rect.Height);

        for (int i = 0; i < view.ItemContainerGenerator.Items.Count; i++)
        {
            if (view.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                continue;

            Size size = container is DataGridRow ? container.DesiredSize : container.RenderSize;
            Rect itemRect = new(container.TranslatePoint(new(), this), size);

            bool intersects = itemRect.IntersectsWith(selection);
            bool wasPreSelected = _preRectSelectedItems.Contains(view.Items[i]);
            bool isSelected = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                ? wasPreSelected ^ intersects
                : wasPreSelected || intersects;

            switch (container)
            {
                case DataGridRow row:
                    row.IsSelected = isSelected;
                    itemRect.Inflate(double.PositiveInfinity, 0);
                    break;
                case ListViewItem item:
                    item.IsSelected = isSelected;
                    break;
            }

            if (itemRect.Contains(mousePosition))
                viewModel.CurrentSelectedIndex = i;
        }

        if (activeSelectedItems.Count == 1
            && (viewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift))
        {
            viewModel.FirstSelectedIndex = view.SelectedIndex;
        }
    }

    private void Rect_MouseMove(object sender, MouseEventArgs e)
    {
        RectMouseMove?.Invoke(sender, e);
    }
}
