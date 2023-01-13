using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal abstract class ActionMenu : ViewModelBase
{
    public enum AnimationSource
    {
        None,
        Click,
        Command,
        External,
    }

    #region Full properties

    public FileAction Action { get; }

    public string Icon { get; }

    public int IconSize { get; }
    
    public IEnumerable<SubMenu> Children { get; }
    
    public StyleHelper.ContentAnimation Animation { get; }

    public AnimationSource ActionAnimationSource { get; }

    private bool activateAnimation = false;
    public bool ActivateAnimation
    {
        get => activateAnimation;
        set => Set(ref activateAnimation, value);
    }

    #endregion

    public bool AnimateOnClick => ActionAnimationSource is AnimationSource.Click;

    public string Tooltip => $"{Action.Description}{(string.IsNullOrEmpty(Action.GestureString) ? "" : $" ({Action.GestureString})")}";

    protected ActionMenu(FileAction fileAction, string icon, IEnumerable<SubMenu> children = null, StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None, int iconSize = 18, AnimationSource animationSource = AnimationSource.Command)
    {
        if (this is not SubMenu)
            StyleHelper.VerifyIcon(ref icon);

        Icon = icon;
        Animation = animation;
        IconSize = iconSize;
        Children = children;
        ActionAnimationSource = animationSource;
        Action = fileAction;

        if (animationSource is AnimationSource.Command)
        {
            ((CommandHandler)Action.Command.Command).OnExecute.PropertyChanged += (object sender, PropertyChangedEventArgs<bool> e) =>
            {
                if (!Data.Settings.IsAnimated)
                    return;

                ActivateAnimation = true;
                Task.Delay(200).ContinueWith((t) => ActivateAnimation = false);
            };
        }
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

    public AltTextMenu(FileAction fileAction, string icon, string altText = null, IEnumerable<SubMenu> children = null, StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None, int iconSize = 18, AnimationSource animationSource = AnimationSource.Command)
        : base(fileAction, icon, children, animation, iconSize, animationSource)
    {
        if (children is not null && children.Any())
            altText = fileAction.Description;

        AltText = altText;
    }
}

internal class DynamicAltTextMenu : AltTextMenu
{
    public DynamicAltTextMenu(FileAction fileAction, ObservableProperty<string> altText, string icon, StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None, int iconSize = 20, AnimationSource animationSource = AnimationSource.Command)
        : base(fileAction, icon, altText, animation: animation, iconSize: iconSize, animationSource: animationSource)
    {
        altText.PropertyChanged += (object sender, PropertyChangedEventArgs<string> e) => AltText = e.NewValue;
    }
}

internal class IconMenu : ActionMenu
{
    private bool isSelectionBarVisible = false;
    public bool IsSelectionBarVisible
    {
        get => isSelectionBarVisible;
        set => Set(ref isSelectionBarVisible, value);
    }

    public IconMenu(FileAction fileAction, string icon, StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None, int iconSize = 16, ObservableProperty<bool> selectionBar = null, IEnumerable<SubMenu> children = null)
        : base(fileAction, icon, children, animation, iconSize: iconSize)
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
    public AnimatedNotifyMenu(FileAction fileAction, ObservableProperty<string> altText, string icon, StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.Pulsate, int iconSize = 18, AnimationSource animationSource = AnimationSource.External)
        : base(fileAction, altText, icon, animation, iconSize, animationSource)
    { }
}

internal class AltIconMenu : ActionMenu
{
    public string AltIcon { get; }

    public int AltIconSize { get; }

    public AltIconMenu(FileAction fileAction, string icon, string altIcon, int iconSize = 18, int altIconSize = 14, IEnumerable<SubMenu> children = null, StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None)
        : base(fileAction, icon, children, animation, iconSize)
    {
        StyleHelper.VerifyIcon(ref altIcon);

        AltIcon = altIcon;
        AltIconSize = altIconSize;
    }
}

internal class SubMenu : ActionMenu
{
    public bool IconIsText { get; }

    protected SubMenu()
    { }

    public SubMenu(FileAction fileAction, string icon, IEnumerable<SubMenu> children = null, int iconSize = 16)
        : base(fileAction, icon, children, iconSize: iconSize)
    {
        IconIsText = !StyleHelper.IsFontIcon(icon);
    }
}

internal class AltIconSubMenu : SubMenu
{
    public string AltIcon { get; }

    public int AltIconSize { get; }

    public AltIconSubMenu(FileAction fileAction, string icon, string altIcon, IEnumerable<SubMenu> children = null, int iconSize = 16, int altIconSize = 12)
        : base(fileAction, icon, children, iconSize: iconSize)
    {
        StyleHelper.VerifyIcon(ref altIcon);

        AltIcon = altIcon;
        AltIconSize = altIconSize;
    }
}

internal class SubMenuSeparator : SubMenu
{
    public bool HideSeparator => !Action.Command.IsEnabled;

    public SubMenuSeparator(Func<bool> canExecute)
        : base(new(FileAction.FileActionType.None, canExecute, () => { }), "")
    { }
}
