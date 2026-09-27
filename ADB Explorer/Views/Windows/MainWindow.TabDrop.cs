using System.Windows.Media.Animation;

namespace ADB_Explorer.Views.Windows;

public partial class MainWindow
{
    private ExplorerInstance? _draggedTab;

    private void TabStrip_TabDragStarted(ExplorerInstance tab)
    {
        _draggedTab = tab;
        // Laid out first, as the list's place is measured against the overlay.
        TabDropOverlay.Visibility = Visibility.Visible;
        TabDropOverlay.UpdateLayout();
        TabDropRegion.Margin = DropRegionMargin();
    }

    /// <summary>In the explorer, the list itself - not its toolbar, status bar or details pane; on a page, what is beside the navigation and details panes.</summary>
    private Thickness DropRegionMargin()
    {
        if (Data.CurrentPage.Value == typeof(ExplorerPage)
            && ExplorerTabs.ActiveTab is { } tab
            && _explorerContents.TryGetValue(tab, out var content)
            && content.ListBounds(TabDropOverlay) is { } list)
        {
            return new Thickness(
                Math.Max(0, list.Left),
                Math.Max(0, list.Top),
                Math.Max(0, TabDropOverlay.ActualWidth - list.Right),
                Math.Max(0, TabDropOverlay.ActualHeight - list.Bottom));
        }

        return new Thickness(Data.Settings.NavigationPaneWidth + 8, 0, OpenDetailsPaneWidth(), 0);
    }

    /// <summary>The details pane is not part of the area a tab can be dropped on.</summary>
    private double OpenDetailsPaneWidth()
    {
        if (Data.CurrentPage.Value != typeof(ExplorerPage)
            || !Data.Settings.IsDetailsPaneOpen
            || ExplorerTabs.ActiveTab is not { } tab
            || !_explorerContents.TryGetValue(tab, out var content))
            return 0;

        return content.DetailsPaneControl.IsVisible ? content.DetailsPaneControl.ActualWidth : 0;
    }

    private void TabStrip_TabDragEnded()
    {
        _draggedTab = null;
        TabDropOverlay.Visibility = Visibility.Collapsed;
        HideTabDropHints();
    }

    /// <summary>The zone last hinted, kept while the pointer is in the region's center.</summary>
    private SplitSide? _lastDropSide;

    /// <summary>Fraction of the drop region's width and height, centered, where the zone doesn't follow the pointer.</summary>
    private const double DropDeadZoneFraction = 0.1;

    private void HideTabDropHints()
    {
        _lastDropSide = null;
        _hintedSide = null;

        DropHint.Visibility = Visibility.Collapsed;
        ClearDropHintAnimations();
    }

    private static readonly DependencyProperty[] DropHintProperties =
    [
        Canvas.LeftProperty,
        Canvas.TopProperty,
        FrameworkElement.WidthProperty,
        FrameworkElement.HeightProperty,
    ];

    private void ClearDropHintAnimations()
    {
        foreach (var property in DropHintProperties)
            DropHint.BeginAnimation(property, null);
    }

    private static readonly TimeSpan DropHintSlideDuration = TimeSpan.FromMilliseconds(180);

    private const double DropHintInset = 6;

    /// <summary>The zone the hint currently marks, null while it is hidden.</summary>
    private SplitSide? _hintedSide;

    /// <summary>Shows the hint over the half of the page area <paramref name="side"/> names; it slides there from wherever it was.</summary>
    private void ShowDropHint(SplitSide side)
    {
        if (_hintedSide == side)
            return;

        var width = TabDropRegion.ActualWidth;
        var height = TabDropRegion.ActualHeight;

        var zone = side switch
        {
            SplitSide.Left => new Rect(0, 0, width / 2, height),
            SplitSide.Right => new Rect(width / 2, 0, width / 2, height),
            SplitSide.Top => new Rect(0, 0, width, height / 2),
            _ => new Rect(0, height / 2, width, height / 2),
        };

        zone.Inflate(-DropHintInset, -DropHintInset);
        double[] targets = [zone.X, zone.Y, zone.Width, zone.Height];

        // Appearing, it is placed straight away; already shown, it eases from its current place.
        var slide = DropHint.Visibility is Visibility.Visible;
        DropHint.Visibility = Visibility.Visible;
        _hintedSide = side;

        for (var i = 0; i < targets.Length; i++)
        {
            if (!slide)
            {
                DropHint.BeginAnimation(DropHintProperties[i], null);
                DropHint.SetValue(DropHintProperties[i], targets[i]);
                continue;
            }

            DropHint.BeginAnimation(DropHintProperties[i], new DoubleAnimation(targets[i], DropHintSlideDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
    }

    /// <summary>Whether the dragged tab can be dropped here, and against which edge of the page area - the one the pointer is nearest to.</summary>
    private bool TryGetTabDropSide(DragEventArgs e, out SplitSide side)
    {
        side = SplitSide.Right;

        if (_draggedTab is not { } dragged
            || ExplorerTabs.ActiveTab is not { } target
            || !ExplorerTabs.CanMerge(target, dragged))
            return false;

        var width = TabDropRegion.ActualWidth;
        var height = TabDropRegion.ActualHeight;
        var point = e.GetPosition(TabDropRegion);
        if (point.X < 0 || point.X > width || point.Y < 0 || point.Y > height || width <= 0 || height <= 0)
            return false;

        var inCenter = Math.Abs(point.X / width - 0.5) <= DropDeadZoneFraction / 2
            && Math.Abs(point.Y / height - 0.5) <= DropDeadZoneFraction / 2;

        if (inCenter)
        {
            // With no zone hinted yet, the center offers no drop.
            if (_lastDropSide is not { } previous)
                return false;

            side = previous;
            return true;
        }

        // Distances are relative to the region's size, so the four zones are the triangles cut by its diagonals.
        var distances = new (SplitSide Side, double Distance)[]
        {
            (SplitSide.Left, point.X / width),
            (SplitSide.Right, 1 - point.X / width),
            (SplitSide.Top, point.Y / height),
            (SplitSide.Bottom, 1 - point.Y / height),
        };

        side = distances.MinBy(d => d.Distance).Side;
        _lastDropSide = side;

        return true;
    }

    private void TabDropOverlay_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!TryGetTabDropSide(e, out var side))
        {
            e.Effects = DragDropEffects.None;
            Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
            HideTabDropHints();
            return;
        }

        e.Effects = DragDropEffects.Move;

        if (side.IsStacked())
            Data.CopyPaste.DragTabTooltip = Strings.Resources.S_SPLIT_VERTICALLY;
        else
            Data.CopyPaste.DragTabTooltip = Strings.Resources.S_SPLIT_HORIZONTALLY;

        Data.CopyPaste.CurrentDropEffect = DragDropEffects.Move;
        ShowDropHint(side);
    }

    private void TabDropOverlay_DragLeave(object sender, DragEventArgs e)
    {
        Data.CopyPaste.CurrentDropEffect = DragDropEffects.None;
        HideTabDropHints();
    }

    private void TabDropOverlay_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // The zone is read first: hiding the hints forgets the last one, which the center relies on.
        var canDrop = TryGetTabDropSide(e, out var side);
        HideTabDropHints();

        if (!canDrop || _draggedTab is not { } dragged || ExplorerTabs.ActiveTab is not { } target)
            return;

        // Dropped onto its own tab, it splits that tab instead.
        if (ReferenceEquals(target, dragged))
            ExplorerTabs.OpenSplit(target, side);
        else
            ExplorerTabs.MergeTab(target, dragged, side);
    }
}
