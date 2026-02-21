using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
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

            Initialize();

            InitializeComponent();
            SetPageService(navigationViewPageProvider);
            contentDialogService.SetDialogHost(RootContentDialog);

            navigationService.SetNavigationControl(RootNavigation);

            RootNavigation.Navigated += RootNavigation_Navigated;
        }

        private async void Initialize()
        {
            SystemThemeWatcher.Watch(this);
            AdbThemeService.SetTheme(Data.Settings.Theme);
            Data.DevicesObject = new();

            if (!await AdbHelper.CheckAdbVersion())
            {
                return;
            }

            Data.RuntimeSettings.DefaultBrowserPath = Network.GetDefaultBrowser();
            Data.FileOpQ = new();
            NativeMethods.InterceptClipboard.Init(this, Data.CopyPaste.GetClipboardPasteItems, IpcService.AcceptIpcMessage);
            
            //DeviceHelper.UpdateWsaPkgStatus();
        }

        private SettingsPageHeader SettingsPageHeader
        {
            get
            {
                field ??= new() { DataContext = App.Services.GetService<SettingsViewModel>() };
                return field;
            }
        } = null;

        private DevicesPageHeader DevicesPageHeader
        {
            get
            {
                field ??= new() { DataContext = App.Services.GetService<DevicesViewModel>() };
                return field;
            }
        } = null;

        private ExplorerPageHeader ExplorerPageHeader
        {
            get
            {
                field ??= new() { DataContext = App.Services.GetService<ExplorerViewModel>() };
                return field;
            }
        } = null;

        private void RootNavigation_Navigated(NavigationView sender, NavigatedEventArgs args)
        {
            PageHeader.Content = args.Page switch
            {
                Pages.SettingsPage => SettingsPageHeader,
                Pages.DevicesPage => DevicesPageHeader,
                Pages.ExplorerPage => ExplorerPageHeader,
                _ => null
            };

            Data.RuntimeSettings.IsDevicesView = args.Page is Pages.DevicesPage;
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

        private void FluentWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Navigate(typeof(Pages.DevicesPage));
        }

        private void RootNavigation_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2)
            {
                e.Handled = true;
            }
        }

        private void RootNavigation_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            
        }
    }
}
