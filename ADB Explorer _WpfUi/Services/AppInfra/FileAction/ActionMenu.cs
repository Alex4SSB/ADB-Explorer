using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public interface IMenuItem : INotifyPropertyChanged
{

}

public abstract class ActionBase : ViewModelBase, IMenuItem
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
        if (!string.IsNullOrEmpty(icon))
            StyleHelper.VerifyIcon(icon);

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
            isVisible.PropertyChanged += (sender, e) => IsVisible = e.NewValue;
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

public abstract class ActionMenu : ActionBase
{
    public IEnumerable<SubMenu> Children { get; set; }

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

public class MenuSeparator : ViewModelBase, IMenuItem
{ }

public class AltTextMenu : ActionMenu
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

public class DynamicAltTextMenu : AltTextMenu
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
        altText.PropertyChanged += (sender, e) => AltText = e.NewValue;
    }
}

public class AltObjectMenu : ActionMenu
{
    public bool IsContentDropDown { get; }
    public AltObjectMenu(FileAction fileAction, string icon, FileAction altAction = null, IEnumerable<SubMenu> children = null, bool isContentDropDown = false)
        : base(fileAction, icon, altAction: altAction, children: children)
    {
        IsContentDropDown = isContentDropDown;
    }
}

public class IconMenu : ActionMenu
{
    private bool isSelectionBarVisible = false;
    public bool IsSelectionBarVisible
    {
        get => isSelectionBarVisible;
        set => Set(ref isSelectionBarVisible, value);
    }

    //public new ObservableList<SubMenu> Children { get; }

    public IconMenu(FileAction fileAction,
                    string icon,
                    ObservableProperty<IEnumerable<SubMenu>> children,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    int iconSize = 16)
        : base(fileAction, icon, animation: animation, iconSize: iconSize)
    {
        if (children is not null)
        {
            children.PropertyChanged += (sender, e) =>
            {
                Children = children.Value;

                OnPropertyChanged(nameof(Children));
            };
        }
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
            selectionBar.PropertyChanged += (sender, e) => IsSelectionBarVisible = e.NewValue;
        }
    }

    public IconMenu(IEnumerable<SubMenu> children,
                    string description,
                    string icon,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    int iconSize = 16,
                    ObservableProperty<bool> selectionBar = null,
                    FileAction altAction = null,
                    ObservableProperty<bool> isVisible = null)
        : this(new(FileAction.FileActionType.More,
                   () => children.Any(c => c is not SubMenuSeparator && c.Action.Command.IsEnabled),
                   () => { },
                   description), icon, animation, iconSize, selectionBar, children, altAction, isVisible)
    { }
}

public class AnimatedNotifyMenu : DynamicAltTextMenu
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

public class CompoundIconMenu : ActionMenu
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

public class TextMenu : ActionMenu
{
    public bool IsLast { get; set; } = false;

    public TextMenu(FileAction fileAction)
        : base(fileAction, null)
    { }
}

public class SubMenu : ActionMenu
{
    public SubMenu()
    { }

    public SubMenu(FileAction fileAction, string icon, IEnumerable<SubMenu> children = null, int iconSize = 16, FileAction altAction = null, ObservableProperty<bool> isVisible = null)
        : base(fileAction, icon, children, iconSize: iconSize, altAction: altAction, isVisible: isVisible)
    { }
}

public class GeneralSubMenu : SubMenu
{
    public object Content { get; }

    public bool IsEnabled { get; private set; } = true;

    public bool IsDropDown { get; }

    public GeneralSubMenu(object content, bool isDropDown = false)
        : base(new(FileAction.FileActionType.None, () => true, () => { }), null)
    {
        Content = content;
        IsDropDown = isDropDown;
        
        if (content is CheckBox cb)
        {
            cb.IsEnabledChanged += (sender, e) => IsEnabled = cb.IsEnabled;
        }
    }
}

public class CompoundIconSubMenu : SubMenu
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

public class DummySubMenu : SubMenu
{
    // This is an easter egg.
    // Do not translate it.
    private static readonly Action dummyAction = () =>
        DialogService.ShowMessage("An SSL error has occurred and a secure connection to\nthe server cannot be made.",
                                  "SHAKESPEARE QUOTE OF THE DAY",
                                  DialogService.DialogIcon.Informational);
    
    private bool? isEnabled = null;
    public bool? IsEnabled
    {
        get => isEnabled;
        set => Set(ref isEnabled, value);
    }

    public DummySubMenu()
        : base(new(FileAction.FileActionType.None, () => true, dummyAction, Strings.Resources.S_MENU_EMPTY), "\uF141")
    { }
}

public class SubMenuSeparator : SubMenu
{
    private readonly bool externalVisibility;

    public bool HideSeparator => externalVisibility ? !IsEnabled : !Action.Command.IsEnabled;

    private bool isEnabled = true;
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (Set(ref isEnabled, value))
                OnPropertyChanged(nameof(HideSeparator));
        }
    }

    public SubMenuSeparator(Func<bool> canExecute = null)
        : base(new(FileAction.FileActionType.None, canExecute, () => { }), "")
    {
        externalVisibility = canExecute is null;
    }

    public SubMenuSeparator(ObservableProperty<bool> isVisible)
        : base(new(FileAction.FileActionType.None, () => isVisible.Value, () => { }), "", isVisible: isVisible)
    { }
}

public class DualActionButton : IconMenu
{
    private bool isChecked = false;
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (observableIsChecked is null)
            {
                Set(ref isChecked, true);
                return;
            }

            if (Set(ref isChecked, value))
                observableIsChecked.Value = value;
        }
    }

    public bool IsCheckable { get; } = true;

    private readonly ObservableProperty<bool> observableIsChecked;

    public Brush CheckBackground { get; }

    /// <summary>
    /// Toggle Button / Menu Item with modifiable background and dynamic icon
    /// </summary>
    public DualActionButton(FileAction action,
                            ObservableProperty<string> icon,
                            ObservableProperty<bool> isChecked = null,
                            int iconSize = 20,
                            StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                            Brush checkBackground = null,
                            IEnumerable<SubMenu> children = null,
                            ObservableProperty<bool> isVisible = null,
                            bool isCheckable = true)
        : base(action, icon, animation, iconSize, children: children, isVisible: isVisible)
    {
        CheckBackground = checkBackground;
        observableIsChecked = isChecked;
        IsCheckable = isCheckable;

        IsChecked = isChecked;
        observableIsChecked.PropertyChanged += (sender, e) => IsChecked = e.NewValue;

        icon.PropertyChanged += (sender, e) => Icon = icon;
    }

    /// <summary>
    /// Accent Button / Menu Item with modifiable background
    /// </summary>
    public DualActionButton(FileAction action,
                            string icon,
                            StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                            int iconSize = 20,
                            Brush checkBackground = null,
                            ObservableProperty<bool> isVisible = null)
        : base(action, icon, animation, iconSize, isVisible: isVisible)
    {
        IsChecked = true;
        CheckBackground = checkBackground;
    }
}

public class CompoundDualAction : DualActionButton
{
    public UserControl CompoundIcon { get; }

    /// <summary>
    /// Accent Button / Menu Item with modifiable background
    /// </summary>
    public CompoundDualAction(FileAction action,
                              UserControl icon,
                              StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                              Brush checkBackground = null,
                              ObservableProperty<bool> isVisible = null)
        : base(action, null, animation, checkBackground: checkBackground, isVisible: isVisible)
    {
        CompoundIcon = icon;
    }
}
