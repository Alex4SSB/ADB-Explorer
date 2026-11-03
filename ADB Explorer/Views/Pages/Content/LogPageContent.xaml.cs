namespace ADB_Explorer.Views.Pages;

public partial class LogPageContent : UserControl
{
    public LogPageContent()
    {
        InitializeComponent();

        DataContextChanged += LogPageContent_DataContextChanged;
        Loaded += OnLoaded;
    }

    private void LogPageContent_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LogViewModel oldVm)
        {
            oldVm.LogEntryAdded -= OnLogEntryAdded;
            oldVm.LogCleared -= OnLogCleared;
            oldVm.RefreshControls -= RefreshControls;
        }

        if (e.NewValue is LogViewModel newVm)
        {
            newVm.LogEntryAdded += OnLogEntryAdded;
            newVm.LogCleared += OnLogCleared;
            newVm.RefreshControls += RefreshControls;

            if (IsLoaded)
                newVm.RefreshDisplayedLog();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogViewModel vm)
            vm.RefreshDisplayedLog();
    }

    private void OnLogEntryAdded(Log entry)
    {
        if (entry is null)
            return;

        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded || LogTextBox is null)
                return;

            LogTextBox.AppendText(ShowUnprintable(entry.ToString()) + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }

    private const char CONTROL_PICTURES = '\u2400';

    private const char DELETE_PICTURE = '\u2421';

    /// <summary>Swaps unprintable characters (separators etc.) for their Unicode pictures, which still copy out of the log.</summary>
    private static string ShowUnprintable(string text) => string.Create(text.Length, text, (chars, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            chars[i] = source[i] switch
            {
                '\n' or '\r' or '\t' => source[i],
                < ' ' => (char)(CONTROL_PICTURES + source[i]),
                '\u007F' => DELETE_PICTURE,
                _ => source[i],
            };
        }
    });

    private void OnLogCleared() =>
        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded || LogTextBox is null)
                return;

            LogTextBox.Document.Blocks.Clear();
        });

    private void RefreshControls() =>
        Dispatcher.Invoke(() =>
        {
            if (!IsLoaded || LogControlsPanel is null)
                return;

            LogControlsPanel.Items.Refresh();
        });
}
