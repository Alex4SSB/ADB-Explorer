using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public class ToggleMenu : ViewModelBase
{
    public ObservableProperty<bool> IsChecked { get; set; } = new();

    public ObservableProperty<string> Description { get; private set; } = new();

    public ObservableProperty<BaseIcon> Icon { get; private set; } = new();

    public FileAction FileAction { get; }

    public DualActionButton Button { get; }

    public ToggleMenu(FileAction.FileActionType type,
                      Func<bool> canExecute,
                      string checkedDescription,
                      BaseIcon checkedIcon,
                      Action action,
                      string uncheckedDescription = "",
                      BaseIcon uncheckedIcon = null,
                      KeyGesture gesture = null,
                      Brush checkBackground = null,
                      IEnumerable<SubMenu> children = null,
                      ObservableProperty<bool> isVisible = null,
                      bool toggleOnClick = true,
                      bool clearClipboard = false)
    {
        uncheckedDescription = string.IsNullOrEmpty(uncheckedDescription) ? checkedDescription : uncheckedDescription;
        uncheckedIcon ??= checkedIcon;

        IsChecked.Value = false;
        Description.Value = uncheckedDescription;
        Icon.Value = uncheckedIcon;

        FileAction = new(type, canExecute, action, Description, gesture, gesture is not null, clearClipboard);
        Button = new(FileAction, Icon, IsChecked, checkBackground: checkBackground, children: children, isVisible: isVisible, isCheckable: toggleOnClick);

        IsChecked.PropertyChanged += (object sender, PropertyChangedEventArgs<bool> e) =>
        {
            Description.Value = e.NewValue ? checkedDescription : uncheckedDescription;
            Icon.Value = e.NewValue ? checkedIcon : uncheckedIcon;
        };
    }

    public void Toggle(bool? toggle = null)
    {
        if (toggle is null || IsChecked.Value != toggle.Value)
        {
            IsChecked.Value ^= true;
            FileAction.Command.Execute();
        }
    }
}
