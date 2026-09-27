using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Helpers;

/// <summary>Fades and shrinks a navigation pane item in and out, instead of switching its visibility.</summary>
internal static class PaneItemAnimation
{
    private static readonly Duration AnimationDuration = new(TimeSpan.FromMilliseconds(150));

    /// <summary>Null until the first <see cref="SetShown"/>, which applies its state without animating.</summary>
    private static readonly DependencyProperty IsShownProperty =
        DependencyProperty.RegisterAttached("IsShown", typeof(bool?), typeof(PaneItemAnimation), new PropertyMetadata(null));

    internal static void SetShown(NavigationViewItem item, bool shown)
    {
        var previous = (bool?)item.GetValue(IsShownProperty);
        if (previous == shown)
            return;

        item.SetValue(IsShownProperty, shown);

        var target = shown ? 1.0 : 0.0;
        if (previous is null)
        {
            item.LayoutTransform = new ScaleTransform(1, target);
            item.Opacity = target;
            item.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var scale = (ScaleTransform)item.LayoutTransform;
        var scaleFrom = scale.ScaleY;
        var opacityFrom = item.Opacity;

        item.Visibility = Visibility.Visible;
        scale.ScaleY = target;
        item.Opacity = target;

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var scaleAnimation = new DoubleAnimation(scaleFrom, target, AnimationDuration) { EasingFunction = ease };
        var opacityAnimation = new DoubleAnimation(opacityFrom, target, AnimationDuration) { EasingFunction = ease };

        // Only the latest request may collapse the item - a reversed animation must leave it visible.
        scaleAnimation.Completed += (_, _) =>
        {
            if ((bool?)item.GetValue(IsShownProperty) is false)
                item.Visibility = Visibility.Collapsed;
        };

        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        item.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
    }
}
