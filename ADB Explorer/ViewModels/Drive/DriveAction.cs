using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public abstract class DriveAction : ViewModelBase
{
    protected DriveViewModel drive;

    public virtual bool IsEnabled { get; } = true;

    protected DriveAction(DriveViewModel drive)
    {
        this.drive = drive;
    }
}

public class BrowseCommand : DriveAction
{
    public BrowseCommand(DriveViewModel drive) : base(drive)
    { }

    public void Action()
    {
        Data.RuntimeSettings.BrowseDrive = drive;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}

public class SelectCommand : DriveAction
{
    public SelectCommand(DriveViewModel drive) : base(drive)
    { }

    public void Action()
    {
        drive.DriveSelected = true;
    }

    private ICommand command;
    public ICommand Command => command ??= new CommandHandler(() => Action(), () => IsEnabled);
}
