using System.Linq.Expressions;

namespace ADB_Explorer.ViewModels;

/// <summary>
/// An item of a selector menu (search options, view options): a radio choice, or a checkable toggle.
/// </summary>
public partial class SelectorItem : ObservableObject
{
    public BaseAction Action { get; protected init; } = null!;

    public UIElement? Icon { get; init; }

    public string? Info { get; init; }

    public string Name { get; init; } = "";

    public ICollection<object>? Children { get; init; }

    public bool IsRadioButton { get; protected init; } = true;

    [ObservableProperty]
    public partial bool IsChecked { get; protected set; }
}

/// <summary>
/// A checkable toggle backed by a boolean <see cref="AppSettings"/> property; unchecked while not allowed.
/// </summary>
public class SettingToggleItem : SelectorItem
{
    private readonly PropertyInfo _valueProp;
    private readonly Func<bool> _isAllowed;

    private bool Value
    {
        get => (bool)(_valueProp.GetValue(Data.Settings) ?? false);
        set => _valueProp.SetValue(Data.Settings, value);
    }

    public SettingToggleItem(string name, Expression<Func<bool>> propertyExpr, UIElement? icon = null, string? info = null, Func<bool>? isAllowed = null)
    {
        _valueProp = AbstractSetting.ExtractPropertyInfo(propertyExpr);
        _isAllowed = isAllowed ?? (() => true);

        Name = name;
        Icon = icon;
        Info = info;
        IsRadioButton = false;
        Action = new(_isAllowed, () => Value ^= true);

        Data.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == _valueProp.Name)
                Refresh();
        };

        if (isAllowed is not null)
        {
            Data.FileActions.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(FileActionsEnable.IsAppDrive))
                    Refresh();
            };
        }

        Refresh();
    }

    private void Refresh() => IsChecked = _isAllowed() && Value;
}
