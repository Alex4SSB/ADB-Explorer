using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Windows;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    [ObservableProperty]
    public partial ICollectionView SettingsList { get; set; }

    [ObservableProperty]
    public partial SettingsGroup SelectedGroup { get; set; }

    [ObservableProperty]
    public partial ICollectionView GroupContent { get; set; }

    [ObservableProperty]
    public partial ICollectionView SortedSettings { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

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
            if (e.PropertyName == nameof(AppSettings.Theme) || e.PropertyName == nameof(AppSettings.UseCustomAccent))
            {
                AdbThemeService.SetTheme(Data.Settings.Theme);
                AdbThemeService.SetAccent(Data.Settings.UseCustomAccent ? Data.Settings.AccentColor : null);
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

        var navEnabled = App.Services.GetService<MainWindowViewModel>()!.IsNavigationEnabled;
        if (navEnabled)
            SelectedGroup = (SettingsGroup)UISettings.SettingsList.FirstOrDefault();
        else
        {
            SelectedGroup = UISettings.SettingsList.OfType<SettingsGroup>().FirstOrDefault(group => group.Name == Strings.Resources.S_SETTINGS_GROUP_WORK_DIRS);
        }

        SortedSettings = CollectionViewSource.GetDefaultView(UISettings.SortSettings);
        SortedSettings.Filter = SettingsFilterPredicate;

        Data.RuntimeSettings.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(AppRuntimeSettings.AdbStatus))
            {
                TranslateAdbStatus();
            }
        };

        TranslateAdbStatus();

        _isInitialized = true;
    }

    private void TranslateAdbStatus()
    {
        AdbStatus = Data.RuntimeSettings.AdbStatus switch
        {
            AdbHelper.AdbStatus.NotFound => string.Format(Strings.Resources.S_ADB_NOT_FOUND, Strings.Resources.S_SETTINGS_OVERRIDE_ADB),
            AdbHelper.AdbStatus.PathInvalid => Strings.Resources.S_ADB_PATH_INVALID,
            AdbHelper.AdbStatus.Compromised => Strings.Resources.S_ADB_COMPROMISED,
            AdbHelper.AdbStatus.VersionUnknown => Strings.Resources.S_MISSING_ADB_OVERRIDE,
            AdbHelper.AdbStatus.Outdated => Strings.Resources.S_ADB_VERSION_LOW_OVERRIDE,
            AdbHelper.AdbStatus.Valid => null,
            _ => throw new InvalidOperationException("Unknown ADB status")
        };
    }

    [ObservableProperty]
    public partial string? AdbStatus { get; set; }

    [ObservableProperty]
    public partial bool SortedView { get; set; }

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
