using System.Linq.Expressions;
using static ADB_Explorer.Models.Data;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace ADB_Explorer.ViewModels;

public abstract class SettingsBase : ObservableObject
{
    public BaseAction[] Commands { get; protected set; } = [];
}

public abstract class AbstractGroup : SettingsBase
{
    public List<AbstractSetting> Children { get; set; } = [];
}

public class SettingsGroup : AbstractGroup
{
    public string Name { get; set; }

    public object? IconContent { get; set; }

    public SettingsGroup(string name, List<AbstractSetting> children, BaseIcon? icon = null)
    {
        Name = name;
        Children = children;
        IconContent = icon?.IconContent;
    }
}

public abstract class AbstractSetting : SettingsBase
{
    protected readonly PropertyInfo? valueProp;
    protected readonly PropertyInfo? visibleProp;
    protected PropertyInfo? enabledProp;

    public string Description { get; private set; }
    public object? IconContent { get; set; }
    public TextAlignment HeaderAlignment { get; protected set; }
    public string? Info { get; init; }

    /// <summary>
    /// Optional WPF UI appearance accent for the settings card background and border.
    /// </summary>
    public ControlAppearance? CardAppearance { get; init; }

    /// <summary>
    /// When set, the settings card is enabled only while this boolean settings property is <see langword="true"/>.
    /// </summary>
    public PropertyInfo? EnableWhen
    {
        init => enabledProp = value;
    }

