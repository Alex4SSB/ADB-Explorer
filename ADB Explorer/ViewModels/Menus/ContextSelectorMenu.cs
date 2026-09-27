namespace ADB_Explorer.ViewModels;

/// <summary>
/// A radio choice inside a context menu's Sort / View submenu. Its dot follows <c>isChecked</c>
/// each time <see cref="Refresh"/> runs, like the items of the toolbar's selector menus.
/// </summary>
public partial class SelectorSubMenu : SubMenu
{
    private readonly Func<bool> _isChecked;
    private readonly Func<BaseIcon>? _createIcon;

    public bool IsRadioButton => true;

    [ObservableProperty]
    public partial bool IsChecked { get; private set; }

    public SelectorSubMenu(string name, Func<BaseIcon>? createIcon, Func<bool> canExecute, Action select, Func<bool> isChecked)
        : base(new(FileAction.FileActionType.None, canExecute, select, name))
    {
        _createIcon = createIcon;
        _isChecked = isChecked;
    }

    /// <summary>The icon is made anew each time, as an element can only sit in one menu.</summary>
    public void Refresh()
    {
        IsChecked = _isChecked();

        if (_createIcon is not null)
            IconContent = _createIcon().IconContent;
    }
}

/// <summary>
/// The context menu's Sort / View entry. Only the currently enabled choices are listed, in
/// sections divided by separators, and its icon is the one of the current choice.
/// </summary>
public class ContextSelectorMenu(FileAction action, Func<BaseIcon> currentIcon, params SelectorSubMenu[][] sections)
    : SubMenu(action)
{
    public void Refresh()
    {
        var children = new List<SubMenu>();

        foreach (var section in sections)
        {
            var enabled = section.Where(choice => choice.Action.Command.IsEnabled).ToList();
            if (enabled.Count == 0)
                continue;

            if (children.Count > 0)
                children.Add(new SubMenuSeparator());

            foreach (var choice in enabled)
            {
                choice.Refresh();
                children.Add(choice);
            }
        }

        Children = children;

        var icon = currentIcon();
        IconContent = icon.IconContent;
        IconSize = (int)icon.Size;
        OnPropertyChanged(nameof(IconSize));
    }
}
