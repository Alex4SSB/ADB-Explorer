using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.ViewModels.Windows;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ADB_Explorer.Views.Windows
{
    public partial class MainWindow : INavigationWindow
    {
        public MainWindowViewModel ViewModel { get; }

        public MainWindow(
            MainWindowViewModel viewModel,
            INavigationViewPageProvider navigationViewPageProvider,
            INavigationService navigationService,
            IContentDialogService contentDialogService
        )
        {
            ViewModel = viewModel;
            DataContext = this;

            SystemThemeWatcher.Watch(this);
            AdbThemeService.SetTheme(Data.Settings.Theme);
            AdbHelper.CheckAdbVersion();
            Data.DevicesObject = new();

            InitializeComponent();
            SetPageService(navigationViewPageProvider);
            contentDialogService.SetDialogHost(RootContentDialog);

            navigationService.SetNavigationControl(RootNavigation);

            RootNavigation.Navigated += RootNavigation_Navigated;
        }

        private readonly SettingsPageHeader settingsPageHeader = new() { DataContext = App.Services.GetService<SettingsViewModel>() };
        private readonly DevicesPageHeader devicesPageHeader = new() { DataContext = App.Services.GetService<DevicesViewModel>() };

        private void RootNavigation_Navigated(NavigationView sender, NavigatedEventArgs args)
        {
            PageHeader.Content = args.Page switch
            {
                Pages.SettingsPage => settingsPageHeader,
                Pages.DevicesPage => devicesPageHeader,
                _ => null
            };
        }

        #region INavigationWindow methods

        public INavigationView GetNavigation() => RootNavigation;

        public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

        public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) => RootNavigation.SetPageProviderService(navigationViewPageProvider);

        public void ShowWindow() => Show();

        public void CloseWindow() => Close();

        #endregion INavigationWindow methods

        /// <summary>
        /// Raises the closed event.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Make sure that closing this window will begin the process of closing the application.
            Application.Current.Shutdown();
        }

        INavigationView INavigationWindow.GetNavigation()
        {
            throw new NotImplementedException();
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            throw new NotImplementedException();
        }
    }
}
