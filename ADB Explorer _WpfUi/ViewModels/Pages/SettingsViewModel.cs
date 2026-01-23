using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    private ICollectionView _settingsList;

    [ObservableProperty]
    private SettingsGroup _selectedGroup;

    [ObservableProperty]
    private ICollectionView _groupContent;

    [ObservableProperty]
    private ICollectionView _sortedSettings;

    [ObservableProperty]
    private string _searchText = "";

    Predicate<object> SettingsFilterPredicate => sett =>
        ((AbstractSetting)sett).Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || (sett is EnumSetting enumSett && enumSett.Buttons.Any(button => button.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)));

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

        PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(SearchText):
                    SortedSettings.Refresh();
                    break;

                case nameof(SelectedGroup):
                    GroupContent = CollectionViewSource.GetDefaultView(SelectedGroup.Children);
                    break;

                case nameof(SortedView):
                    if (SortedView)
                        SortedSettings.Refresh();
                    else
                        GroupContent.Refresh();

                    break;

                default:
                    break;
            }
        };

        UISettings.Init();
        SettingsList = CollectionViewSource.GetDefaultView(UISettings.SettingsList);
        SelectedGroup = (SettingsGroup)UISettings.SettingsList.FirstOrDefault();

        SortedSettings = CollectionViewSource.GetDefaultView(UISettings.SortSettings);
        SortedSettings.Filter = SettingsFilterPredicate;

        _isInitialized = true;
    }

    [ObservableProperty]
    private bool _sortedView;

    [RelayCommand]
    private void SortSettings()
    {
        SortedView ^= true;
    }

    [RelayCommand]
    private async Task ResetSettings()
    {
        var result = await DialogService.ShowConfirmation(
                        Strings.Resources.S_RESET_SETTINGS,
                        Strings.Resources.S_RESET_SETTINGS_TITLE,
                        primaryText: Strings.Resources.S_CONFIRM,
                        cancelText: Strings.Resources.S_CANCEL,
                        icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 == Wpf.Ui.Controls.ContentDialogResult.None)
            return;

        Data.RuntimeSettings.ResetAppSettings = true;
    }
}
