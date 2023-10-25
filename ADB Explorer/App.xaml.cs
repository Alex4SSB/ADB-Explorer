using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static string TempFilesPath => $"{Data.IsolatedStorageLocation}\\{AdbExplorerConst.TEMP_FILES_FOLDER}";

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // Restore application-scope property from isolated storage
        IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();
        try
        {
            using IsolatedStorageFileStream stream = new(AdbExplorerConst.APP_SETTINGS_FILE, FileMode.Open, storage);
            Data.IsolatedStorageLocation = Path.GetDirectoryName(stream.GetType().GetField("_fullPath", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stream).ToString());

            if (Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                Process.Start("explorer.exe", Data.IsolatedStorageLocation);
            }

            using StreamReader reader = new(stream);
            // Restore each application-scope property individually
            while (!reader.EndOfStream)
            {
                string[] keyValue = reader.ReadLine().TrimEnd(';').Split(':', 2);
                try
                {
                    var jObj = JsonConvert.DeserializeObject(keyValue[1], new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Objects });
                    if (jObj is JArray jArr)
                        Properties[keyValue[0]] = jArr.Values<string>().ToArray();
                    else
                        Properties[keyValue[0]] = jObj;
                }
                catch (Exception)
                {
                    Properties[keyValue[0]] = keyValue[1];
                }
            }

            Task.Run(() =>
            {
                if (!Directory.Exists(TempFilesPath))
                    Directory.CreateDirectory(TempFilesPath);
                else
                    CleanTempFiles();
            });
        }
        catch // FileNotFoundException ex
        {
            WriteSettings();
        }

        //Select the text in a TextBox when it receives focus.
        EventManager.RegisterClassHandler(typeof(TextBox), TextBox.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(SelectivelyIgnoreMouseButton));
        EventManager.RegisterClassHandler(typeof(TextBox), TextBox.GotKeyboardFocusEvent,
            new RoutedEventHandler(SelectAllText));
        EventManager.RegisterClassHandler(typeof(TextBox), TextBox.MouseDoubleClickEvent,
            new RoutedEventHandler(SelectAllText));
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        Data.FileOpQ.Stop();
        WriteSettings();
        Task.Run(CleanTempFiles);

        if (Data.Settings.UnrootOnDisconnect is true)
            ADBService.Unroot(Data.CurrentADBDevice);
    }

    private void WriteSettings()
    {
        // Persist application-scope property to isolated storage
        IsolatedStorageFile storage = IsolatedStorageFile.GetUserStoreForDomain();

        if (Data.RuntimeSettings.ResetAppSettings)
        {
            storage.DeleteFile(AdbExplorerConst.APP_SETTINGS_FILE);
            return;
        }

        using IsolatedStorageFileStream stream = new(AdbExplorerConst.APP_SETTINGS_FILE, FileMode.Create, storage);
        using StreamWriter writer = new(stream);

        // Persist each application-scope property individually
        foreach (string key in from string key in Properties.Keys
                               orderby key
                               select key)
        {
            writer.WriteLine($"{key}:{JsonConvert.SerializeObject(Properties[key], new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Objects })};");
        }
    }

    private void CleanTempFiles()
    {
        try
        {
            foreach (var file in Directory.GetFiles(TempFilesPath))
            {
                File.Delete(file);
            }
        }
        catch (Exception)
        { }
    }

    private void SelectivelyIgnoreMouseButton(object sender, MouseButtonEventArgs e)
    {
        // Find the TextBox
        DependencyObject parent = e.OriginalSource as UIElement;
        while (parent is not null and not TextBox)
            parent = VisualTreeHelper.GetParent(parent);

        if (parent is not null)
        {
            var textBox = (TextBox)parent;
            if (!textBox.IsKeyboardFocusWithin)
            {
                // If the text box is not yet focused, give it the focus and
                // stop further processing of this click event.
                textBox.Focus();
                e.Handled = true;
            }
        }
    }

    private void SelectAllText(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Handle error 0x800401D0 (CLIPBRD_E_CANT_OPEN) - global WPF issue
        if (e.Exception is COMException comException && comException.ErrorCode == -2147221040)
            e.Handled = true;
    }
}
