using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

public partial class ExplorerListHost
{
    internal void ActiveScrollIntoView(object item)
    {
        if (item is null)
            return;

        if (Owner.Instance.IsIconView)
            IconView.ScrollIntoView(item);
        else
        {
            ActiveDataGrid.ScrollIntoView(item);
            // ScrollIntoView aligns the row box and skips the 3px left margin; restore it.
            ResetExplorerHorizontalScroll();
        }
    }

    internal ScrollViewer ExplorerScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid);
            return field;
        }
    } = null;

    internal ScrollViewer ContentScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(ContentGrid);
            return field;
        }
    } = null;

    internal ScrollViewer IconScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(IconView);
            return field;
        }
    } = null;

    internal ScrollViewer ActiveScrollViewer => Owner.Instance.IsIconView
        ? IconScrollViewer
        : Owner.Instance.IsContentView ? ContentScrollViewer : ExplorerScrollViewer;

    internal void ResetExplorerHorizontalScroll()
    {
        void reset() => ActiveScrollViewer?.ScrollToHorizontalOffset(0);

        reset();
        // DataGrid.ScrollIntoView often defers BringIntoView to Loaded; run after that
        // so the row left margin is not scrolled off against the tree splitter.
        App.SafeBeginInvoke(reset, DispatcherPriority.Loaded);
        App.SafeBeginInvoke(reset, DispatcherPriority.Input);
    }

    private double? RowHeight { get; set; }

    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight") + ScrollContentPresenterMargin;

    private double ScrollContentPresenterMargin => ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top;

    private double DataGridContentWidth
        => StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid) is ItemsPresenter presenter ? presenter.ActualWidth : 0;

    /// <summary>Debounces APK icon priority updates on scroll / selection.</summary>
    private readonly DispatcherTimer _apkPriorityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>
    /// Coalesces Content view column layout recalculations - see
    /// <see cref="ContentGrid_SizeChanged"/> for why this is deferred rather than applied inline.
    /// </summary>
    private int _contentColumnsGeneration;

    internal void ScheduleApkIconPriorityUpdate()
    {
        _apkPriorityTimer.Stop();
        _apkPriorityTimer.Start();
    }

    private void UpdateApkIconPriorities()
    {
        if (!FileActions.IsAppDrive || Data.Packages is null || Data.Packages.Count == 0)
            return;

        var selected = SelectedPackages.ToList();
        var visible = CollectVisiblePackages();
        ApkIconService.UpdatePackageLoadPriorities(selected, visible);
    }

    private List<Package> CollectVisiblePackages()
    {
        List<Package> visible = [];
        if (Owner.Instance.IsIconView)
        {
            var range = IconView.VisibleRange;
            var count = IconView.Items.Count;
            for (int i = range.StartIndex; i <= range.EndIndex && i < count; i++)
            {
                if (i < 0)
                    continue;
                if (IconView.Items[i] is Package package)
                    visible.Add(package);
            }
        }
        else
        {
            var grid = ActiveDataGrid;
            var generator = grid.ItemContainerGenerator;
            for (int i = 0; i < grid.Items.Count; i++)
            {
                if (generator.ContainerFromIndex(i) is null)
                    continue;
                if (grid.Items[i] is Package package)
                    visible.Add(package);
            }
        }

        return visible;
    }

    private void ExplorerScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!FileActions.IsAppDrive)
            return;
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0 && e.ExtentHeightChange == 0)
            return;

        ScheduleApkIconPriorityUpdate();
    }

    private void DataGridCell_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is DataGridCell && e.TargetRect == Rect.Empty)
        {
            e.Handled = true;
        }
    }

    internal void OnThumbsSizeChanged()
    {
        var size = RuntimeSettings.ThumbsSize;
        if (size != ThumbnailService.ThumbnailSize.Disabled)
            Owner.InvalidateFileIcons();

        if (ActiveSelectedItems.Count > 0)
            ScheduleKeepFirstSelectedInView();
        else
        {
            App.SafeBeginInvoke(() => ActiveScrollViewer?.ScrollToTop(), DispatcherPriority.Loaded);
        }
    }

    private void IconView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || !Owner.Instance.IsIconView || ActiveSelectedItems.Count == 0)
            return;

        ScheduleKeepFirstSelectedInView();
    }

    private void DriveList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged
            || !FileActions.IsDriveViewVisible
            || DriveList.SelectedItem is null)
            return;

        var generation = ++_keepSelectionInViewGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _keepSelectionInViewGeneration)
                return;
            if (DriveList.SelectedItem is not null)
                DriveList.ScrollIntoView(DriveList.SelectedItem);
        }, DispatcherPriority.Loaded);
    }

    private const double ContentNameComfortableWidth = 350;

    // Type names resolve after their rows are shown, so an auto-sized column would start out empty and narrow.
    private const double ContentTypeEstimatedWidth = 200;

    private const double ContentDateSizeFullWidth = 250;

    private const double ContentDateSizeCollapsedWidth = 130;

    private const double ContentTypeColumnHideWidth =
        ContentNameComfortableWidth + ContentDateSizeFullWidth + ContentTypeEstimatedWidth;

    private const double ContentDateLineHideWidth =
        ContentNameComfortableWidth + ContentDateSizeFullWidth;

    private const double ContentDateSizeColumnHideWidth = 400;

    public static readonly DependencyProperty ContentDateLineVisibilityProperty =
        DependencyProperty.Register(
            nameof(ContentDateLineVisibility),
            typeof(Visibility),
            typeof(ExplorerListHost),
            new PropertyMetadata(Visibility.Visible));

    public Visibility ContentDateLineVisibility
    {
        get => (Visibility)GetValue(ContentDateLineVisibilityProperty);
        set => SetValue(ContentDateLineVisibilityProperty, value);
    }

    private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        var generation = ++_contentColumnsGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _contentColumnsGeneration)
                return;

            ApplyContentColumnLayout(ContentGrid.ActualWidth);
        }, DispatcherPriority.Loaded);
    }

    private void ApplyContentColumnLayout(double width)
    {
        ContentTypeColumn.Visibility = width < ContentTypeColumnHideWidth
            ? Visibility.Collapsed
            : Visibility.Visible;

        ContentDateSizeColumn.Visibility = width < ContentDateSizeColumnHideWidth
            ? Visibility.Collapsed
            : Visibility.Visible;

        var showDateLine = width >= ContentDateLineHideWidth;
        ContentDateSizeColumn.Width = new(showDateLine ? ContentDateSizeFullWidth : ContentDateSizeCollapsedWidth);
        ContentDateLineVisibility = showDateLine ? Visibility.Visible : Visibility.Hidden;
    }

    // Wired at each source (list + item containers), not just this ScrollViewer - see EventSetter usages.
    private void DriveScrollViewer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        => e.Handled = true;
}
