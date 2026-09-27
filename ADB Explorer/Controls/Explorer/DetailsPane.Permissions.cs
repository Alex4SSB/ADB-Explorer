namespace ADB_Explorer.Controls;

public partial class DetailsPane
{
    public bool IsEditingPermissions
    {
        get => (bool)GetValue(IsEditingPermissionsProperty);
        set => SetValue(IsEditingPermissionsProperty, value);
    }

    public static readonly DependencyProperty IsEditingPermissionsProperty =
        DependencyProperty.Register(nameof(IsEditingPermissions), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false, OnIsEditingPermissionsChanged));

    private static void OnIsEditingPermissionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DetailsPane pane) return;

        pane.EditPermissionsTooltip = pane.IsEditingPermissions
            ? Strings.Resources.S_CANCEL
            : Strings.Resources.S_MENU_EDIT;

        pane.UpdateIsEditorFocused();

        if (pane.IsEditingPermissions)
            pane.BeginPermissionsEdit();
    }

    public bool CanEditPermissions
    {
        get => (bool)GetValue(CanEditPermissionsProperty);
        set => SetValue(CanEditPermissionsProperty, value);
    }

    public static readonly DependencyProperty CanEditPermissionsProperty =
        DependencyProperty.Register(nameof(CanEditPermissions), typeof(bool),
          typeof(DetailsPane), new PropertyMetadata(false));

    public string EditPermissionsTooltip
    {
        get => (string)GetValue(EditPermissionsTooltipProperty);
        set => SetValue(EditPermissionsTooltipProperty, value);
    }

    public static readonly DependencyProperty EditPermissionsTooltipProperty =
        DependencyProperty.Register(nameof(EditPermissionsTooltip), typeof(string),
          typeof(DetailsPane), new PropertyMetadata(Strings.Resources.S_BUTTON_CHANGE));

    public ObservableCollection<IDetailsViewModel> PermissionsItems { get; } = [];

    public PermissionsEditViewModel PermissionsEdit { get; } = new();

    public AsyncRelayCommand SavePermissionsCommand { get; }

    public RelayCommand EditPermissionsCommand { get; }

    private LogicalDeviceViewModel? _permissionDevice;

    private void UpdateCanEditPermissions()
    {
        var allowed = DriveHelper.GetEditableUnixChanges(File, Data.ActiveDevice);
        CanEditPermissions = allowed.Any;
        if (!CanEditPermissions && IsEditingPermissions)
            IsEditingPermissions = false;
    }

    private void SubscribePermissionDevice()
    {
        var device = Data.ActiveDevice;
        if (_permissionDevice == device)
            return;

        UnsubscribePermissionDevice();
        _permissionDevice = device;
        if (device is not null)
            device.PropertyChanged += OnPermissionDeviceChanged;
    }

    private void UnsubscribePermissionDevice()
    {
        if (_permissionDevice is null)
            return;

        _permissionDevice.PropertyChanged -= OnPermissionDeviceChanged;
        _permissionDevice = null;
    }

    private void OnPermissionDeviceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(LogicalDeviceViewModel.HasRootShell)
            and not nameof(LogicalDeviceViewModel.Mounts))
            return;

        App.SafeInvoke(UpdateCanEditPermissions);
    }

    private void BeginPermissionsEdit()
    {
        if (File is not { } file)
        {
            IsEditingPermissions = false;
            return;
        }

        var device = Data.ActiveDevice;
        var allowed = DriveHelper.GetEditableUnixChanges(file, device);
        if (!allowed.Any)
        {
            IsEditingPermissions = false;
            return;
        }

        PermissionsEdit.BeginEdit(file, allowed);
        LoadKnownIdentities(file, device);
    }

    private void LoadKnownIdentities(FileClass file, LogicalDeviceViewModel? device)
    {
        if (device is null)
        {
            IEnumerable<string> users = file.User is null ? [] : [file.User];
            IEnumerable<string> groups = file.Group is null ? [] : [file.Group];
            PermissionsEdit.SetKnownIdentities(users, groups, file.User, file.Group);
            return;
        }

        device.RecordUnixIdentity(file.User, file.Group);
        PermissionsEdit.SetKnownIdentities(
            device.GetKnownUsersOrdered(file.User),
            device.GetKnownGroupsOrdered(file.Group),
            file.User,
            file.Group);

        _ = Task.Run(() =>
        {
            device.EnsureKnownIdentities();
            App.SafeInvoke(() =>
            {
                if (!IsEditingPermissions || !ReferenceEquals(File, file))
                    return;

                PermissionsEdit.SetKnownIdentities(
                    device.GetKnownUsersOrdered(file.User),
                    device.GetKnownGroupsOrdered(file.Group),
                    PermissionsEdit.SelectedUser ?? file.User,
                    PermissionsEdit.SelectedGroup ?? file.Group);
            });
        });
    }

    private async Task SavePermissionsAsync()
    {
        if (!IsEditingPermissions || File is not { } file)
            return;

        var device = Data.ActiveDevice;
        if (device is null)
            return;

        var token = _cancellationToken?.Token ?? default;
        var error = await PermissionsEdit.ApplyAsync(file, device.ID, token);

        IsEditingPermissions = false;
        await file.UpdateExtraInfoAsync(token);
        UpdateCanEditPermissions();

        if (!string.IsNullOrEmpty(error))
        {
            DialogService.ShowMessage(
                error,
                Strings.Resources.S_FILE_PERMISSIONS,
                DialogService.DialogIcon.Critical,
                copyToClipboard: true,
                error: DialogError.ChangePermissionsFailed);
        }
    }
}
