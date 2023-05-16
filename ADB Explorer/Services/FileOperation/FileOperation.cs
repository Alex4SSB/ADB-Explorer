using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Services;

public abstract class FileOperation : ViewModelBase
{
    public enum OperationStatus
    {
        Waiting,
        InProgress,
        Completed,
        Canceled,
        Failed,
    }

    public enum OperationType
    {
        Push,
        Pull,
        Move,
        Delete,
        Recycle,
        Copy,
        Restore,
        Install,
        Update,
    }

    private OperationType operationType;
    public OperationType OperationName
    {
        get => operationType;
        protected set
        {
            if (Set(ref operationType, value))
            {
                OpIcon = new(value);
                OnPropertyChanged(nameof(CompletedStatsVisible));
                OnPropertyChanged(nameof(FinishedIconVisible));
            }
        }
    }

    public Dispatcher Dispatcher { get; }

    public ADBService.AdbDevice Device { get; }
    public FilePath FilePath { get; }

    private OperationStatus status;
    public OperationStatus Status
    {
        get => status;
        protected set
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (Set(ref status, value))
                {
                    OnPropertyChanged(nameof(CompletedStatsVisible));
                    OnPropertyChanged(nameof(FinishedIconVisible));
                }
            });
        }
    }

    public bool CompletedStatsVisible => Status is OperationStatus.Completed
                                         && OperationName is OperationType.Push or OperationType.Pull;

    public bool FinishedIconVisible => ((OperationName is not OperationType.Push and not OperationType.Pull) || Status is OperationStatus.Canceled or OperationStatus.Failed)
                                       && Status is not OperationStatus.InProgress and not OperationStatus.Waiting;

    public OperationIcon OpIcon { get; private set; }

    private object statusInfo;
    public object StatusInfo
    {
        get => statusInfo;
        protected set => Dispatcher.Invoke(() => Set(ref statusInfo, value));
    }

    public FilePath TargetPath { get; set; }

    public FileOperation(Dispatcher dispatcher, ADBService.AdbDevice adbDevice, FilePath filePath)
    {
        Dispatcher = dispatcher;
        Device = adbDevice;
        FilePath = filePath;
        Status = OperationStatus.Waiting;
    }

    public abstract void Start();
    public abstract void Cancel();
}

public class OperationIcon
{
    public string PrimaryIcon { get; }
    public string SecondaryIcon { get; }
    public Thickness SecondaryMargin { get; }
    public double SecondarySize { get; }
    public HorizontalAlignment PrimaryAlignment { get; }

    public OperationIcon(FileOperation.OperationType operation)
    {
        PrimaryIcon = operation switch
        {
            FileOperation.OperationType.Pull or FileOperation.OperationType.Push => "\uE8EA",
            FileOperation.OperationType.Move => "\uE8DE",
            FileOperation.OperationType.Recycle or FileOperation.OperationType.Delete => "\uE74D",
            FileOperation.OperationType.Copy => "\uE8C8",
            FileOperation.OperationType.Restore => "\uE845",
            FileOperation.OperationType.Install => "\uE7B8",
            FileOperation.OperationType.Update => "\uE787",
            _ => throw new System.NotImplementedException(),
        };

        SecondaryIcon = operation switch
        {
            FileOperation.OperationType.Recycle => "\uF143",
            FileOperation.OperationType.Push => "\uE973",
            FileOperation.OperationType.Pull => "\uE974",
            _ => "",
        };

        SecondaryMargin = operation switch
        {
            FileOperation.OperationType.Pull or FileOperation.OperationType.Push => new(14, 0, 0, 0),
            FileOperation.OperationType.Recycle => new(0, 0, -26, -15),
            FileOperation.OperationType.Install => new(0, 0, -28, -18),
            _ => new(0, 0, 0, 0),
        };

        SecondarySize = operation switch
        {
            FileOperation.OperationType.Pull or FileOperation.OperationType.Push => 14,
            FileOperation.OperationType.Install => 10,
            _ => 20,
        };

        PrimaryAlignment = operation switch
        {
            FileOperation.OperationType.Pull or FileOperation.OperationType.Push => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Right,
        };
    }
}
