using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class LogViewModel : ObservableObject, INavigationAware
{
    private bool _isInitialized = false;

    public event Action<Log> LogEntryAdded;
    public event Action LogCleared;
    public event Action RefreshControls;

    public LogViewModel()
    {
        Data.Settings.PropertyChanged += Settings_PropertyChanged;
        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    public Task OnNavigatedToAsync()
    {
        if (!_isInitialized)
            InitializeViewModel();

        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void InitializeViewModel()
    {
        Data.CommandLog.CollectionChanged += CommandLog_CollectionChanged;

        foreach (var entry in Data.CommandLog)
        {
            LogEntryAdded?.Invoke(entry);
        }

        _isInitialized = true;
    }

    private void Cleanup()
    {
        Data.CommandLog.CollectionChanged -= CommandLog_CollectionChanged;
        Data.CommandLog.Clear();

        LogCleared?.Invoke();

        _isInitialized = false;
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.EnableLog) && !Data.Settings.EnableLog)
        {
            Cleanup();
        }
    }

    private void RuntimeSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppRuntimeSettings.ClearLogs))
        {
            LogCleared?.Invoke();
            RefreshControls?.Invoke();
        }
        else if (e.PropertyName is nameof(AppRuntimeSettings.IsLogPaused))
        {
            RefreshControls?.Invoke();
        }
    }

    private void CommandLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add && e.NewItems is not null && !Data.RuntimeSettings.IsLogPaused)
        {
            foreach (Log entry in e.NewItems)
            {
                LogEntryAdded?.Invoke(entry);
            }
        }
    }
}
