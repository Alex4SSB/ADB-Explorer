using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

public class AdbVirtualizingWrapPanel : Wpf.Ui.Controls.VirtualizingWrapPanel
{
    public new Size ChildSize => base.ChildSize;

    public new int ItemsPerRowCount => base.ItemsPerRowCount;

    private int _prevEndIndex = -1;
    private int _prevItemsCount = -1;
    private CancellationTokenSource? _placeholderCts;
    private int _earliestPlaceholderIndex = -1;
    private double _viewportHeight;

    public int ItemsInView => ChildSize.Height > 0 && ItemsPerRowCount > 0
        ? (int)Math.Ceiling(_viewportHeight / ChildSize.Height) * ItemsPerRowCount
        : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        _viewportHeight = availableSize.Height;
        var result = base.MeasureOverride(availableSize);

        // base.MeasureOverride calls UpdateChildSize (which reads the pre-measure DesiredSize to set
        // ChildSize) and then measures children (updating their DesiredSize). If the two differ, the
        // ChildSize used for layout this pass is stale; schedule a second pass to correct it.
        if (ItemSize == Size.Empty && InternalChildren.Count > 0 && ChildSize != InternalChildren[0].DesiredSize)
            InvalidateMeasure();

        return result;
    }

    protected override Wpf.Ui.Controls.ItemRange UpdateItemRange()
    {
        var range = base.UpdateItemRange();

        // Reset tracking when the items collection changes (e.g. navigation to a new folder).
        if (Items.Count != _prevItemsCount)
        {
            _placeholderCts?.Cancel();
            _earliestPlaceholderIndex = -1;
            _prevItemsCount = Items.Count;
            _prevEndIndex = range.EndIndex;
            return range;
        }

        // When the end index does not increase (scroll up, jump to selection, first call)
        // just update the tracking value and return the full range.
        if (_prevEndIndex < 0 || range.EndIndex <= _prevEndIndex)
        {
            _prevEndIndex = range.EndIndex;
            return range;
        }

        // For a large viewport increase (e.g. switching to full screen), realize all new items
        // instantly as lightweight placeholders, then progressively restore real content at
        // Background priority so the UI stays responsive.
        int newItems = range.EndIndex - _prevEndIndex;
        int threshold = 3 * Math.Max(ItemsPerRowCount, 1);
        if (newItems > threshold)
        {
            int firstNewIndex = Math.Max(_prevEndIndex + 1, range.StartIndex);
            _prevEndIndex = range.EndIndex;
            BeginPlaceholderTransition(firstNewIndex, range.EndIndex);
            return range;
        }

        _prevEndIndex = range.EndIndex;
        return range;
    }

    private void BeginPlaceholderTransition(int fromIndex, int toIndex)
    {
        _placeholderCts?.Cancel();
        _placeholderCts = new CancellationTokenSource();
        var token = _placeholderCts.Token;

        // If a previous restore was cancelled mid-way, start from the earliest
        // item that was set as a placeholder but never restored.
        int restoreFrom = _earliestPlaceholderIndex >= 0
            ? Math.Min(_earliestPlaceholderIndex, fromIndex)
            : fromIndex;
        _earliestPlaceholderIndex = restoreFrom;

        for (int i = fromIndex; i <= toIndex && i < Items.Count; i++)
        {
            if (Items[i] is FileClass file)
                file.IsIconPlaceholder = true;
        }

        // Items up to inViewEnd are inside the visible viewport and are restored
        // first (larger batch).  Items beyond are buffer/off-screen and follow.
        int inViewEnd = Math.Min(restoreFrom + ItemsInView - 1, toIndex);
        Dispatcher.InvokeAsync(() => ProgressivelyRestore(restoreFrom, inViewEnd, toIndex, token), DispatcherPriority.Background);
    }

    private void ProgressivelyRestore(int index, int inViewEnd, int toIndex, CancellationToken token)
    {
        if (token.IsCancellationRequested || index > toIndex || index >= Items.Count)
        {
            if (!token.IsCancellationRequested)
                _earliestPlaceholderIndex = -1;
            return;
        }

        int batchSize = index <= inViewEnd ? 5 : 2;
        int end = Math.Min(index + batchSize - 1, Math.Min(toIndex, Items.Count - 1));

        for (int i = index; i <= end; i++)
        {
            if (Items[i] is FileClass file)
                file.IsIconPlaceholder = false;
        }

        if (end < toIndex && end < Items.Count - 1)
        {
            Dispatcher.InvokeAsync(
                () => ProgressivelyRestore(end + 1, inViewEnd, toIndex, token),
                DispatcherPriority.Background);
        }
        else
        {
            _earliestPlaceholderIndex = -1;
        }
    }
}

