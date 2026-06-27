using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;
using Wpf.Ui.Controls;

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

    private object? iconContent;
    public object? IconContent
    {
        get => iconContent;
        protected set => Set(ref iconContent, value);
    }

    public int IconSize { get; protected set; }

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

    public bool MirrorInRTL { get; }

    protected ActionBase(FileAction action,
                         BaseIcon? icon = null,
                         StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                         AnimationSource animationSource = AnimationSource.Command,
                         FileAction altAction = null,
                         ObservableProperty<bool> isVisible = null,
                         bool mirrorInRTL = false)
    {
        Action = action;
        IconSize = icon is null ? 16 : (int)icon.Size;
        IconContent = icon?.IconContent;
        Animation = animation;
        ActionAnimationSource = animationSource;
        AltAction = altAction;
        MirrorInRTL = mirrorInRTL;

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

        Action.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(FileAction.Description))
                OnPropertyChanged(nameof(Tooltip));
        };
    }

    protected ActionBase()
    { }

    private void OnExecute_PropertyChanged(object sender, PropertyChangedEventArgs<bool> e)
    {
        ActivateAnimation = true;
        Task.Delay(200).ContinueWith((t) => ActivateAnimation = false);
    }
}

public abstract class ActionMenu : ActionBase
{
    public IEnumerable<SubMenu> Children { get; set; }

    public bool IsChevronVisible { get; set; }

    protected ActionMenu(FileAction fileAction,
                         BaseIcon? icon = null,
                         IEnumerable<SubMenu> children = null,
                         StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                         AnimationSource animationSource = AnimationSource.Command,
                         FileAction altAction = null,
                         ObservableProperty<bool> isVisible = null,
                         bool mirrorInRTL = false,
                         bool isChevronVisible = false)
        : base(fileAction, icon, animation, animationSource, altAction, isVisible, mirrorInRTL)
    {
        Children = children;
        IsChevronVisible = isChevronVisible;
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
            if (Set(ref altText, value) && ActionAnimationSource is AnimationSource.External)
            {
                ActivateAnimation = true;
                Task.Delay(Animation is StyleHelper.ContentAnimation.Pulsate ? 500 : 200).ContinueWith((t) => ActivateAnimation = false);
            }
        }
    }

    public bool IsTooltipVisible { get; }

    public AltTextMenu(FileAction fileAction,
                       BaseIcon icon,
                       string altText = null,
                       IEnumerable<SubMenu> children = null,
                       StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                       AnimationSource animationSource = AnimationSource.Command,
                       bool isTooltipVisible = true,
                       FileAction altAction = null,
                       ObservableProperty<bool> isVisible = null)
        : base(fileAction, icon, children, animation, animationSource, altAction, isVisible: isVisible)
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
                              BaseIcon icon,
                              StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                              AnimationSource animationSource = AnimationSource.Command,
                              FileAction altAction = null)
        : base(fileAction, icon, altText, animation: animation, animationSource: animationSource, altAction: altAction)
    {
        altText.PropertyChanged += (sender, e) => AltText = e.NewValue;
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
                    BaseIcon icon,
                    ObservableProperty<IEnumerable<SubMenu>> children,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    bool isChevronVisible = false)
        : base(fileAction, icon, animation: animation, isChevronVisible: isChevronVisible)
    {
        children?.PropertyChanged += (sender, e) =>
        {
            Children = children.Value;

            OnPropertyChanged(nameof(Children));
        };
    }

    public IconMenu(FileAction fileAction,
                    BaseIcon icon,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    ObservableProperty<bool> selectionBar = null,
                    IEnumerable<SubMenu> children = null,
                    FileAction altAction = null,
                    ObservableProperty<bool> isVisible = null,
                    bool mirrorInRTL = false)
        : base(fileAction, icon, children, animation, altAction: altAction, isVisible: isVisible, mirrorInRTL: mirrorInRTL)
    {
        if (selectionBar is not null)
        {
            IsSelectionBarVisible = selectionBar.Value;
            selectionBar.PropertyChanged += (sender, e) => IsSelectionBarVisible = e.NewValue;
        }
    }

    public IconMenu(IEnumerable<SubMenu> children,
                    string description,
                    BaseIcon icon,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    ObservableProperty<bool> selectionBar = null,
                    FileAction altAction = null,
                    ObservableProperty<bool> isVisible = null)
        : this(new(FileAction.FileActionType.More,
                   () => children.Any(c => c is not SubMenuSeparator && c.Action.Command.IsEnabled),
                   () => { },
                   description), icon, animation, selectionBar, children, altAction, isVisible)
    { }
}

public class CompoundIconMenu : ActionMenu
{
    public bool IsNameDisplayed { get; }

    public CompoundIconMenu(FileAction fileAction,
                       BaseIcon icon,
                       IEnumerable<SubMenu> children = null,
                       StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                       FileAction altAction = null,
                       ObservableProperty<bool> isVisible = null,
                       bool isNameDisplayed = false,
                       bool isChevronVisible = false)
        : base(fileAction, icon, children, animation, altAction: altAction, isVisible: isVisible, isChevronVisible: isChevronVisible)
    {
        IsNameDisplayed = isNameDisplayed;
    }
}

public class TextMenu : ActionMenu
{
    public bool IsLast { get; set; } = false;

    public ControlAppearance Appearance { get; set; } = ControlAppearance.Secondary;

    public FlowDirection FlowDirection => TextHelper.ContainsRtl(Action.Description)
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    public TextMenu(FileAction fileAction)
        : base(fileAction, null)
    { }
}

public class SubMenu : ActionMenu
{
    public SubMenu()
    { }

    public SubMenu(FileAction fileAction, BaseIcon? icon = null, IEnumerable<SubMenu> children = null, FileAction altAction = null, ObservableProperty<bool> isVisible = null)
        : base(fileAction, icon, children, altAction: altAction, isVisible: isVisible)
    { }
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
        : base(new(FileAction.FileActionType.None, () => true, dummyAction, Strings.Resources.S_MENU_EMPTY), new("\uF141", 16))
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
        : base(new(FileAction.FileActionType.None, canExecute, () => { }))
    {
        externalVisibility = canExecute is null;
    }

    public SubMenuSeparator(ObservableProperty<bool> isVisible)
        : base(new(FileAction.FileActionType.None, () => isVisible.Value, () => { }), isVisible: isVisible)
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
                            ObservableProperty<BaseIcon> icon,
                            ObservableProperty<bool> isChecked = null,
                            StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                            Brush checkBackground = null,
                            IEnumerable<SubMenu> children = null,
                            ObservableProperty<bool> isVisible = null,
                            bool isCheckable = true)
        : base(action, icon.Value, animation, children: children, isVisible: isVisible)
    {
        CheckBackground = checkBackground;
        observableIsChecked = isChecked;
        IsCheckable = isCheckable;

        IsChecked = isChecked;
        observableIsChecked.PropertyChanged += (sender, e) => IsChecked = e.NewValue;

        icon.PropertyChanged += (sender, e) =>
        {
            IconContent = icon.Value?.IconContent;
            if (icon.Value is not null)
            {
                IconSize = (int)icon.Value.Size;
                OnPropertyChanged(nameof(IconSize));
            }
        };
    }
}
