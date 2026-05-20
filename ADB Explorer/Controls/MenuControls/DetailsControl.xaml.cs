using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsControl.xaml
/// </summary>
public partial class DetailsControl : UserControl
{

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register("IsChecked", typeof(bool),
          typeof(DetailsControl), new PropertyMetadata(false));

    public DetailsPane.SidePaneMode Mode
    {
        get => (DetailsPane.SidePaneMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register("Mode", typeof(DetailsPane.SidePaneMode),
          typeof(DetailsControl), new PropertyMetadata(DetailsPane.SidePaneMode.Details));

    public DetailsControl()
    {
        InitializeComponent();

        Data.Settings.PropertyChanged += Settings_PropertyChanged;
        Data.FileActions.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.SidePane)
            or nameof(FileActionsEnable.IsDriveViewVisible)
            or nameof(FileActionsEnable.IsAppDrive)
            or nameof(FileActionsEnable.IsRecycleBin))
        {
            Mode = IsPreviewAllowed()
                ? Data.Settings.SidePane
                : DetailsPane.SidePaneMode.Details;
        }
    }

    private static bool IsPreviewAllowed() => !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive && !Data.FileActions.IsDriveViewVisible;
}
