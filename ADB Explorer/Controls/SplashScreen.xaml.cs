using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for SplashScreen.xaml
/// </summary>
public partial class SplashScreen : UserControl
{
    public SplashScreen()
    {
        InitializeComponent();

        FirstLaunchGrid.Visibility = Data.Settings.ShowWelcomeScreen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        ConfirmButton.IsEnabled = true;
        ExplanationPanel.Visibility = Visibility.Visible;

        DeployTextBlock.Visibility = DeployRadioButton.IsChecked is true ? Visibility.Visible : Visibility.Hidden;
        CmdTextBlock.Visibility = CmdRadioButton.IsChecked is true ? Visibility.Visible : Visibility.Hidden;
        DiskUsageTextBlock.Visibility = DiskUsageRadioButton.IsChecked is true ? Visibility.Visible : Visibility.Hidden;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeployRadioButton.IsChecked is true)
            Data.Settings.AdbProgressMethod = Services.AppSettings.ProgressMethod.Redirection;
        else if (CmdRadioButton.IsChecked is true)
            Data.Settings.AdbProgressMethod = Services.AppSettings.ProgressMethod.Console;
        else if (DiskUsageRadioButton.IsChecked is true)
            Data.Settings.AdbProgressMethod = Services.AppSettings.ProgressMethod.DiskUsage;
        else
            throw new NotSupportedException("Unsupported progress method");

        Data.Settings.ShowWelcomeScreen =
        Data.RuntimeSettings.IsSplashScreenVisible = false;

        AdbHelper.VerifyProgressRedirection();
    }
}
