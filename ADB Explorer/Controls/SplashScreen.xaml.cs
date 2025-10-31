using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SplashScreen.xaml
/// </summary>
public partial class SplashScreen
{
    public SplashScreen()
    {
        InitializeComponent();

        Init();
    }

    public async void Init()
    {
        await AsyncHelper.WaitUntil(() => Data.RuntimeSettings.AdbVersion is not null, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200), CancellationToken.None);

        MissingAdbGrid.Visibility = Data.RuntimeSettings.AdbVersion >= AdbExplorerConst.MIN_ADB_VERSION
            ? Visibility.Collapsed
            : Visibility.Visible;

        _ = Task.Run(async () =>
        {
            while (Data.RuntimeSettings.IsSplashScreenVisible && MissingAdbGrid.Visible())
            {
                Thread.Sleep(1000);
                var validVersion = await AdbHelper.CheckAdbVersion();
                App.Current.Dispatcher.Invoke(() => CloseAdbScreenButton.IsEnabled = validVersion);
            }
        });
    }

    private static void CloseSplashScreen()
    {
        //Data.Settings.AdvancedDragSet = true;
        Data.RuntimeSettings.FinalizeSplash = true;
    }

    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsHelper.ChangeAdbPathAction();

        if (Data.RuntimeSettings.AdbVersion >= AdbExplorerConst.MIN_ADB_VERSION)
            CloseAdbScreenButton.IsEnabled = true;
    }

    private void CloseAdbScreenButton_Click(object sender, RoutedEventArgs e)
    {
        MissingAdbGrid.Visibility = Visibility.Collapsed;

        CloseSplashScreen();
    }

    private void HelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogService.ShowMessage(Strings.Resources.S_HELP_ON_ADB, Strings.Resources.S_HELP_ON_ADB_TITLE, DialogService.DialogIcon.Informational);
    }

    private void ConfirmAdvancedDrag_Click(object sender, RoutedEventArgs e)
    {
        CloseSplashScreen();
    }
}
