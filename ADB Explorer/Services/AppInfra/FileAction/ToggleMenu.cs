﻿using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

internal class ToggleMenu : ViewModelBase
{
    public ObservableProperty<bool> IsChecked { get; set; } = new();

    public ObservableProperty<string> Description { get; private set; } = new();

    public ObservableProperty<string> Icon { get; private set; } = new();

    public FileAction FileAction { get; }

    public DualActionButton Button { get; }

    public ToggleMenu(FileAction.FileActionType type,
                      Func<bool> canExecute,
                      string checkedDescription,
                      string checkedIcon,
                      Action action,
                      string uncheckedDescription = "",
                      string uncheckedIcon = "",
                      KeyGesture gesture = null,
                      Brush checkBackground = null,
                      ObservableProperty<bool> isVisible = null)
    {
        uncheckedDescription = string.IsNullOrEmpty(uncheckedDescription) ? checkedDescription : uncheckedDescription;
        uncheckedIcon = string.IsNullOrEmpty(uncheckedIcon) ? checkedIcon : uncheckedIcon;

        IsChecked.Value = false;
        Description.Value = uncheckedDescription;
        Icon.Value = uncheckedIcon;

        FileAction = new(type, canExecute, action, Description, gesture, gesture is not null);
        Button = new(FileAction, Icon, IsChecked, checkBackground: checkBackground, isVisible: isVisible);

        IsChecked.PropertyChanged += (object sender, PropertyChangedEventArgs<bool> e) =>
        {
            Description.Value = e.NewValue ? checkedDescription : uncheckedDescription;
            Icon.Value = e.NewValue ? checkedIcon : uncheckedIcon;
        };
    }
}