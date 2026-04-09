using ADB_Explorer.Helpers;

namespace ADB_Explorer.Controls;

public class AdbListView : Wpf.Ui.Controls.ListView
{
    public bool IsItemScrolling
    {
        get => (bool)GetValue(IsItemScrollingProperty);
        set
        {
            var oldValue = IsItemScrolling;
            SetValue(IsItemScrollingProperty, value);

            if (oldValue != value)
            {
                if (value)
                    PreviewMouseWheel += GridListView_PreviewMouseWheel;
                else
                    PreviewMouseWheel -= GridListView_PreviewMouseWheel;
            }
        }
    }

    public static readonly DependencyProperty IsItemScrollingProperty =
            DependencyProperty.Register(
                    nameof(IsItemScrolling),
                    typeof(bool),
                    typeof(AdbListView));

    public AdbListView()
    {
        PreviewMouseWheel += GridListView_PreviewMouseWheel;
    }

    public ScrollViewer ScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(this);
            return field;
        }
    }

    public AdbVirtualizingWrapPanel WrapPanel
    {
        get
        {
            field ??= StyleHelper.FindDescendant<AdbVirtualizingWrapPanel>(ScrollViewer);
            return field;
        }
    }

    public int ItemsPerRow => WrapPanel.ItemsPerRowCount;

    public double ItemHeight => WrapPanel.ChildSize.Height;

    public int ItemsInView => WrapPanel?.ItemsInView ?? 0;

    private void GridListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var offset = ScrollViewer.VerticalOffset - Math.Sign(e.Delta) * ItemHeight;
        ScrollViewer.ScrollToVerticalOffset(offset);
        e.Handled = true;
    }
}
