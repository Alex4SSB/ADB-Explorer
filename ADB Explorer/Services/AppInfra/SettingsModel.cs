using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AdbExplorerConst;
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
        ResetCommand resetCommand = new();
        ShowAnimationTipCommand tipCommand = new();
        ChangeDefaultPathCommand defPathCommand = new();
        ChangeAdbPathCommand adbPathCommand = new();

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
                new DoubleClickSetting(appSettings.GetProperty(nameof(Settings.DoubleClick)), "File Double Click", new() { { DoubleClickAction.pull, "Pull To Default Folder" }, { DoubleClickAction.edit, "Open In Editor" } }),
            }),
            new SettingsSeparator(),
            new SettingsGroup("Working Directories", new()
            {
                new StringSetting(appSettings.GetProperty(nameof(Settings.DefaultFolder)), "Default Folder", "Working Directories", null, null, defPathCommand, new ClearTextSettingCommand(appSettings.GetProperty(nameof(Settings.DefaultFolder)))),
                new StringSetting(appSettings.GetProperty(nameof(Settings.ManualAdbPath)), "Override ADB Path", "Working Directories", null, null, adbPathCommand, new ClearTextSettingCommand(appSettings.GetProperty(nameof(Settings.ManualAdbPath))), resetCommand),
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
                new BoolSetting(appSettings.GetProperty(nameof(Settings.DisableAnimation)), "Disable Animations", "Graphics", null, null, resetCommand, tipCommand),
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
{

}

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
    protected readonly PropertyInfo enableProp;
    protected readonly PropertyInfo visibleProp;

    public string Description { get; private set; }
    public string GroupName { get; private set; }
    public SettingButton[] Commands { get; private set; }

    public bool IsEnabled => enableProp is null || (bool)enableProp.GetValue(Settings);
    public Visibility Visibility => visibleProp is null || (bool)visibleProp.GetValue(Settings) ? Visibility.Visible : Visibility.Collapsed;

    protected AbstractSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
    {
        this.enableProp = enableProp;
        this.visibleProp = visibleProp;
        this.valueProp = valueProp;
        Description = description;
        GroupName = groupName;
        Commands = commands;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    protected virtual void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == enableProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
        else if (e.PropertyName == visibleProp?.Name)
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

    public BoolSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
        : base(valueProp, description, groupName, enableProp, visibleProp, commands)
    { }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == valueProp.Name)
        {
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == enableProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
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

    public StringSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
        : base(valueProp, description, groupName, enableProp, visibleProp, commands)
    {

    }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == valueProp.Name)
        {
            OnPropertyChanged(nameof(Value));
        }
        else if (e.PropertyName == enableProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
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

    public EnumSetting(PropertyInfo valueProp, string description, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
        : base(valueProp, description, groupName, enableProp, visibleProp, commands)
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
    public ThemeSetting(PropertyInfo valueProp, string description, Dictionary<AppTheme, string> enumNames, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
        : base(valueProp, description, groupName, enableProp, visibleProp, commands)
    {
        Buttons.AddRange(enumNames.Select(val => new EnumRadioButton(val.Key, val.Value, valueProp)));
    }
}

public class DoubleClickSetting : EnumSetting
{
    public DoubleClickSetting(PropertyInfo valueProp, string description, Dictionary<DoubleClickAction, string> enumNames, string groupName = null, PropertyInfo enableProp = null, PropertyInfo visibleProp = null, params SettingButton[] commands)
        : base(valueProp, description, groupName, enableProp, visibleProp, commands)
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

public abstract class SettingButton : ViewModelBase
{
    public virtual bool IsEnabled { get; } = true;

}

public class ResetCommand : SettingButton
{
    public static void Action()
    {
        Process.Start(Environment.ProcessPath);
        Application.Current.Shutdown();
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ShowAnimationTipCommand : SettingButton
{
    public static void Action()
    {
        DialogService.ShowMessage("The app has many animations that are enabled as part of the fluent design.\nThe side views animation is always disabled when the app window is maximized on a secondary display.\n\n• Checking this setting disables all app animations except progress bars, progress rings, and drive usage bars.", "App Animations", DialogService.DialogIcon.Tip);
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ClearTextSettingCommand : SettingButton
{
    private readonly PropertyInfo sourceProp;

    public override bool IsEnabled => sourceProp.GetValue(Settings) is string str && !string.IsNullOrEmpty(str);

    public ClearTextSettingCommand(PropertyInfo sourceProp)
    {
        this.sourceProp = sourceProp;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == sourceProp.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public void Action()
    {
        sourceProp.SetValue(Settings, "");
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ChangeDefaultPathCommand : SettingButton
{
    public static void Action()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false
        };
        if (Settings.DefaultFolder != "")
            dialog.DefaultDirectory = Settings.DefaultFolder;

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            Settings.DefaultFolder = dialog.FileName;
        }
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class ChangeAdbPathCommand : SettingButton
{
    public static void Action()
    {
        var dialog = new OpenFileDialog()
        {
            Multiselect = false,
            Title = "Select ADB Executable",
            Filter = "ADB Executable|adb.exe",
        };

        if (!string.IsNullOrEmpty(Settings.ManualAdbPath))
        {
            try
            {
                dialog.InitialDirectory = Directory.GetParent(Settings.ManualAdbPath).FullName;
            }
            catch (Exception) { }
        }

        if (dialog.ShowDialog() == true)
        {
            string message = "";
            var version = ADBService.VerifyAdbVersion(dialog.FileName);
            if (version is null)
            {
                message = "Could not get ADB version from provided path.";
            }
            else if (version < MIN_ADB_VERSION)
            {
                message = "ADB version from provided path is too low.";
            }

            if (message != "")
            {
                DialogService.ShowMessage(message, "Fail to override ADB", DialogService.DialogIcon.Exclamation);
                return;
            }

            Settings.ManualAdbPath = dialog.FileName;
        }
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}
