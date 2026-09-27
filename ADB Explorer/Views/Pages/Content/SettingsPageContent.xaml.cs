namespace ADB_Explorer.Views.Pages;

public partial class SettingsPageContent : UserControl
{
    public SettingsPageContent()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        DialogService.ShowMessage(Strings.Resources.S_HELP_ON_ADB, Strings.Resources.S_HELP_ON_ADB_TITLE, DialogService.DialogIcon.Informational);
    }
}
