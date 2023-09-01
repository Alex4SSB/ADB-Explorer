using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal abstract class ActionBase : ViewModelBase
{
    public enum AnimationSource
    {
        None,
        Click,
        Command,
        External,
    }

    public FileAction Action { get; }

    public FileAction AltAction { get; }

    private string icon = "";
    public string Icon
    {
        get => icon;
        protected set => Set(ref icon, value);
    }

    public int IconSize { get; }

    public StyleHelper.ContentAnimation Animation { get; }

    public AnimationSource ActionAnimationSource { get; }

    private bool activateAnimation = false;
    public bool ActivateAnimation
    {
        get => activateAnimation;
        set => Set(ref activateAnimation, value);
    }

    private bool isVisible = true;
    public bool IsVisible
    {
        get => isVisible;
        private set => Set(ref isVisible, value);
    }

    public string Tooltip => $"{Action.Description}{(string.IsNullOrEmpty(Action.GestureString) ? "" : $" ({Action.GestureString})")}";

    public bool AnimateOnClick => ActionAnimationSource is AnimationSource.Click;

    protected ActionBase(FileAction action,
                         string icon,
                         int iconSize,
                         StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                         AnimationSource animationSource = AnimationSource.Command,
                         FileAction altAction = null,
                         ObservableProperty<bool> isVisible = null)
    {
        if (this is not SubMenu)
            StyleHelper.VerifyIcon(ref icon);

        Action = action;
        Icon = icon;
        IconSize = iconSize;
        Animation = animation;
        ActionAnimationSource = animationSource;
        AltAction = altAction;
        
        if (animationSource is AnimationSource.Command)
        {
            ((CommandHandler)Action.Command.Command).OnExecute.PropertyChanged += OnExecute_PropertyChanged;

            if (AltAction is not null)
                ((CommandHandler)AltAction.Command.Command).OnExecute.PropertyChanged += OnExecute_PropertyChanged;
        }

        if (isVisible is not null)
        {
            IsVisible = isVisible;
            isVisible.PropertyChanged += (object sender, PropertyChangedEventArgs<bool> e) => IsVisible = e.NewValue;
        }
    }

    protected ActionBase()
    { }

    private void OnExecute_PropertyChanged(object sender, PropertyChangedEventArgs<bool> e)
    {
        if (!Data.Settings.IsAnimated)
            return;

        ActivateAnimation = true;
        Task.Delay(200).ContinueWith((t) => ActivateAnimation = false);
    }
}

internal abstract class ActionMenu : ActionBase
{
    public IEnumerable<SubMenu> Children { get; }

    protected ActionMenu(FileAction fileAction,
                         string icon,
                         IEnumerable<SubMenu> children = null,
                         StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                         int iconSize = 18,
                         AnimationSource animationSource = AnimationSource.Command,
                         FileAction altAction = null,
                         ObservableProperty<bool> isVisible = null)
        : base(fileAction, icon, iconSize, animation, animationSource, altAction, isVisible)
    {
        Children = children;
    }

    protected ActionMenu()
    { }
}

internal class MenuSeparator : ActionMenu
{ }

internal class AltTextMenu : ActionMenu
{
    protected string altText = "";
    public string AltText
    {
        get => altText;
        set
        {
            if (Set(ref altText, value) && Data.Settings.IsAnimated && ActionAnimationSource is AnimationSource.External)
            {
                ActivateAnimation = true;
                Task.Delay(Animation is StyleHelper.ContentAnimation.Pulsate ? 500 : 200).ContinueWith((t) => ActivateAnimation = false);
            }
        }
    }

    public bool IsTooltipVisible { get; }

    public AltTextMenu(FileAction fileAction,
                       string icon,
                       string altText = null,
                       IEnumerable<SubMenu> children = null,
                       StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                       int iconSize = 18,
                       AnimationSource animationSource = AnimationSource.Command,
                       bool isTooltipVisible = true,
                       FileAction altAction = null,
                       ObservableProperty<bool> isVisible = null)
        : base(fileAction, icon, children, animation, iconSize, animationSource, altAction, isVisible: isVisible)
    {
        if (children is not null && children.Any())
            altText = fileAction.Description;

        AltText = altText;
        IsTooltipVisible = isTooltipVisible;
    }
}

internal class DynamicAltTextMenu : AltTextMenu
{
    public DynamicAltTextMenu(FileAction fileAction,
                              ObservableProperty<string> altText,
                              string icon,
                              StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                              int iconSize = 20,
                              AnimationSource animationSource = AnimationSource.Command,
                              FileAction altAction = null)
        : base(fileAction, icon, altText, animation: animation, iconSize: iconSize, animationSource: animationSource, altAction: altAction)
    {
        altText.PropertyChanged += (object sender, PropertyChangedEventArgs<string> e) => AltText = e.NewValue;
    }
}

