using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private ObservableList<AbstractGroup> _settingsList = [];

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        Data.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Theme))
            {
                AdbThemeService.SetTheme(Data.Settings.Theme);
            }
        };

        if (SettingsList.Count == 0)
        {
            UISettings.Init();
            SettingsList = UISettings.SettingsList;
        }
        
        _isInitialized = true;
    }

}
