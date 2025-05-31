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

        FirstLaunchGrid.Visibility = Data.Settings.ShowWelcomeScreen && !MissingAdbGrid.Visible()
            ? Visibility.Visible
            : Visibility.Collapsed;

        AdvancedDragPanel.Visibility = !Data.Settings.AdvancedDragSet && !MissingAdbGrid.Visible() && !FirstLaunchGrid.Visible()
            ? Visibility.Visible
            : Visibility.Collapsed;

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

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        ConfirmButton.IsEnabled = true;
        ExplanationPanel.Visibility = Visibility.Visible;

        DeployTextBlock.Visibility = DeployRadioButton.IsChecked is true ? Visibility.Visible : Visibility.Hidden;
        DiskUsageTextBlock.Visibility = DiskUsageRadioButton.IsChecked is true ? Visibility.Visible : Visibility.Hidden;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        FirstLaunchGrid.Visibility = Visibility.Collapsed;

        if (DeployRadioButton.IsChecked is true)
            Data.Settings.UseProgressRedirection = true;
        else if (DiskUsageRadioButton.IsChecked is true)
            Data.Settings.UseProgressRedirection = false;
        else
            throw new NotSupportedException("Unsupported progress method");

        if (Data.Settings.AdvancedDragSet)
            CloseSplashScreen();
        else
            AdvancedDragPanel.Visibility = Visibility.Visible;
    }

    private static void CloseSplashScreen()
    {
        Data.Settings.ShowWelcomeScreen = false;
        Data.Settings.AdvancedDragSet = true;
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

        if (Data.Settings.ShowWelcomeScreen)
            FirstLaunchGrid.Visibility = Visibility.Visible;
        else
            CloseSplashScreen();
    }

    private void HelpButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogService.ShowMessage(Strings.Resources.S_HELP_ON_ADB, Strings.Resources.S_HELP_ON_ADB_TITLE, DialogService.DialogIcon.Informational);
    }

    private void AdvancedDragInfo_Click(object sender, RoutedEventArgs e)
    {
        DialogService.ShowMessage(Strings.Resources.S_ADVANCED_DRAG, Strings.Resources.S_ADVANCED_DRAG_TITLE, DialogService.DialogIcon.Informational);
    }

    private void ConfirmAdvancedDrag_Click(object sender, RoutedEventArgs e)
    {
        AdvancedDragPanel.Visibility = Visibility.Collapsed;

        if (AdvancedDragEnabledRadioButton.IsChecked is true)
            Data.Settings.AdvancedDrag = true;
        else if (AdvancedDragDisabledRadioButton.IsChecked is true)
            Data.Settings.AdvancedDrag = false;
        
        CloseSplashScreen();
    }

    private void RadioButton_Click(object sender, RoutedEventArgs e)
    {
        ConfirmAdvancedDrag.IsEnabled = true;
    }
}
