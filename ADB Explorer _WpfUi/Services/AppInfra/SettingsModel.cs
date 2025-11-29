using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;
using System.Linq.Expressions;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.SettingsAction;

namespace ADB_Explorer.Services;

public static class UISettings
{
    public static ObservableList<AbstractGroup> SettingsList { get; set; }

    public static ObservableList<Notification> Notifications { get; set; } = [];

    public static IEnumerable<AbstractGroup> GroupedSettings => SettingsList.Where(group => group is not Ungrouped);

    public static IEnumerable<AbstractSetting> SortSettings => SettingsList.Where(group => group is not SettingsSeparator)
        .SelectMany(group => group.Children)
        .Where(set => set.Visibility is Visibility.Visible)
        .OrderBy(sett => sett.Description);

    private static readonly Dictionary<ActionType, string> Icons = new()
    {
        { ActionType.ChangeDefaultPath, "\uE70F" },
        { ActionType.ClearDefaultPath, "\uE711" },
        { ActionType.ResetApp, "\uE72C" },
        { ActionType.AnimationInfo, "\uE82F" },
    };

    private static readonly List<SettingsAction> SettingsActions =
    [
        new(ActionType.ChangeDefaultPath, () => true, SettingsHelper.ChangeDefaultPathAction, Icons[ActionType.ChangeDefaultPath], Strings.Resources.S_BUTTON_CHANGE),
        new(ActionType.ClearDefaultPath, () => !string.IsNullOrEmpty(Settings.DefaultFolder), () => Settings.DefaultFolder = "", Icons[ActionType.ClearDefaultPath], Strings.Resources.S_BUTTON_CLEAR),
        new(ActionType.ChangeAdbPath, () => true, SettingsHelper.ChangeAdbPathAction, Icons[ActionType.ChangeDefaultPath], Strings.Resources.S_BUTTON_CHANGE),
        new(ActionType.ClearAdbPath, () => !string.IsNullOrEmpty(Settings.ManualAdbPath), () => Settings.ManualAdbPath = "", Icons[ActionType.ClearDefaultPath], Strings.Resources.S_BUTTON_CLEAR),
        new(ActionType.ResetApp, () => true, SettingsHelper.ResetAppAction, Icons[ActionType.ResetApp], Strings.Resources.S_RESTART_APP),
        new(ActionType.AnimationInfo, () => true, SettingsHelper.DisableAnimationTipAction, Icons[ActionType.AnimationInfo], Strings.Resources.S_BUTTON_MORE_INFO_TOOLTIP),
    ];

