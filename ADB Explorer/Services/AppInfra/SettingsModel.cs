using ADB_Explorer.Helpers;
using ADB_Explorer.Resources;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.SettingsAction;

namespace ADB_Explorer.Services;

public static class UISettings
{
    public static ObservableList<AbstractGroup> SettingsList { get; set; }

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

    private static readonly List<SettingsAction> SettingsActions = new()
    {
        new(ActionType.ChangeDefaultPath, () => true, SettingsHelper.ChangeDefaultPathAction, Icons[ActionType.ChangeDefaultPath], "Change"),
        new(ActionType.ClearDefaultPath, () => !string.IsNullOrEmpty(Settings.DefaultFolder), () => Settings.DefaultFolder = "", Icons[ActionType.ClearDefaultPath], "Clear"),
        new(ActionType.ChangeAdbPath, () => true,SettingsHelper.ChangeAdbPathAction, Icons[ActionType.ChangeDefaultPath], "Change"),
        new(ActionType.ClearAdbPath, () => !string.IsNullOrEmpty(Settings.ManualAdbPath), () => Settings.ManualAdbPath = "", Icons[ActionType.ClearDefaultPath], "Clear"),
        new(ActionType.ResetApp, () => true, SettingsHelper.ResetAppAction, Icons[ActionType.ResetApp], Strings.S_RESTART_APP),
        new(ActionType.AnimationInfo, () => true, SettingsHelper.DisableAnimationTipAction, Icons[ActionType.AnimationInfo], "More Info"),
    };

    public static void Init()
    {
        Type appSettings = Settings.GetType();

        SettingsList = new()
        {
            new SettingsGroup("ADB", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableMdns)), "Enable mDNS (experimental)"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.PollDevices)), "Poll For Devices"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.PollBattery)), "Poll For Battery Status"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableLog)), "Enable Command Log"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Device", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.AutoRoot)), "Automatically Enable Root"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.SaveDevices)), "Save Manually Connected Devices"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.AutoOpen)), "Automatically Open For Browsing"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("File Operations", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowExtendedView)), "Use Extended View"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.StopPollingOnSync)), "Stop Polling On Push/Pull"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.AllowMultiOp)), "Allow Simultaneous Operations"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Drives & Features", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.PollDrives)), "Poll For Drives"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableRecycle)), "Enable Recycle Bin"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableApk)), "Enable APK Handling"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Explorer", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowExtensions)), "Show File Name Extensions"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowHiddenItems)), "Show Hidden Items"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowSystemPackages)), "Show System Apps", visibleProp: appSettings.GetProperty(nameof(Settings.EnableApk))),
            }),
            new SettingsSeparator(),
            new SettingsGroup("File Double Click", new()
            {
                new DoubleClickSetting(appSettings.GetProperty(nameof(Settings.DoubleClick)), "File Double Click", new() {
                    { AppSettings.DoubleClickAction.none, "None" },
                    { AppSettings.DoubleClickAction.pull, "Pull To Default Folder" },
                    { AppSettings.DoubleClickAction.edit, "Open In Editor" } }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Working Directories", new()
            {
                new StringSetting(appSettings.GetProperty(nameof(Settings.DefaultFolder)),
                                  "Default Folder",
                                  commands: new[] {
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeDefaultPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearDefaultPath),
                                  }),
                new StringSetting(appSettings.GetProperty(nameof(Settings.ManualAdbPath)),
                                  "Override ADB Path",
                                  commands: new[] {
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                                  }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Theme", new()
            {
                new ThemeSetting(appSettings.GetProperty(nameof(Settings.Theme)), "Theme", new() {
                    { AppSettings.AppTheme.light, "Light" },
                    { AppSettings.AppTheme.dark, "Dark" },
                    { AppSettings.AppTheme.windowsDefault, "Windows Default" } }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Graphics", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ForceFluentStyles)), "Force Fluent Styles", visibleProp: RuntimeSettings.GetType().GetProperty(nameof(RuntimeSettings.HideForceFluent))),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.SwRender)), "Disable Hardware Acceleration"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.DisableAnimation)),
                                "Disable Animations",
                                commands: new[] {
                                    SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                                    SettingsActions.Find(a => a.Name is ActionType.AnimationInfo),
                                }),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableSplash)), "Display Splash Screen"),
            }),
            new Ungrouped(new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.CheckForUpdates)), "Check For Updates"),
            }),
        };
    }
}

public abstract class AbstractGroup : ViewModelBase
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

    public SettingsGroup(string name, List<AbstractSetting> children)
    {
        Name = name;
        Children = children;

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

public abstract class AbstractSetting : ViewModelBase
{
    protected readonly PropertyInfo valueProp;
    protected readonly PropertyInfo visibleProp;

    public string Description { get; private set; }
    public string GroupName { get; set; }
    public BaseAction[] Commands { get; private set; }

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

}

public class BoolSetting : AbstractSetting
{
    public bool Value
    {
        get => (bool)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public BoolSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, visibleProp, commands)
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

    public StringSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, visibleProp, commands)
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

public class EnumSetting : AbstractSetting
{
    private bool isExpanded = false;
    public bool IsExpanded
    {
        get => isExpanded;
        set => Set(ref isExpanded, value);
    }

    public List<EnumRadioButton> Buttons { get; } = new();

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
    public ThemeSetting(PropertyInfo valueProp, string description, Dictionary<AppSettings.AppTheme, string> enumNames, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, visibleProp, commands)
    {
        Buttons.AddRange(enumNames.Select(val => new EnumRadioButton(val.Key, val.Value, valueProp)));
    }
}

public class DoubleClickSetting : EnumSetting
{
    public DoubleClickSetting(PropertyInfo valueProp, string description, Dictionary<AppSettings.DoubleClickAction, string> enumNames, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, visibleProp, commands)
    {
        Buttons.AddRange(enumNames.Select(val => new EnumRadioButton(val.Key, val.Value, valueProp)));
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
