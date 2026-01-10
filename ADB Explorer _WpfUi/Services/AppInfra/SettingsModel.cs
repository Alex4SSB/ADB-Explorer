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

    public static IEnumerable<AbstractSetting> SortSettings => SettingsList.SelectMany(group => group.Children)
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
                new BoolSetting(() => Settings.EnableMdns, Strings.Resources.S_SETTINGS_ENABLE_MDNS, icon: "\uED14"),
                new BoolSetting(() => Settings.PollDevices, Strings.Resources.S_SETTINGS_POLL_DEVICES, icon: "\uEBDE"),
                new BoolSetting(() => Settings.PollBattery, Strings.Resources.S_SETTINGS_POLL_BATTERY, icon: "\uEE63"),
                new BoolSetting(() => Settings.EnableLog, Strings.Resources.S_SETTINGS_ENABLE_LOG, icon: "\uE9A4"),
            ], "\uE8CC"),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DEVICE,
            [
                new BoolSetting(() => Settings.AutoRoot, Strings.Resources.S_SETTINGS_AUTO_ROOT, icon: "\uE7EF"),
                new BoolSetting(() => Settings.SaveDevices, Strings.Resources.S_SETTINGS_SAVE_DEVICES, icon: "\uE78C"),
                new BoolSetting(() => Settings.AutoOpen, Strings.Resources.S_SETTINGS_AUTO_OPEN, icon: "\uE838"),
            ], "\uE8EA"),
            new SettingsGroup(Strings.Resources.S_FILE_OP_TOOLTIP,
            [
                new BoolSetting(() => Settings.EnableCompactView, Strings.Resources.S_SETTINGS_COMPACT_VIEW, icon: "\uED0C"),
                new BoolSetting(() => Settings.StopPollingOnSync, Strings.Resources.S_SETTINGS_STOP_ON_SYNC, icon: "\uE8D8"),
                new BoolSetting(() => Settings.AllowMultiOp, Strings.Resources.S_SETTINGS_PARALLEL_OPERATIONS, icon: "\uE762"),
                new BoolSetting(() => Settings.RescanOnPush, Strings.Resources.S_SETTINGS_MEDIA_RESCAN, icon: "\uE7C5"),
                new BoolSetting(() => Settings.KeepDateModified, Strings.Resources.S_SETTINGS_KEEP_MODIFIED_DATE, icon: "\uEC92"),
            ], "\uEADF"),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DRIVES,
            [
                new BoolSetting(() => Settings.PollDrives, Strings.Resources.S_SETTINGS_POLL_DRIVES, icon: "\uEBC4"),
                new BoolSetting(() => Settings.EnableRecycle, Strings.Resources.S_SETTINGS_ENABLE_TRASH, icon: "\uE74D"),
                new BoolSetting(() => Settings.EnableApk, Strings.Resources.S_SETTINGS_APK, icon: "\uE7B8"),
            ], "\uE8CE"),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_EXPLORER,
            [
                new BoolSetting(() => Settings.ShowExtensions, Strings.Resources.S_SETTINGS_SHOW_EXTENSIONS, icon: "\uE8AC"),
                new BoolSetting(() => Settings.ShowHiddenItems, Strings.Resources.S_SETTINGS_HIDDEN_ITEMS, icon: "\uE8FF"),
                new BoolSetting(() => Settings.ShowSystemPackages, Strings.Resources.S_SETTINGS_SYSTEM_APPS, visibleProp: () => Settings.EnableApk, icon: "\uE835"),
                new DoubleClickSetting(() => Settings.DoubleClick, Strings.Resources.S_SETTINGS_GROUP_DOUBLE_CLICK, [
                    new(AppSettings.DoubleClickAction.None, Strings.Resources.S_SETTINGS_DOUBLE_CLICK_NONE),
                    new(AppSettings.DoubleClickAction.Pull, Strings.Resources.S_SETTINGS_DOUBLE_CLICK_PULL, AbstractSetting.ExtractPropertyInfo(() => Settings.DefaultFolder)),
                    new(AppSettings.DoubleClickAction.Edit, Strings.Resources.S_SETTINGS_DOUBLE_CLICK_OPEN) ],
                    icon: "\uE7C9"),
            ], "\uEC50"),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_WORK_DIRS,
            [
                new TextboxSetting(() => Settings.DefaultFolder,
                                  Strings.Resources.S_SETTINGS_DEFAULT_FOLDER,
                                  commands: [
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeDefaultPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearDefaultPath),
                                  ]),
                new TextboxSetting(() => Settings.ManualAdbPath,
                                  Strings.Resources.S_SETTINGS_OVERRIDE_ADB,
                                  commands: [
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                                  ]),
            ], "\uE62F"),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_GRAPHICS,
            [
                new ComboSetting<CultureInfo>(() => Settings.UICulture,
                                 Strings.Resources.S_SETTINGS_LANGUAGE,
                                 SettingsHelper.GetAvailableLanguages(),
                                 Settings.CultureTranslationProgress,
                                 "\uF2B7",
                                 SettingsActions.Find(a => a.Name is ActionType.ResetApp)),
                //new BoolSetting(() => Settings.ForceFluentStyles, Strings.Resources.S_SETTINGS_FLUENT, visibleProp: () => RuntimeSettings.HideForceFluent),
                new ThemeSetting(() => Settings.Theme, Strings.Resources.S_SETTINGS_GROUP_THEME, new() {
                    { AppSettings.AppTheme.Light, Strings.Resources.S_SETTINGS_THEME_LIGHT },
                    { AppSettings.AppTheme.Dark, Strings.Resources.S_SETTINGS_THEME_DARK },
                    { AppSettings.AppTheme.WindowsDefault, Strings.Resources.S_SETTINGS_THEME_DEFAULT } },
                    icon: "\uE2B1"),
                new BoolSetting(() => Settings.SwRender, Strings.Resources.S_SETTINGS_DISABLE_HW, icon: "\uF211"),
                //new BoolSetting(() => Settings.DisableAnimation,
                //                Strings.Resources.S_SETTINGS_ANIMATION,
                //                commands: [
                //                    SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                //                    SettingsActions.Find(a => a.Name is ActionType.AnimationInfo),
                //                ]),
                new BoolSetting(() => Settings.EnableSplash, Strings.Resources.S_SETTINGS_SPLASH),
            ], "\uE2B1"),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_ABOUT,
            [
                new InfoSetting(Properties.AppGlobal.AppDisplayName, "\uE946", (FontFamily)App.Current.Resources["Nunito"], 18, $"v{Properties.AppGlobal.AppVersion}"),
                new BoolSetting(() => Settings.CheckForUpdates, Strings.Resources.S_SETTINGS_UPDATES, icon: "\uE895"),
            ], "\uE946"),
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
    public string Name { get; set; }

    public string Icon { get; }

    public SettingsGroup(string name, List<AbstractSetting> children, string icon = "")
    {
        Name = name;
        Children = children;
        Icon = icon;

        //Children.ForEach(c => c.GroupName = Name);

        //Children.OfType<EnumSetting>().ForEach(es => es.Icon = icon);
    }
}