    public static void Init()
    {
        SettingsList =
        [
            new SettingsGroup("ADB",
            [
                new BoolSetting(() => Settings.EnableMdns, Strings.Resources.S_SETTINGS_ENABLE_MDNS),
                new BoolSetting(() => Settings.PollDevices, Strings.Resources.S_SETTINGS_POLL_DEVICES),
                new BoolSetting(() => Settings.PollBattery, Strings.Resources.S_SETTINGS_POLL_BATTERY),
                new BoolSetting(() => Settings.EnableLog, Strings.Resources.S_SETTINGS_ENABLE_LOG),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DEVICE,
            [
                new BoolSetting(() => Settings.AutoRoot, Strings.Resources.S_SETTINGS_AUTO_ROOT),
                new BoolSetting(() => Settings.SaveDevices, Strings.Resources.S_SETTINGS_SAVE_DEVICES),
                new BoolSetting(() => Settings.AutoOpen, Strings.Resources.S_SETTINGS_AUTO_OPEN),
            ]),
            new SettingsGroup(Strings.Resources.S_FILE_OP_TOOLTIP,
            [
                new BoolSetting(() => Settings.EnableCompactView, Strings.Resources.S_SETTINGS_COMPACT_VIEW),
                new BoolSetting(() => Settings.StopPollingOnSync, Strings.Resources.S_SETTINGS_STOP_ON_SYNC),
                new BoolSetting(() => Settings.AllowMultiOp, Strings.Resources.S_SETTINGS_PARALLEL_OPERATIONS),
                new BoolSetting(() => Settings.RescanOnPush, Strings.Resources.S_SETTINGS_MEDIA_RESCAN),
                new BoolSetting(() => Settings.KeepDateModified, Strings.Resources.S_SETTINGS_KEEP_MODIFIED_DATE),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DRIVES,
            [
                new BoolSetting(() => Settings.PollDrives, Strings.Resources.S_SETTINGS_POLL_DRIVES),
                new BoolSetting(() => Settings.EnableRecycle, Strings.Resources.S_SETTINGS_ENABLE_TRASH),
                new BoolSetting(() => Settings.EnableApk, Strings.Resources.S_SETTINGS_APK),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_EXPLORER,
            [
                new BoolSetting(() => Settings.ShowExtensions, Strings.Resources.S_SETTINGS_SHOW_EXTENSIONS),
                new BoolSetting(() => Settings.ShowHiddenItems, Strings.Resources.S_SETTINGS_HIDDEN_ITEMS),
                new BoolSetting(() => Settings.ShowSystemPackages, Strings.Resources.S_SETTINGS_SYSTEM_APPS, visibleProp: () => Settings.EnableApk),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DOUBLE_CLICK,
            [
                new DoubleClickSetting(() => Settings.DoubleClick, Strings.Resources.S_SETTINGS_GROUP_DOUBLE_CLICK, new() {
                    { AppSettings.DoubleClickAction.None, Strings.Resources.S_SETTINGS_DOUBLE_CLICK_NONE },
                    { AppSettings.DoubleClickAction.Pull, Strings.Resources.S_SETTINGS_DOUBLE_CLICK_PULL },
                    { AppSettings.DoubleClickAction.Edit, Strings.Resources.S_SETTINGS_DOUBLE_CLICK_OPEN } }),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_WORK_DIRS,
            [
                new StringSetting(() => Settings.DefaultFolder,
                                  Strings.Resources.S_SETTINGS_DEFAULT_FOLDER,
                                  commands: [
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeDefaultPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearDefaultPath),
                                  ]),
                new StringSetting(() => Settings.ManualAdbPath,
                                  Strings.Resources.S_SETTINGS_OVERRIDE_ADB,
                                  commands: [
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                                  ]),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_THEME,
            [
                new ThemeSetting(() => Settings.Theme, Strings.Resources.S_SETTINGS_GROUP_THEME, new() {
                    { AppSettings.AppTheme.Light, Strings.Resources.S_SETTINGS_THEME_LIGHT },
                    { AppSettings.AppTheme.Dark, Strings.Resources.S_SETTINGS_THEME_DARK },
                    { AppSettings.AppTheme.WindowsDefault, Strings.Resources.S_SETTINGS_THEME_DEFAULT } }),
            ]),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_GRAPHICS,
            [
                new ComboSetting<CultureInfo>(() => Settings.UICulture,
                                 Strings.Resources.S_SETTINGS_LANGUAGE,
                                 SettingsHelper.GetAvailableLanguages(),
                                 Settings.CultureTranslationProgress,
                                 commands: SettingsActions.Find(a => a.Name is ActionType.ResetApp)),
                //new BoolSetting(() => Settings.ForceFluentStyles, Strings.Resources.S_SETTINGS_FLUENT, visibleProp: () => RuntimeSettings.HideForceFluent),
                new BoolSetting(() => Settings.SwRender, Strings.Resources.S_SETTINGS_DISABLE_HW),
                //new BoolSetting(() => Settings.DisableAnimation,
                //                Strings.Resources.S_SETTINGS_ANIMATION,
                //                commands: [
                //                    SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                //                    SettingsActions.Find(a => a.Name is ActionType.AnimationInfo),
                //                ]),
                new BoolSetting(() => Settings.EnableSplash, Strings.Resources.S_SETTINGS_SPLASH),
            ]),
            new Ungrouped(
            [
                new BoolSetting(() => Settings.CheckForUpdates, Strings.Resources.S_SETTINGS_UPDATES)
                { GroupName = Strings.Resources.S_SETTINGS_GROUP_ABOUT },
            ]),
        ];
    }
}

public class Notification : BaseAction
{
    public string Title { get; }

    public static string Tooltip => Strings.Resources.S_TOOLTIP_MORE_INFO;

    public Notification(Action action, string title) : base(() => true, action)
    {
        Title = title;

        ((CommandHandler)Command).OnExecute.PropertyChanged += (sender, e) =>
        {
            UISettings.Notifications.Remove(this);
        };
    }
}

public abstract class SettingsBase : ViewModelBase
{
    public BaseAction[] Commands { get; protected set; }
}

public abstract class AbstractGroup : SettingsBase
{
    public List<AbstractSetting> Children { get; set; }
}

public class SettingsGroup : AbstractGroup
{
    private bool isExpanded = false;
    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }

    public string Name { get; set; }

    public SettingsGroup(string name, List<AbstractSetting> children, params SettingsAction[] commands)
    {
        Name = name;
        Children = children;
        Commands = commands;
        
        Children.ForEach(c => c.GroupName = Name);

        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.GroupsExpanded))
            IsExpanded = RuntimeSettings.GroupsExpanded;
    }
}

public class SettingsSeparator : AbstractGroup
{ }

public class Ungrouped : AbstractGroup
{
    public Ungrouped(List<AbstractSetting> children)
    {
        Children = children;
    }
}

public abstract class AbstractSetting : SettingsBase
{
    protected readonly PropertyInfo valueProp;
    protected readonly PropertyInfo visibleProp;

    public string Description { get; private set; }
    public string GroupName { get; set; }

