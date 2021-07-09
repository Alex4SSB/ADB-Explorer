using System.IO;
using System.IO.IsolatedStorage;
using System.Windows;

namespace ADB_Explorer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly string filename = "App.txt";

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Restore application-scope property from isolated storage
            IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
            try
            {
                using IsolatedStorageFileStream stream = new(filename, FileMode.Open, storage);
                using StreamReader reader = new(stream);
                // Restore each application-scope property individually
                while (!reader.EndOfStream)
                {
                    string[] keyValue = reader.ReadLine().Split(new char[] { ',' });
                    Properties[keyValue[0]] = keyValue[1];
                }
            }
            catch // FileNotFoundException ex
            {
                // Handle when file is not found in isolated storage:
                // * When the first application session
                // * When file has been deleted
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            // Persist application-scope property to isolated storage
            IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
            using IsolatedStorageFileStream stream = new(filename, FileMode.Create, storage);
            using StreamWriter writer = new(stream);
            // Persist each application-scope property individually
            foreach (string key in Properties.Keys)
            {
                writer.WriteLine("{0},{1}", key, Properties[key]);
            }
        }
    }
}
