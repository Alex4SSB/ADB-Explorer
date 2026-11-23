namespace ADB_Explorer.Platform;

/// <summary>
/// Marshals work to the UI thread, so Core code doesn't depend on a UI framework's dispatcher.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Whether the calling thread is the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> on the UI thread without waiting for it.</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> on the UI thread and completes when it has run.</summary>
    Task InvokeAsync(Action action);
}
