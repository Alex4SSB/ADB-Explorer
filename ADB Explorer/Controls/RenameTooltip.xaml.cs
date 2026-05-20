using ADB_Explorer.Models;
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

    private void Position(FrameworkElement anchor, bool centerHorizontally)
    {
        if (!Data.FileActions.IsExplorerEditing)
            return;

        var tooltipWidth = TooltipBorder.ActualWidth;
        var tooltipHeight = TooltipBorder.ActualHeight;
        var canvasWidth = OverlayCanvas.ActualWidth;
        var canvasHeight = OverlayCanvas.ActualHeight;

        var anchorTopLeft = anchor.TranslatePoint(new Point(0, 0), OverlayCanvas);
        var anchorHeight = anchor.ActualHeight;
        var anchorWidth = anchor.ActualWidth;

        // Vertical: prefer above, fall back to below
        var aboveY = anchorTopLeft.Y - tooltipHeight - 8;
        var belowY = anchorTopLeft.Y + anchorHeight + 8;
        var top = aboveY >= 0 ? aboveY : belowY;
        top = Math.Max(0, Math.Min(top, canvasHeight - tooltipHeight));

        // Horizontal: left-align with anchor (folder view) or center on it (icon view), clamp within canvas
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

        // Slide-in animation: come from below when shown above anchor, from above when shown below
        bool placedAbove = aboveY >= 0 && top == Math.Max(0, Math.Min(aboveY, canvasHeight - tooltipHeight));
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
