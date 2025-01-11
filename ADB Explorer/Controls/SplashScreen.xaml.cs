﻿using ADB_Explorer.Helpers;
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
        if (DeployRadioButton.IsChecked is true)
            Data.Settings.UseProgressRedirection = true;
        else if (DiskUsageRadioButton.IsChecked is true)
            Data.Settings.UseProgressRedirection = false;
        else
            throw new NotSupportedException("Unsupported progress method");

        CloseSplashScreen();
    }

    private static void CloseSplashScreen()
    {
        Data.Settings.ShowWelcomeScreen = false;
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
        DialogService.ShowMessage("""
                                  This app is a UI for ADB - Android Debug Bridge.
                                  To use the app, you need to have ADB on your PC.
                                  ADB is available as part of the Android SDK Platform Tools.
                                  You can use the provided link to download the SDK Platform Tools.
                                  Unfortunately, we cannot provide ADB with the app due to licensing restrictions.
                                  """, "Help On ADB", DialogService.DialogIcon.Informational);
    }
}