    public Visibility Visibility => visibleProp is null || (bool)visibleProp.GetValue(visibleProp.DeclaringType.Name is nameof(AppSettings) ? Settings : RuntimeSettings) ? Visibility.Visible : Visibility.Collapsed;

    protected AbstractSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, params BaseAction[] commands)
    {
        this.visibleProp = visibleProp;
        this.valueProp = valueProp;
        Description = description;
        Commands = commands;
        
        Settings.PropertyChanged += Settings_PropertyChanged;
        RuntimeSettings.PropertyChanged += Settings_PropertyChanged;
    }

    protected virtual void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(Visibility));
        }
    }

    public static PropertyInfo ExtractPropertyInfo<T>(Expression<Func<T>> expr)
    {
        if (expr?.Body is MemberExpression member
            && member.Member is PropertyInfo info)
        {
            return info;
        }

        return null;
    }

}

public class BoolSetting : AbstractSetting
{
    public bool Value
    {
        get => (bool)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public BoolSetting(Expression<Func<bool>> propertyExpr, string description, Expression<Func<bool>> visibleProp = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, ExtractPropertyInfo(visibleProp), commands)
    { }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == valueProp.Name)
        {
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(Visibility));
        }
    }
}

public class StringSetting : AbstractSetting
{
    public string Value
    {
        get => (string)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public StringSetting(Expression<Func<string>> propertyExpr, string description, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, commands)
    { }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == valueProp.Name)
        {
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(Visibility));
        }
    }
}

public class ComboSetting<T> : AbstractSetting
{
    public T Value
    {
        get => (T)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public IEnumerable<T> Options { get; } = [];

    public ObservableProperty<string> ObservableAltLabel { get; } = new();

    public string AltLabel { get; private set; } = null;

    public ComboSetting(Expression<Func<T>> propertyExpr, string description, IEnumerable<T> options, ObservableProperty<string> altLabel = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, null, commands)
    {
        Options = options;
        ObservableAltLabel = altLabel;

        if (ObservableAltLabel is not null)
        {
            ObservableAltLabel.PropertyChanged += (sender, e) =>
            {
                AltLabel = e.NewValue;
                OnPropertyChanged(nameof(AltLabel));
            };

            AltLabel = ObservableAltLabel.Value;
        }
    }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == valueProp.Name)
        {
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(Visibility));
        }
    }
}

public class EnumSetting : AbstractSetting
{
    private bool isExpanded = false;
    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }

    public List<EnumRadioButton> Buttons { get; } = [];

    public EnumSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, visibleProp, commands)
    {
        RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.SearchText))
        {
            IsExpanded = !string.IsNullOrEmpty(RuntimeSettings.SearchText) && Buttons.Any(button => button.Name.Contains(RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase));
        }
    }
}

public class ThemeSetting : EnumSetting
{
    public ThemeSetting(Expression<Func<AppSettings.AppTheme>> propertyExpr, string description, Dictionary<AppSettings.AppTheme, string> enumNames, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, commands)
    {
        var sourceProp = ExtractPropertyInfo(propertyExpr);
        Buttons.AddRange(enumNames.Select(val =>
        {
            
            return new EnumRadioButton(val.Key, val.Value, sourceProp);
        }));
    }
}

public class DoubleClickSetting : EnumSetting
{
    public DoubleClickSetting(Expression<Func<AppSettings.DoubleClickAction>> propertyExpr, string description, Dictionary<AppSettings.DoubleClickAction, string> enumNames, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, commands)
    {
        var sourceProp = ExtractPropertyInfo(propertyExpr);
        Buttons.AddRange(enumNames.Select(val => new EnumRadioButton(val.Key, val.Value, sourceProp)));
    }
}

public class EnumRadioButton
{
    private readonly PropertyInfo sourceProp;
    public Enum Value
    {
        get => (Enum)sourceProp.GetValue(Settings);
        set => sourceProp.SetValue(Settings, value);
    }
    public Enum ButtonIndex { get; set; }
    public string Name { get; set; }
    public bool IsChecked
    {
        get => Value.Equals(ButtonIndex);
        set
        {
            if (value)
                Value = ButtonIndex;
        }
    }
    public string Group => sourceProp.Name;

    public EnumRadioButton(Enum buttonIndex, string name, PropertyInfo sourceProp)
    {
        ButtonIndex = buttonIndex;
        Name = name;
        this.sourceProp = sourceProp;
    }
}

public class SettingsAction : BaseAction
{
    public enum ActionType
    {
        ChangeDefaultPath,
        ChangeAdbPath,
        ClearDefaultPath,
        ClearAdbPath,
        ResetApp,
        AnimationInfo,
    }

    public ActionType Name { get; }

    public string Icon { get; }

    public string Tooltip { get; }

    public SettingsAction(ActionType name, Func<bool> canExecute, Action action, string icon, string tooltip)
        : base(canExecute, action)
    {
        Name = name;
        Icon = icon;
        Tooltip = tooltip;
    }
}
