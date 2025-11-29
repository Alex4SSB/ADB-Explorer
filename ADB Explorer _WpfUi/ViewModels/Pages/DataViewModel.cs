using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages
{
    public partial class DataViewModel : ObservableObject, INavigationAware
    {
        private bool _isInitialized = false;

        //[ObservableProperty]
        //private IEnumerable<DataColor> _colors;

        public Task OnNavigatedToAsync()
        {
            if (!_isInitialized)
                InitializeViewModel();

            return Task.CompletedTask;
        }

        public Task OnNavigatedFromAsync() => Task.CompletedTask;

        private void InitializeViewModel()
        {
            //var random = new Random();
            //var colorCollection = new List<DataColor>();

            //for (int i = 0; i < 8192; i++)
            //    colorCollection.Add(
            //        new DataColor
            //        {
            //            Color = new SolidColorBrush(
            //                Color.FromArgb(
            //                    (byte)200,
            //                    (byte)random.Next(0, 250),
            //                    (byte)random.Next(0, 250),
            //                    (byte)random.Next(0, 250)
            //                )
            //            )
            //        }
            //    );

            //Colors = colorCollection;

            _isInitialized = true;
        }
    }
}
