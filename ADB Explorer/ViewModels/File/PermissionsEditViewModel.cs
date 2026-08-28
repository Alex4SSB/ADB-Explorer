using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public partial class PermissionsEditViewModel : ObservableObject
{
    public ObservableCollection<string> KnownUsers { get; } = [];
    public ObservableCollection<string> KnownGroups { get; } = [];

    [ObservableProperty]
    public partial bool UserRead { get; set; }

    [ObservableProperty]
    public partial bool UserWrite { get; set; }

    [ObservableProperty]
    public partial bool UserExecute { get; set; }

    [ObservableProperty]
    public partial bool GroupRead { get; set; }

    [ObservableProperty]
    public partial bool GroupWrite { get; set; }

    [ObservableProperty]
    public partial bool GroupExecute { get; set; }

    [ObservableProperty]
    public partial bool OtherRead { get; set; }

    [ObservableProperty]
    public partial bool OtherWrite { get; set; }

    [ObservableProperty]
    public partial bool OtherExecute { get; set; }

    [ObservableProperty]
    public partial string? SelectedUser { get; set; }

    [ObservableProperty]
    public partial string? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial bool CanChangeMode { get; set; }

    [ObservableProperty]
    public partial bool CanChangeOwner { get; set; }

    [ObservableProperty]
    public partial bool CanChangeGroup { get; set; }

    private UnixFileMode _originalMode;
    private string? _originalUser;
    private string? _originalGroup;

    public void BeginEdit(FileClass file, ShellAccessHelper.UnixPermissionChanges allowed)
    {
        CanChangeMode = allowed.Mode;
        CanChangeOwner = allowed.Owner;
        CanChangeGroup = allowed.Group;

        var mode = file.Permissions ?? 0;
        _originalMode = mode;
        _originalUser = file.User;
        _originalGroup = file.Group;

        UserRead = mode.HasFlag(UnixFileMode.UserRead);
        UserWrite = mode.HasFlag(UnixFileMode.UserWrite);
        UserExecute = mode.HasFlag(UnixFileMode.UserExecute);
        GroupRead = mode.HasFlag(UnixFileMode.GroupRead);
        GroupWrite = mode.HasFlag(UnixFileMode.GroupWrite);
        GroupExecute = mode.HasFlag(UnixFileMode.GroupExecute);
        OtherRead = mode.HasFlag(UnixFileMode.OtherRead);
        OtherWrite = mode.HasFlag(UnixFileMode.OtherWrite);
        OtherExecute = mode.HasFlag(UnixFileMode.OtherExecute);

        SelectedUser = file.User;
        SelectedGroup = file.Group;
    }

    public void SetKnownIdentities(IEnumerable<string> users, IEnumerable<string> groups, string? currentUser, string? currentGroup)
    {
        ReplaceItems(KnownUsers, users);
        ReplaceItems(KnownGroups, groups);
        SelectedUser = currentUser;
        SelectedGroup = currentGroup;
    }

    public UnixFileMode EditedMode => ShellAccessHelper.ComposeMode(
        UserRead, UserWrite, UserExecute,
        GroupRead, GroupWrite, GroupExecute,
        OtherRead, OtherWrite, OtherExecute);

    public bool ModeChanged => EditedMode != _originalMode;

    public bool UserChanged => !string.Equals(NormalizeIdentity(SelectedUser), NormalizeIdentity(_originalUser), StringComparison.Ordinal);

    public bool GroupChanged => !string.Equals(NormalizeIdentity(SelectedGroup), NormalizeIdentity(_originalGroup), StringComparison.Ordinal);

    public async Task<string?> ApplyAsync(FileClass file, string deviceId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var noDeref = file.IsLink;

        if (ModeChanged && CanChangeMode)
        {
            var error = await ADBService.ChangeFileModeAsync(deviceId, file.FullPath, EditedMode, cancellationToken);
            if (!string.IsNullOrEmpty(error))
                errors.Add(error);
        }

        var user = NormalizeIdentity(SelectedUser);
        if (UserChanged && CanChangeOwner && user is not null)
        {
            var error = await ADBService.ChangeFileUserAsync(deviceId, file.FullPath, user, noDeref, cancellationToken);
            if (!string.IsNullOrEmpty(error))
                errors.Add(error);
        }

        var group = NormalizeIdentity(SelectedGroup);
        if (GroupChanged && CanChangeGroup && group is not null)
        {
            var error = await ADBService.ChangeFileGroupAsync(deviceId, file.FullPath, group, noDeref, cancellationToken);
            if (!string.IsNullOrEmpty(error))
                errors.Add(error);
        }

        if (errors.Count == 0)
            return null;

        return string.Join(Environment.NewLine, errors.Distinct(StringComparer.Ordinal));
    }

    private static string? NormalizeIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
