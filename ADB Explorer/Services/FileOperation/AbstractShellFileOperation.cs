namespace ADB_Explorer.Services;

public abstract class AbstractShellFileOperation : FileOperation
{
    public override FileClass FilePath { get; }

    public override SyncFile AndroidPath => TargetPath;

    protected AbstractShellFileOperation(FileClass filePath, LogicalDeviceViewModel device, Dispatcher dispatcher)
        : base(filePath, device, dispatcher)
    {
        FilePath = filePath;
        TargetPath = new(filePath);
    }

    public override void ClearChildren()
    {
        if (AndroidPath is null)
            return;

        AndroidPath.Children.Clear();
        AndroidPath.ProgressUpdates.Clear();
    }

    public override void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates)
        => AndroidPath?.AddUpdates(newUpdates);

    public override void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AndroidPath?.AddUpdates(newUpdates);

    protected void BeginInProgress()
    {
        if (Status == OperationStatus.InProgress)
            throw new Exception("Cannot start an already active operation!");

        Status = OperationStatus.InProgress;
        StatusInfo = new InProgShellProgressViewModel();
    }

    protected void SetCompleted()
    {
        Status = OperationStatus.Completed;
        StatusInfo = new CompletedShellProgressViewModel();
    }

    protected void SetCanceled()
    {
        Status = OperationStatus.Canceled;
        StatusInfo = new CanceledOpProgressViewModel();
    }

    protected void SetFailed(string statusText)
    {
        Status = OperationStatus.Failed;
        StatusInfo = new FailedOpProgressViewModel(statusText);
    }

    protected void SetFailedWithError(string message)
        => SetFailed(FileOpStatusFormatter.StatusString(typeof(ShellErrorInfo), failed: -1, message: message, total: true));

    /// <summary>Empty shell output means success; anything else is shown as the failure text.</summary>
    protected void SetShellResult(string result)
    {
        if (result == "")
            SetCompleted();
        else
            SetFailed(result);
    }

    /// <summary>Result of an archive-building task: empty on success, "Canceled", or an error message.</summary>
    protected void SetArchiveResult(string result)
    {
        if (result == "")
            SetCompleted();
        else if (CancelTokenSource?.IsCancellationRequested == true || result == "Canceled")
            SetCanceled();
        else
            SetFailedWithError(result);
    }

    /// <summary>Like <see cref="SetShellResult"/>, but parses the output into per-path shell errors.</summary>
    protected void SetParsedShellResult(string result)
    {
        if (result == "")
        {
            SetCompleted();
            return;
        }

        Status = OperationStatus.Failed;

        var updates = AdbRegEx.RE_SHELL_ERROR().Matches(result)
            .Where(m => m.Success)
            .Select(m => new ShellErrorInfo(m, FilePath.FullPath))
            .ToList();

        AddUpdates(updates);

        var message = updates.Count > 0 ? updates[^1].Message : result;
        if (message.Contains(':'))
            message = message.Split(':').Last().TrimStart();

        StatusInfo = new FailedOpProgressViewModel(FileOpStatusFormatter.StatusString(typeof(ShellErrorInfo),
            failed: Children.Count > 0 ? updates.Count : -1,
            message: message,
            total: true));
    }

    /// <summary>
    /// Sets the final status when <paramref name="task"/> ends. <paramref name="onAborted"/> runs on cancel or fault.
    /// </summary>
    protected void TrackTask(Task task, string faultFallback, Action? onCompleted = null, Action? onAborted = null)
    {
        task.ContinueWith(_ => (onCompleted ?? SetCompleted)(), TaskContinuationOptions.OnlyOnRanToCompletion);

        task.ContinueWith(_ =>
        {
            onAborted?.Invoke();
            SetCanceled();
        }, TaskContinuationOptions.OnlyOnCanceled);

        task.ContinueWith(t =>
        {
            onAborted?.Invoke();
            SetFailedWithError(t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? faultFallback);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    protected void TrackTask(Task<string> task, string faultFallback, Action<string> onResult, Action? onAborted = null)
        => TrackTask((Task)task, faultFallback, () => onResult(task.Result), onAborted);
}
