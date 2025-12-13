using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
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

    [RelayCommand]
    private void SponsorButton()
    {
        Process.Start(Data.RuntimeSettings.DefaultBrowserPath, $"\"{Links.SPONSOR}\"");
    }

    private DateTime appDataClick = DateTime.MinValue;

    [RelayCommand]
    private void AppDataHyperlink()
    {
        if (DateTime.Now - appDataClick < AdbExplorerConst.LINK_CLICK_DELAY)
            return;

        appDataClick = DateTime.Now;
        Process.Start("explorer.exe", Data.AppDataPath);
    }

    [RelayCommand]
    private async Task AndroidRobotLicense()
    {
        SimpleStackPanel stack = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = Strings.Resources.S_ANDROID_ROBOT_LIC,
                },
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = Strings.Resources.S_APK_ICON_LIC,
                },
                new Wpf.Ui.Controls.HyperlinkButton()
                {
                    Content = Strings.Resources.S_CC_NAME,
                    ToolTip = Links.L_CC_LIC,
                    NavigateUri = Links.L_CC_LIC.OriginalString,
                    HorizontalAlignment = HorizontalAlignment.Center,
                }
            },
        };

        App.Current.Dispatcher.Invoke(() =>
        {
            DialogService.ShowContent(stack, Strings.Resources.S_ANDROID_ICONS_TITLE, DialogService.DialogIcon.Informational);
        });
    }
}