    public Visibility Visibility
    {
        get
        {
            if (visibleProp is null)
                return Visibility.Visible;

            var value = visibleProp.GetValue(Settings);
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (value is Enum enumVal)
            {
                return Convert.ToInt32(enumVal) == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (value is string strVal)
            {
                return string.IsNullOrEmpty(strVal) ? Visibility.Collapsed : Visibility.Visible;
            }

            return Visibility.Visible;
        }
    }

    public bool IsEnabled
    {
        get
        {
            if (enabledProp is null)
                return true;

            return enabledProp.GetValue(Settings) is true;
        }
    }

    protected AbstractSetting(PropertyInfo? valueProp, string description, PropertyInfo? visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
    {
        this.visibleProp = visibleProp;
        this.valueProp = valueProp;
        Description = description;
        Commands = commands;
        IconContent = icon?.IconContent;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    protected virtual void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(Visibility));
        }

        if (e.PropertyName == enabledProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
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

    public InfoSetting(string description, BaseIcon? icon = null, FontFamily? fontFamily = null, int fontSize = 14, string? altText = null, TextAlignment headerAlignment = TextAlignment.Left)
        : base(null, description, icon: icon)
    {
        FontFamily = fontFamily ?? new("Segoe UI");
        FontSize = fontSize;
        AltText = altText ?? "";
        HeaderAlignment = headerAlignment;
    }
}

public class LongDescriptionSetting : AbstractSetting
{
    public string AltText { get; set; }

    public LongDescriptionSetting(string description, string altText, BaseIcon? icon = null)
        : base(null, description, null, icon)
    {
        AltText = altText;
    }
}

public class MultiLinkSetting : AbstractSetting
{
    public List<LinkSetting> Links { get; set; } = [];
    public MultiLinkSetting(string description, List<LinkSetting> links, BaseIcon? icon = null)
        : base(null, description, icon: icon)
    {
        Links = links;
    }
}

public class LinkSetting : AbstractSetting
{
    private readonly Func<string>? _resolveFilePath;

    public Uri Url { get; set; }
    public string AltText { get; set; }

    public BaseAction Command => new(() => true, () =>
    {
        try
        {
            if (_resolveFilePath is not null)
                Process.Start("explorer.exe", _resolveFilePath());
            else if (Url.IsFile)
                Process.Start("explorer.exe", Url.LocalPath);
            else
                Network.OpenUrl(Url.ToString(), RuntimeSettings.DefaultBrowserPath);
        }
        catch
        {
            // Broken shell association or missing path — never crash from a settings link.
        }
    });

    public string ToolTip => _resolveFilePath?.Invoke()
        ?? (Url.IsFile ? Url.LocalPath : Url.ToString());

    public LinkSetting(string description, Uri url, BaseIcon? iconBase = null, string? altText = null, Func<string>? resolveFilePath = null)
        : base(null, description)
    {
        _resolveFilePath = resolveFilePath;
        Url = url;
        AltText = altText ?? "";
        IconContent = iconBase?.IconContent;
    }
}

public class NumericSetting : AbstractSetting
{
    public int Value
    {
        get => (int)(valueProp!.GetValue(Settings) ?? 0);
        set => valueProp!.SetValue(Settings, value);
    }

    public int MinValue { get; }

    public int MaxValue { get; }

    public string Unit { get; }

    public NumericSetting(Expression<Func<int>> propertyExpr,
                          string description,
                          int minValue = int.MinValue,
                          int maxValue = int.MaxValue,
                          string unit = "",
                          PropertyInfo? visibleProp = null,
                          BaseIcon? icon = null,
                          params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    {
        Unit = unit;
        MinValue = minValue;
        MaxValue = maxValue;
    }

    protected override void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class BoolSetting : AbstractSetting
{
    public bool Value
    {
        get => (bool)(valueProp!.GetValue(Settings) ?? false);
        set
        {
            valueProp!.SetValue(Settings, value);
            OnPropertyChanged(nameof(Label));
        }
    }

    public string Label => Value ? Strings.Resources.S_SETTINGS_ACTIVE : Strings.Resources.S_SETTINGS_INACTIVE;

    public BoolSetting(Expression<Func<bool>> propertyExpr, string description, PropertyInfo? visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    { }

    protected override void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class TextboxSetting : AbstractSetting
{
    public string Value
    {
        get => (string)valueProp!.GetValue(Settings)!;
        set => valueProp!.SetValue(Settings, value);
    }

    public TextboxSetting(Expression<Func<string>> propertyExpr, string description, PropertyInfo? visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    { }

    protected override void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class SimpleComboSetting<T> : AbstractSetting
{
    public T Value
    {
        get => (T)valueProp!.GetValue(Settings)!;
        set => valueProp!.SetValue(Settings, value);
    }

    public IEnumerable<EnumComboboxItem> Options { get; } = [];

    public SimpleComboSetting(Expression<Func<T>> propertyExpr, string description, IEnumerable<EnumComboboxItem> options, PropertyInfo? visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    {
        Options = options;
    }

    protected override void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class ComboSetting<T> : AbstractSetting
{
    public T Value
    {
        get => (T)valueProp!.GetValue(Settings)!;
        set => valueProp!.SetValue(Settings, value);
    }

    public IEnumerable<T> Options { get; } = [];

    public ObservableProperty<string>? ObservableAltLabel { get; }

    public string? AltLabel { get; private set; }

    public ComboSetting(Expression<Func<T>> propertyExpr, string description, IEnumerable<T> options, ObservableProperty<string>? altLabel = null, BaseIcon? icon = null, params BaseAction[] commands)
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

    protected override void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class ColorSetting : AbstractSetting
{
    public Color PickerColor
    {
        get => (Color)(valueProp!.GetValue(Settings) ?? default(Color));
        set => valueProp!.SetValue(Settings, value);
    }

    public AsyncRelayCommand PickColorCommand { get; }

    public ColorSetting(Expression<Func<Color>> propertyExpr,
                        string description,
                        PropertyInfo? visibleProp = null,
                        BaseIcon? icon = null)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon)
    {
        PickColorCommand = new AsyncRelayCommand(async () =>
        {
            var panel = new ColorPickerPanel
            {
                SelectedColor = PickerColor
            };

            var result = await DialogService.ShowDialog(
                AdbContentDialog.CustomContentDialog(panel),
                Strings.Resources.S_PICK_COLOR,
                primaryText: Strings.Resources.S_CONFIRM,
                closeText: Strings.Resources.S_CANCEL);

            if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
                PickerColor = panel.SelectedColor;
        });
    }

    protected override void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(PickerColor));
    }
}

public class EnumComboboxItem : ObservableObject
{
    public Enum Key { get; set; }
    public string Name { get; set; }

    readonly PropertyInfo? _visibleProp;

    public bool IsEnabled
    {
        get
        {
            if (_visibleProp == null)
                return true;

            object? value = _visibleProp.GetValue(Settings);
            if (value is bool val)
                return val;
            if (value is string str)
                return !string.IsNullOrEmpty(str);

            return true;
        }
    }

    public EnumComboboxItem(Enum key, string name, PropertyInfo? visibleProp = null)
    {
        Key = key;
        Name = name;
        _visibleProp = visibleProp;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _visibleProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }
}
