using ADB_Explorer.Models;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for RenameTooltip.xaml
/// </summary>
public partial class RenameTooltip : UserControl
{
    public RenameTooltip()
    {
        InitializeComponent();
    }

    public void Show(FrameworkElement anchor, object dataContext, bool centerHorizontally = false)
    {
        DataContext = dataContext;
        TooltipBorder.Opacity = 0;
        App.SafeBeginInvoke(() => Position(anchor, centerHorizontally), DispatcherPriority.Loaded);
    }

    private void Position(FrameworkElement anchor, bool centerHorizontally, bool isRetry = false)
    {
        if (!Data.FileActions.IsExplorerEditing || anchor is null || !anchor.IsVisible)
            return;

        TooltipBorder.UpdateLayout();
        if (TooltipBorder.ActualHeight <= 0)
            TooltipBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var tooltipWidth = Math.Max(TooltipBorder.ActualWidth, TooltipBorder.DesiredSize.Width);
        var tooltipHeight = Math.Max(TooltipBorder.ActualHeight, TooltipBorder.DesiredSize.Height);
        var canvasWidth = OverlayCanvas.ActualWidth;
        var canvasHeight = OverlayCanvas.ActualHeight;

        var anchorHeight = anchor.ActualHeight;
        var anchorWidth = anchor.ActualWidth;
        if (anchorHeight <= 0 && VisualTreeHelper.GetParent(anchor) is FrameworkElement parent)
            anchorHeight = parent.ActualHeight;

        if (!isRetry && (tooltipHeight <= 0 || canvasHeight <= 0 || anchorHeight <= 0))
        {
            App.SafeBeginInvoke(() => Position(anchor, centerHorizontally, isRetry: true), DispatcherPriority.ContextIdle);
            return;
        }

        if (tooltipHeight <= 0)
            return;

        Point anchorTopLeft;
        try
        {
            anchorTopLeft = anchor.TranslatePoint(new Point(0, 0), OverlayCanvas);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        const double gap = 8;
        var aboveY = anchorTopLeft.Y - tooltipHeight - gap;
        var belowY = anchorTopLeft.Y + anchorHeight + gap;

        // Prefer below the edit box so a new item at the top of the list is not covered.
        bool fitsBelow = canvasHeight <= 0 || belowY + tooltipHeight <= canvasHeight;
        bool fitsAbove = aboveY >= 0;
        bool placedAbove = !fitsBelow && fitsAbove;
        var top = placedAbove ? aboveY : belowY;

        // Clamp to the canvas only when that would not cover the textbox.
        if (canvasHeight > tooltipHeight)
        {
            var clamped = Math.Max(0, Math.Min(top, canvasHeight - tooltipHeight));
            var anchorBottom = anchorTopLeft.Y + anchorHeight;
            bool overlapsAnchor = clamped < anchorBottom && clamped + tooltipHeight > anchorTopLeft.Y;
            if (!overlapsAnchor)
                top = clamped;
        }

        var left = centerHorizontally
            ? anchorTopLeft.X + (anchorWidth - tooltipWidth) / 2
            : anchorTopLeft.X;

        var adjustedLeft = Math.Max(0, Math.Min(left, canvasWidth - tooltipWidth));

        if (adjustedLeft == 0)
            adjustedLeft = 10;
        else if (adjustedLeft != left)
            adjustedLeft -= 10;

        Canvas.SetLeft(TooltipBorder, adjustedLeft);
        Canvas.SetTop(TooltipBorder, top);
        TooltipBorder.Opacity = 1;

        double slideFrom = placedAbove ? tooltipHeight * 0.6 : -tooltipHeight * 0.6;

        TooltipTranslate.Y = slideFrom;
        var anim = new DoubleAnimation
        {
            From = slideFrom,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(167),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        TooltipTranslate.BeginAnimation(TranslateTransform.YProperty, anim);
    }
}