internal class AltObjectMenu : ActionMenu
{
    public AltObjectMenu(FileAction fileAction, string icon, FileAction altAction = null)
        : base(fileAction, icon, altAction: altAction)
    { }
}

internal class IconMenu : ActionMenu
{
    private bool isSelectionBarVisible = false;
    public bool IsSelectionBarVisible
    {
        get => isSelectionBarVisible;
        set => Set(ref isSelectionBarVisible, value);
    }

    public IconMenu(FileAction fileAction,
                    string icon,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    int iconSize = 16,
                    ObservableProperty<bool> selectionBar = null,
                    IEnumerable<SubMenu> children = null,
                    FileAction altAction = null,
                    ObservableProperty<bool> isVisible = null)
        : base(fileAction, icon, children, animation, iconSize: iconSize, altAction: altAction, isVisible: isVisible)
    {
        if (selectionBar is not null)
        {
            IsSelectionBarVisible = selectionBar.Value;
            selectionBar.PropertyChanged += (object sender, PropertyChangedEventArgs<bool> e) => IsSelectionBarVisible = e.NewValue;
        }
    }
}

internal class AnimatedNotifyMenu : DynamicAltTextMenu
{
    public AnimatedNotifyMenu(FileAction fileAction,
                              ObservableProperty<string> altText,
                              string icon,
                              StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.Pulsate,
                              int iconSize = 18,
                              AnimationSource animationSource = AnimationSource.External,
                              FileAction altAction = null)
        : base(fileAction, altText, icon, animation, iconSize, animationSource, altAction)
    { }
}

internal class CompoundIconMenu : ActionMenu
{
    public UserControl CompoundIcon { get; }

    public CompoundIconMenu(FileAction fileAction,
                       UserControl icon,
                       IEnumerable<SubMenu> children = null,
                       StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                       FileAction altAction = null,
                       ObservableProperty<bool> isVisible = null)
        : base(fileAction, null, children, animation, altAction: altAction, isVisible: isVisible)
    {
        CompoundIcon = icon;
    }
}

internal class SubMenu : ActionMenu
{
    public bool IconIsText { get; }

    protected SubMenu()
    { }

    public SubMenu(FileAction fileAction, string icon, IEnumerable<SubMenu> children = null, int iconSize = 16, FileAction altAction = null)
        : base(fileAction, icon, children, iconSize: iconSize, altAction: altAction)
    {
        IconIsText = !StyleHelper.IsFontIcon(icon);
    }
}

internal class CompoundIconSubMenu : SubMenu
{
    public UserControl CompoundIcon { get; }

    public CompoundIconSubMenu(FileAction fileAction,
                          UserControl icon,
                          IEnumerable<SubMenu> children = null,
                          FileAction altAction = null)
        : base(fileAction, null, children, altAction: altAction)
    {
        CompoundIcon = icon;
    }
}

internal class SubMenuSeparator : SubMenu
{
    public bool HideSeparator => !Action.Command.IsEnabled;

    public SubMenuSeparator(Func<bool> canExecute)
        : base(new(FileAction.FileActionType.None, canExecute, () => { }), "")
    { }
}

internal class ActionButton : ActionBase
{
    public ActionButton(FileAction action,
                        string icon,
                        int iconSize = 16,
                        StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                        FileAction altAction = null,
                        ObservableProperty<bool> isVisible = null)
        : base(action, icon, iconSize, animation, altAction: altAction, isVisible: isVisible)
    { }
}

internal class ActionAccentButton : ActionButton
{
    public ActionAccentButton(FileAction action,
                              string icon,
                              int iconSize = 16,
                              StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                              FileAction altAction = null,
                              ObservableProperty<bool> isVisible = null)
        : base(action, icon, iconSize, animation, altAction: altAction, isVisible: isVisible)
    { }
}

internal class DualActionButton : IconMenu
{
    private bool isChecked = false;
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (Set(ref isChecked, value))
                observableIsChecked.Value = value;
        }
    }

    private readonly ObservableProperty<bool> observableIsChecked;

    public Brush CheckBackground { get; }

    public DualActionButton(FileAction action,
                            ObservableProperty<string> icon,
                            ObservableProperty<bool> isChecked,
                            int iconSize = 20,
                            StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                            Brush checkBackground = null,
                            ObservableProperty<bool> isVisible = null)
        : base(action, icon, animation, iconSize, isVisible: isVisible)
    {
        icon.PropertyChanged += (object sender, PropertyChangedEventArgs<string> e) => Icon = icon;
        observableIsChecked = isChecked;
        IsChecked = isChecked;
        CheckBackground = checkBackground;
    }
}
