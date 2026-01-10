using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages;

public partial class SettingsPage : INavigableView<SettingsViewModel>
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        Thread.CurrentThread.CurrentCulture =
        Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
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

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            default:
                break;
        }
    }
}