public abstract class AbstractSetting : SettingsBase
{
    protected readonly PropertyInfo valueProp;
    protected readonly PropertyInfo visibleProp;

    public string Description { get; private set; }
    //public string GroupName { get; set; }
    public string Icon { get; set; }

    public Visibility Visibility => visibleProp is null || (bool)visibleProp.GetValue(visibleProp.DeclaringType.Name is nameof(AppSettings) ? Settings : RuntimeSettings) ? Visibility.Visible : Visibility.Collapsed;

    protected AbstractSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, string icon = null, params BaseAction[] commands)
    {
        this.visibleProp = visibleProp;
        this.valueProp = valueProp;
        Description = description;
        Commands = commands;
        Icon = icon;

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

public class InfoSetting : AbstractSetting
{
    public FontFamily FontFamily { get; set; }
    public int FontSize { get; set; }
    public string AltText { get; set; }

    public InfoSetting(string description, string icon = null, FontFamily fontFamily = null, int fontSize = 14, string altText = null)
        : base(null, description, icon: icon)
    {
        FontFamily = fontFamily ?? new("Segoe UI");
        FontSize = fontSize;
        AltText = altText;
    }
}

public class BoolSetting : AbstractSetting
{
    public bool Value
    {
        get => (bool)valueProp.GetValue(Settings);
        set
        {
            valueProp.SetValue(Settings, value);
            OnPropertyChanged(nameof(Label));
        }
    }

    public string Label => Value ? Strings.Resources.S_SETTINGS_ACTIVE : Strings.Resources.S_SETTINGS_INACTIVE;

    public BoolSetting(Expression<Func<bool>> propertyExpr, string description, Expression<Func<bool>> visibleProp = null, string icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, ExtractPropertyInfo(visibleProp), icon, commands)
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

public class TextboxSetting : AbstractSetting
{
    public string Value
    {
        get => (string)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public TextboxSetting(Expression<Func<string>> propertyExpr, string description, PropertyInfo visibleProp = null, string icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
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

    public ComboSetting(Expression<Func<T>> propertyExpr, string description, IEnumerable<T> options, ObservableProperty<string> altLabel = null, string icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, null, icon, commands)
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
    public List<EnumComboboxItem> Buttons { get; protected set; } = [];

    public EnumSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, string icon = null, params BaseAction[] commands)
        : base(valueProp, description, visibleProp, icon, commands)
    { }
}

public class ThemeSetting : EnumSetting
{
    public AppSettings.AppTheme Value
    {
        get => (AppSettings.AppTheme)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public ThemeSetting(Expression<Func<AppSettings.AppTheme>> propertyExpr, string description, Dictionary<AppSettings.AppTheme, string> enumNames, PropertyInfo visibleProp = null, string icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    {
        var sourceProp = ExtractPropertyInfo(propertyExpr);
        Buttons.AddRange(enumNames.Select(val =>
        {
            return new EnumComboboxItem(val.Key, val.Value);
        }));
    }
}

public class DoubleClickSetting : EnumSetting
{
    public AppSettings.DoubleClickAction Value
    {
        get => (AppSettings.DoubleClickAction)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public DoubleClickSetting(Expression<Func<AppSettings.DoubleClickAction>> propertyExpr, string description, IEnumerable<EnumComboboxItem> enumNames, PropertyInfo visibleProp = null, string icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    {
        var sourceProp = ExtractPropertyInfo(propertyExpr);
        Buttons = [.. enumNames];
    }
}

public class EnumComboboxItem : ViewModelBase
{
    public Enum Key { get; set; }
    public string Name { get; set; }

    readonly PropertyInfo visibleProp;

    public bool IsEnabled
    {
        get
        {
            if (visibleProp == null)
                return true;

            object? value = visibleProp.GetValue(visibleProp.DeclaringType.Name is nameof(AppSettings) ? Settings : RuntimeSettings);
            if (value is bool val)
                return val;
            if (value is string str)
                return !string.IsNullOrEmpty(str);

            return true;
        }
    }

    public EnumComboboxItem(Enum key, string name, PropertyInfo visibleProp = null)
    {
        Key = key;
        Name = name;
        this.visibleProp = visibleProp;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
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
