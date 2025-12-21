using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.Views.Pages
{
    public partial class SettingsPage : INavigableView<SettingsViewModel>
    {
        public SettingsViewModel ViewModel { get; }

        public SettingsPage(SettingsViewModel viewModel)
        {
            Thread.CurrentThread.CurrentCulture =
            Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();

            Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
        }

        private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppRuntimeSettings.SearchText):
                    FilterSettings();
                    break;
                default:
                    break;
            }
        }

        private void FilterSettings()
        {
            var collectionView = CollectionViewSource.GetDefaultView(SortedSettings.ItemsSource);
            if (collectionView is null)
                return;

            if (string.IsNullOrEmpty(Data.RuntimeSettings.SearchText))
                collectionView.Filter = null;
            else
            {
                collectionView.Filter = sett => ((AbstractSetting)sett).Description.Contains(Data.RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase)
                                                || (sett is EnumSetting enumSett && enumSett.Buttons.Any(button => button.Name.Contains(Data.RuntimeSettings.SearchText, StringComparison.OrdinalIgnoreCase)));
            }
        }
    }
}
