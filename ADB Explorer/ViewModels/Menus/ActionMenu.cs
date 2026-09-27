using Wpf.Ui.Controls;

namespace ADB_Explorer.ViewModels;

public interface IMenuItem : INotifyPropertyChanged
{

}

public abstract partial class ActionBase : ObservableObject, IMenuItem
{
    public enum AnimationSource
    {
        None,
        Click,
        Command,
        External,
    }

    public FileAction Action { get; }

    public FileAction? AltAction { get; }

    private object? _iconContent;
    public object? IconContent
    {
        get => _iconContent;
        protected set => SetProperty(ref _iconContent, value);
    }

    public int IconSize { get; protected set; }

    public StyleHelper.ContentAnimation Animation { get; }

    public AnimationSource ActionAnimationSource { get; }

    private bool _activateAnimation = false;
    public bool ActivateAnimation
    {
        get => _activateAnimation;
        set => SetProperty(ref _activateAnimation, value);
    }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    [ObservableProperty]
    public partial string? Info { get; protected set; }

    public string Tooltip => $"{Action.Description}{(string.IsNullOrEmpty(Action.GestureTooltip) ? "" : $" ({Action.GestureTooltip})")}";

    public bool AnimateOnClick => ActionAnimationSource is AnimationSource.Click;

    public bool MirrorInRTL { get; }

    protected ActionBase(FileAction action,
                         BaseIcon? icon = null,
                         StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                         AnimationSource animationSource = AnimationSource.Command,
                         FileAction? altAction = null,
                         ObservableProperty<bool>? isVisible = null,
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

    // Unused by any current call site; kept as an extension point for a future derived
    // type that assigns Action by other means. Not null in practice today.
    protected ActionBase()
    {
        Action = null!;
    }

    private void OnExecute_PropertyChanged(object? sender, PropertyChangedEventArgs<bool> e)
    {
        ActivateAnimation = true;
        Task.Delay(200).ContinueWith((t) => ActivateAnimation = false);
    }
}

public abstract class ActionMenu : ActionBase
{
    public IEnumerable<SubMenu>? Children { get; set; }

    public bool IsChevronVisible { get; set; }

