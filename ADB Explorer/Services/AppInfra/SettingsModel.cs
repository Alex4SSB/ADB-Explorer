using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Services;

public static class UISettings
{
    public static ObservableList<AbstractGroup> SettingsList { get; set; }

    public static IEnumerable<AbstractGroup> GroupedSettings => SettingsList.Where(group => group is not Ungrouped);

    public static IEnumerable<AbstractSetting> SortSettings => SettingsList.Where(group => group is not SettingsSeparator)
        .SelectMany(group => group.Children)
        .Where(set => set.Visibility is Visibility.Visible)
        .OrderBy(sett => sett.Description);

    public static void Init()
    {
        Type appSettings = Settings.GetType();

        SettingsList = new()
        {
            new SettingsGroup("ADB", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableMdns)), "Enable mDNS (experimental)", "ADB"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.PollDevices)), "Poll For Devices", "ADB"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.PollBattery)), "Poll For Battery Status", "ADB"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableLog)), "Enable Command Log", "ADB"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Device", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.AutoRoot)), "Automatically Enable Root", "Device"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.SaveDevices)), "Save Manually Connected Devices", "Device"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.AutoOpen)), "Automatically Open For Browsing", "Device"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Drives & Features", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.PollDrives)), "Poll For Drives", "Drives & Features"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableRecycle)), "Enable Recycle Bin", "Drives & Features"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableApk)), "Enable APK Handling", "Drives & Features"),
            }),
            new SettingsSeparator(),
            new SettingsGroup("File Behavior", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowExtensions)), "Show File Name Extensions", "File Behavior"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowHiddenItems)), "Show Hidden Items", "File Behavior"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ShowSystemPackages)), "Show System Apps", "File Behavior", visibleProp: appSettings.GetProperty(nameof(Settings.EnableApk))),
            }),
            new SettingsSeparator(),
            new SettingsGroup("File Double Click", new()
            {
                new DoubleClickSetting(appSettings.GetProperty(nameof(Settings.DoubleClick)), "File Double Click", new() { { DoubleClickAction.none, "None" }, { DoubleClickAction.pull, "Pull To Default Folder" }, { DoubleClickAction.edit, "Open In Editor" } }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Working Directories", new()
            {
                new StringSetting(appSettings.GetProperty(nameof(Settings.DefaultFolder)),
                                  "Default Folder",
                                  "Working Directories",
                                  commands: new SettingsAction[] {
                                      new(() => true, () => SettingsHelper.ChangeDefaultPathAction(), "\uE70F", "Change"),
                                      new(() => !string.IsNullOrEmpty(Settings.DefaultFolder), () => Settings.DefaultFolder = "", "\uE711", "Clear"),
                                  }),
                new StringSetting(appSettings.GetProperty(nameof(Settings.ManualAdbPath)),
                                  "Override ADB Path",
                                  "Working Directories",
                                  commands: new SettingsAction[] {
                                      new(() => true,() => SettingsHelper.ChangeAdbPathAction(), "\uE70F", "Change"),
                                      new(() => !string.IsNullOrEmpty(Settings.ManualAdbPath), () => Settings.ManualAdbPath = "", "\uE711", "Clear"),
                                      new(() => true, () => SettingsHelper.ResetAppAction(), "\uE72C", Strings.S_RESTART_APP),
                                  }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Theme", new()
            {
                new ThemeSetting(appSettings.GetProperty(nameof(Settings.Theme)), "Theme", new() { { AppTheme.light, "Light" }, { AppTheme.dark, "Dark" }, { AppTheme.windowsDefault, "Windows Default" } }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Graphics", new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.ForceFluentStyles)), "Force Fluent Styles", "Graphics", visibleProp: appSettings.GetProperty(nameof(Settings.HideForceFluent))),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.SwRender)), "Disable Hardware Acceleration", "Graphics"),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.DisableAnimation)),
                                "Disable Animations",
                                "Graphics",
                                commands: new SettingsAction[] {
                                    new(() => true, () => SettingsHelper.ResetAppAction(), "\uE72C", Strings.S_RESTART_APP),
                                    new(() => true, () => SettingsHelper.DisableAnimationTipAction(), "\uE82F", "More Info"),
                                }),
                new BoolSetting(appSettings.GetProperty(nameof(Settings.EnableSplash)), "Display Splash Screen", "Graphics"),
            }),
            new Ungrouped(new()
            {
                new BoolSetting(appSettings.GetProperty(nameof(Settings.CheckForUpdates)), "Check For Updates", "About"),
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
    public string GroupName { get; private set; }
    public BaseAction[] Commands { get; private set; }

    public Visibility Visibility => visibleProp is null || (bool)visibleProp.GetValue(Settings) ? Visibility.Visible : Visibility.Collapsed;

    protected AbstractSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo visibleProp = null, params BaseAction[] commands)
    {
        this.visibleProp = visibleProp;
        this.valueProp = valueProp;
        Description = description;
        GroupName = groupName;
        Commands = commands;

        Settings.PropertyChanged += Settings_PropertyChanged;
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

    public BoolSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, groupName, visibleProp, commands)
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

    public StringSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, groupName, visibleProp, commands)
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

    public EnumSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, groupName, visibleProp, commands)
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
    public ThemeSetting(PropertyInfo valueProp, string description, Dictionary<AppTheme, string> enumNames, string groupName = null, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, groupName, visibleProp, commands)
    {
        Buttons.AddRange(enumNames.Select(val => new EnumRadioButton(val.Key, val.Value, valueProp)));
    }
}

public class DoubleClickSetting : EnumSetting
{
    public DoubleClickSetting(PropertyInfo valueProp, string description, Dictionary<DoubleClickAction, string> enumNames, string groupName = null, PropertyInfo visibleProp = null, params BaseAction[] commands)
        : base(valueProp, description, groupName, visibleProp, commands)
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
    public string Icon { get; }

    public string Tooltip { get; }

    public SettingsAction(Func<bool> canExecute, Action action, string icon, string tooltip)
        : base(canExecute, action)
    {
        Icon = icon;
        Tooltip = tooltip;
    }
}
