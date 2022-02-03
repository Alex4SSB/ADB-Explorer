using System.Windows.Controls;

namespace ADB_Explorer.Helpers
{
    public partial class FluentForwarder : UserControl
    {
        public bool UseFluentStyles => MainWindow.UseFluentStyles;
        public FluentForwarder()
        {
            InitializeComponent();
        }
    }
}