    protected ActionMenu(FileAction fileAction,
                         BaseIcon? icon = null,
                         IEnumerable<SubMenu>? children = null,
                         StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                         AnimationSource animationSource = AnimationSource.Command,
                         FileAction? altAction = null,
                         ObservableProperty<bool>? isVisible = null,
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

public class MenuSeparator : ObservableObject, IMenuItem
{ }

public class AltTextMenu : ActionMenu
{
    protected string altText = "";
    public string AltText
    {
        get => altText;
        set
        {
            if (SetProperty(ref altText, value) && ActionAnimationSource is AnimationSource.External)
            {
                ActivateAnimation = true;
                Task.Delay(Animation is StyleHelper.ContentAnimation.Pulsate ? 500 : 200).ContinueWith((t) => ActivateAnimation = false);
            }
        }
    }

    public bool IsTooltipVisible { get; }

    public AltTextMenu(FileAction fileAction,
                       BaseIcon icon,
                       string? altText = null,
                       IEnumerable<SubMenu>? children = null,
                       StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                       AnimationSource animationSource = AnimationSource.Command,
                       bool isTooltipVisible = true,
                       FileAction? altAction = null,
                       ObservableProperty<bool>? isVisible = null)
        : base(fileAction, icon, children, animation, animationSource, altAction, isVisible: isVisible)
    {
        if (children is not null && children.Any())
            altText = fileAction.Description;

        AltText = altText ?? "";
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
                              FileAction? altAction = null,
                              ObservableProperty<bool>? isVisible = null)
        : base(fileAction, icon, altText, animation: animation, animationSource: animationSource, altAction: altAction, isVisible: isVisible)
    {
        altText.PropertyChanged += (sender, e) => AltText = e.NewValue;
    }

    public DynamicAltTextMenu(FileAction fileAction,
                              ObservableProperty<string> altText,
                              ObservableProperty<BaseIcon> dynamicIcon,
                              StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                              AnimationSource animationSource = AnimationSource.Command,
                              FileAction? altAction = null,
                              ObservableProperty<bool>? isVisible = null)
        : base(fileAction, dynamicIcon.Value, altText, animation: animation, animationSource: animationSource, altAction: altAction, isVisible: isVisible)
    {
        altText.PropertyChanged += (sender, e) => AltText = e.NewValue;

        dynamicIcon.PropertyChanged += (_, _) =>
        {
            IconContent = dynamicIcon.Value?.IconContent;
            if (dynamicIcon.Value is not null)
            {
                IconSize = (int)dynamicIcon.Value.Size;
                OnPropertyChanged(nameof(IconSize));
            }
        };
    }
}

public class IconMenu : ActionMenu
{
    private bool _isSelectionBarVisible = false;
    public bool IsSelectionBarVisible
    {
        get => _isSelectionBarVisible;
        set => SetProperty(ref _isSelectionBarVisible, value);
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
                    ObservableProperty<bool>? selectionBar = null,
                    IEnumerable<SubMenu>? children = null,
                    FileAction? altAction = null,
                    ObservableProperty<bool>? isVisible = null,
                    bool mirrorInRTL = false)
        : base(fileAction, icon, children, animation, altAction: altAction, isVisible: isVisible, mirrorInRTL: mirrorInRTL)
    {
        if (selectionBar is not null)
        {
            IsSelectionBarVisible = selectionBar.Value;
            selectionBar.PropertyChanged += (sender, e) => IsSelectionBarVisible = e.NewValue;
        }
    }

    public IconMenu(FileAction fileAction,
                    ObservableProperty<BaseIcon> dynamicIcon,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    IEnumerable<SubMenu>? children = null,
                    FileAction? altAction = null,
                    ObservableProperty<bool>? isVisible = null,
                    bool mirrorInRTL = false)
        : base(fileAction, dynamicIcon.Value, children, animation, altAction: altAction, isVisible: isVisible, mirrorInRTL: mirrorInRTL)
    {
        dynamicIcon.PropertyChanged += (_, _) =>
        {
            IconContent = dynamicIcon.Value?.IconContent;
            if (dynamicIcon.Value is not null)
            {
                IconSize = (int)dynamicIcon.Value.Size;
                OnPropertyChanged(nameof(IconSize));
            }
        };
    }

    public IconMenu(IEnumerable<SubMenu> children,
                    string description,
                    BaseIcon icon,
                    StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                    ObservableProperty<bool>? selectionBar = null,
                    FileAction? altAction = null,
                    ObservableProperty<bool>? isVisible = null)
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
                       IEnumerable<SubMenu>? children = null,
                       StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                       FileAction? altAction = null,
                       ObservableProperty<bool>? isVisible = null,
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

    public SubMenu(FileAction fileAction, BaseIcon? icon = null, IEnumerable<SubMenu>? children = null, FileAction? altAction = null, ObservableProperty<bool>? isVisible = null)
        : base(fileAction, icon, children, altAction: altAction, isVisible: isVisible)
    {
        Info = fileAction.Info;
    }

    public SubMenu(FileAction fileAction, ObservableProperty<BaseIcon> dynamicIcon, IEnumerable<SubMenu>? children = null, FileAction? altAction = null, ObservableProperty<bool>? isVisible = null)
        : base(fileAction, dynamicIcon.Value, children, altAction: altAction, isVisible: isVisible)
    {
        Info = fileAction.Info;

        dynamicIcon.PropertyChanged += (_, _) =>
        {
            IconContent = dynamicIcon.Value?.IconContent;
            if (dynamicIcon.Value is not null)
            {
                IconSize = (int)dynamicIcon.Value.Size;
                OnPropertyChanged(nameof(IconSize));
            }
        };
    }
}

public class DummySubMenu : SubMenu
{
    // This is an easter egg.
    // Do not translate it.
    private static readonly Action _dummyAction = () =>
        DialogService.ShowMessage("An SSL error has occurred and a secure connection to\nthe server cannot be made.",
                                  "SHAKESPEARE QUOTE OF THE DAY",
                                  DialogService.DialogIcon.Informational);
    
    private bool? _isEnabled = null;
    public bool? IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public DummySubMenu()
        : base(new(FileAction.FileActionType.None, () => true, _dummyAction, Strings.Resources.S_MENU_EMPTY), new BaseIcon("\uF141", 16))
    { }
}

public class SubMenuSeparator : SubMenu
{
    private readonly bool _externalVisibility;

    public bool HideSeparator => _externalVisibility ? !IsEnabled : !Action.Command.IsEnabled;

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
                OnPropertyChanged(nameof(HideSeparator));
        }
    }

    public SubMenuSeparator(Func<bool>? canExecute = null)
        : base(new(FileAction.FileActionType.None, canExecute ?? (() => true), () => { }))
    {
        _externalVisibility = canExecute is null;
    }

    public SubMenuSeparator(ObservableProperty<bool> isVisible)
        : base(new(FileAction.FileActionType.None, () => isVisible.Value, () => { }), isVisible: isVisible)
    { }
}

public class DualActionButton : IconMenu
{
    private bool _isChecked = false;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_observableIsChecked is null)
            {
                SetProperty(ref _isChecked, true);
                return;
            }

            if (SetProperty(ref _isChecked, value))
                _observableIsChecked.Value = value;
        }
    }

    public bool IsCheckable { get; } = true;

    private readonly ObservableProperty<bool>? _observableIsChecked;

    public Brush? CheckBackground { get; }

    /// <summary>
    /// Toggle Button / Menu Item with modifiable background and dynamic icon
    /// </summary>
    public DualActionButton(FileAction action,
                            ObservableProperty<BaseIcon> icon,
                            ObservableProperty<bool>? isChecked = null,
                            StyleHelper.ContentAnimation animation = StyleHelper.ContentAnimation.None,
                            Brush? checkBackground = null,
                            IEnumerable<SubMenu>? children = null,
                            ObservableProperty<bool>? isVisible = null,
                            bool isCheckable = true)
        : base(action, icon.Value, animation, children: children, isVisible: isVisible)
    {
        CheckBackground = checkBackground;
        _observableIsChecked = isChecked;
        IsCheckable = isCheckable;

        if (_observableIsChecked is not null)
        {
            IsChecked = _observableIsChecked;
            _observableIsChecked.PropertyChanged += (sender, e) => IsChecked = e.NewValue;
        }

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
