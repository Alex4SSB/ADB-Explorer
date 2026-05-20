using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls.Pages;

/// <summary>
/// Interaction logic for SettingsPageHeader.xaml
/// </summary>
public partial class SettingsPageHeader : UserControl
{
    public SettingsPageHeader()
    {
        InitializeComponent();

        Data.Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.EnableMdns):
                AdbHelper.EnableMdns();
                break;
            default:
                break;
        }
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        DialogService.ShowMessage(Strings.Resources.S_HELP_ON_ADB, Strings.Resources.S_HELP_ON_ADB_TITLE, DialogService.DialogIcon.Informational);
    }
}
