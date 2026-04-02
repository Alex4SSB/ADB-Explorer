using System.Windows.Media.Animation;

namespace ADB_Explorer.Controls;

public class AdbMenu : Menu
{
    protected override DependencyObject GetContainerForItemOverride()
        => new AdbMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is AdbMenuItem or Separator;
}

public class AdbMenuItem : Wpf.Ui.Controls.MenuItem
{
    private static readonly Duration Short = new(TimeSpan.FromMilliseconds(100));
    private static readonly Duration Medium = new(TimeSpan.FromMilliseconds(200));
    private static readonly BounceEase BounceOut = new() { Bounces = 2, Bounciness = 4, EasingMode = EasingMode.EaseOut };

    private ContentPresenter _headerPresenter;
    private Border _topLevelBorder;

    protected override DependencyObject GetContainerForItemOverride()
        => new AdbMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item)
        => item is AdbMenuItem or Separator;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _headerPresenter = GetTemplateChild("Header") as ContentPresenter;
        _topLevelBorder = GetTemplateChild("Border") as Border;
        UpdateContentMirror();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == Helpers.StyleHelper.ActivateAnimationProperty && e.NewValue is true)
            PlayAnimation();

        if (e.Property == Helpers.StyleHelper.MirrorContentInRTLProperty)
            UpdateContentMirror();
    }

    protected override void OnClick()
    {
        base.OnClick();

        if (Helpers.StyleHelper.GetAnimateOnClick(this) && Models.Data.Settings.IsAnimated)
            PlayAnimation();
    }

    private void UpdateContentMirror()
    {
        if (_headerPresenter is null)
            return;

        if (Helpers.StyleHelper.GetMirrorContentInRTL(this) && Models.Data.RuntimeSettings.IsRTL)
            _headerPresenter.LayoutTransform = new ScaleTransform(-1, 1);
        else
            _headerPresenter.ClearValue(FrameworkElement.LayoutTransformProperty);
    }

    private void PlayAnimation()
    {
        if (_headerPresenter is null)
            return;

        switch (Helpers.StyleHelper.GetContentAnimation(this))
        {
            case Helpers.StyleHelper.ContentAnimation.Bounce:
                AnimateBounce();
                break;
            case Helpers.StyleHelper.ContentAnimation.RotateCW:
                AnimateRotate(clockwise: true);
                break;
            case Helpers.StyleHelper.ContentAnimation.RotateCCW:
                AnimateRotate(clockwise: false);
                break;
            case Helpers.StyleHelper.ContentAnimation.LeftMarquee:
                AnimateMarquee(dx: -1);
                break;
            case Helpers.StyleHelper.ContentAnimation.RightMarquee:
                AnimateMarquee(dx: 1);
                break;
            case Helpers.StyleHelper.ContentAnimation.UpMarquee:
                AnimateMarquee(dy: -1);
                break;
            case Helpers.StyleHelper.ContentAnimation.DownMarquee:
                AnimateMarquee(dy: 1);
                break;
            case Helpers.StyleHelper.ContentAnimation.Pulsate:
                AnimatePulsate();
                break;
        }
    }

    private void AnimateBounce()
    {
        var transform = new TranslateTransform();
        _headerPresenter.RenderTransform = transform;
        _headerPresenter.RenderTransformOrigin = new Point(0.5, 0.5);
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, -4, Short) { AutoReverse = true, EasingFunction = BounceOut });
    }

    private void AnimateRotate(bool clockwise)
    {
        var transform = new RotateTransform();
        _headerPresenter.RenderTransform = transform;
        _headerPresenter.RenderTransformOrigin = new Point(0.5, 0.5);
        transform.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(clockwise ? 0 : 360, clockwise ? 360 : 0, Medium));
    }

    private void AnimateMarquee(int dx = 0, int dy = 0)
    {
        var transform = new TranslateTransform();
        _headerPresenter.RenderTransform = transform;

        var prop = dx != 0 ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var sign = dx != 0 ? dx : dy;

        var exit = new DoubleAnimation(0, sign * 30, Short);
        exit.Completed += (_, _) =>
            transform.BeginAnimation(prop, new DoubleAnimation(-sign * 30, 0, Short) { EasingFunction = BounceOut });
        transform.BeginAnimation(prop, exit);
    }

    private void AnimatePulsate()
    {
        if (_topLevelBorder is null)
            return;

        var brush = new SolidColorBrush(Colors.Gray) { Opacity = 0 };
        _topLevelBorder.SetCurrentValue(Border.BackgroundProperty, brush);

        var anim = new DoubleAnimation(0, 0.3, new Duration(TimeSpan.FromMilliseconds(150)))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3)
        };
        anim.Completed += (_, _) => _topLevelBorder.ClearValue(Border.BackgroundProperty);
        brush.BeginAnimation(SolidColorBrush.OpacityProperty, anim);
    }
}
