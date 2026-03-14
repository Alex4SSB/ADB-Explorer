using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

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

    private ScrollViewer? _scrollViewer;
    public ScrollViewer ScrollViewer
    {
        get
        {
            _scrollViewer ??= StyleHelper.FindDescendant<ScrollViewer>(this);
            return _scrollViewer;
        }
    }

    private double _itemHeight = 0;
    public double ItemHeight
    {
        get
        {
            // Update whenever possible
            if (ItemContainerGenerator.ContainerFromIndex(0) is ListViewItem item && item.ActualHeight > 0)
                _itemHeight = item.ActualHeight;

            return _itemHeight;
        }
    }

    private void GridListView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var dpiOffset = Data.RuntimeSettings.MainWindowScalingFactor * -2 + 2;

        double itemHeight = ItemHeight + 10 - dpiOffset;
        var offset = ScrollViewer.VerticalOffset - Math.Sign(e.Delta) * itemHeight;
        ScrollViewer.ScrollToVerticalOffset(Math.Floor(offset / itemHeight) * itemHeight);
        e.Handled = true;
    }
}
