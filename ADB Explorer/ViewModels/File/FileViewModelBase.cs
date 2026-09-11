using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;

namespace ADB_Explorer.ViewModels;

public partial class FileViewModelBase : ObservableObject
{
    protected readonly FileClass _file;

    private string _typeName;
    public string TypeName
    {
        get => _typeName;
        protected set
        {
            if (SetProperty(ref _typeName, value))
            {
                OnPropertyChanged(nameof(TypeIsRtl));
                OnPropertyChanged(nameof(TypeFlowDirection));
                OnPropertyChanged(nameof(ContentViewTypeText));
            }
        }
    }

    public bool TypeIsRtl => TextHelper.ContainsRtl(TypeName);
    public FlowDirection TypeFlowDirection => TypeIsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>"Type: {TypeName}" (RTL-aware), for the Content view's collapsible type column.</summary>
    public string ContentViewTypeText => FormatLabeledValue(Strings.Resources.S_COLUMN_TYPE, TypeName, TypeIsRtl);

    /// <summary>"Date modified: {ModifiedTimeString}", for the Content view's date/size column.</summary>
    public string ContentViewModifiedTimeText => FormatLabeledValue(Strings.Resources.S_COLUMN_DATE_MODIFIED, ModifiedTimeString, false);

    /// <summary>"Size: {SizeString}", for the Content view's date/size column.</summary>
    public string ContentViewSizeText => FormatLabeledValue(Strings.Resources.S_COLUMN_SIZE, SizeString, false);

    private static string FormatLabeledValue(string label, string value, bool valueIsRtl)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        return Data.RuntimeSettings.IsRTL && !valueIsRtl
            ? $"{TextHelper.LTR_MARK}{label}: {TextHelper.RTL_MARK}{value}{TextHelper.LTR_MARK}"
            : $"{label}: {value}";
    }

    public string ModifiedTimeString => TabularDateFormatter.Format(_file.ModifiedTime, Data.Settings.ActualFormatCulture);
    public string ModifiedTimeWithOffsetString => _file.ModifiedTimeWithOffset is { } dto
        ? TabularDateFormatter.Format(dto, Data.Settings.ActualFormatCulture)
        : TabularDateFormatter.Format(_file.ModifiedTime, Data.Settings.ActualFormatCulture);
    public string CreationTimeString => TabularDateFormatter.Format(_file.CreationTime, Data.Settings.ActualFormatCulture);
    public string LastAccessTimeString => TabularDateFormatter.Format(_file.LastAccessTime, Data.Settings.ActualFormatCulture);

    public string SizeString
    {
        get
        {
            if (_file.IsDirectory)
            {
                return "";
            }
            else
            {
                return _file.ShellLsSize?.BytesToSize(true) ?? _file.Size?.BytesToSize(true);
            }
        }
    }

    public string ShortExtension
        => FileHelper.TryGetUnicodeIconExtension(_file.FullName, out var icon) ? icon : "";

    public string UserPermissionsString
    {
        get
        {
            if (_file.Permissions is null)
                return "";

            return GetPermissionsString(_file.Permissions.Value,
                                        UnixFileMode.UserRead,
                                        UnixFileMode.UserWrite,
                                        UnixFileMode.UserExecute);
        }
    }

    public string GroupPermissionsString
    {
        get
        {
            if (_file.Permissions is null)
                return "";

            return GetPermissionsString(_file.Permissions.Value,
                                        UnixFileMode.GroupRead,
                                        UnixFileMode.GroupWrite,
                                        UnixFileMode.GroupExecute);
        }
    }

    public string OtherPermissionsString
    {
        get
        {
            if (_file.Permissions is null)
                return "";

            return GetPermissionsString(_file.Permissions.Value,
                                        UnixFileMode.OtherRead,
                                        UnixFileMode.OtherWrite,
                                        UnixFileMode.OtherExecute);
        }
    }

    [ObservableProperty]
    public partial bool IsDragOver { get; set; }

    [ObservableProperty]
    public partial bool IsInEditMode { get; set; }

    [ObservableProperty]
    public partial bool IsRenameUnixLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameNamingLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameWindowsLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameDriveRootLegal { get; set; }

    [ObservableProperty]
    public partial bool IsRenameUnique { get; set; }

    /// <summary>
    /// True while a search-mode unique-name check is debouncing or in flight - the rename tooltip
    /// shows a "checking" indicator instead of pass/fail, and commit is refused until this clears.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCheckingUniqueName { get; set; }

    private DispatcherTimer? _uniqueCheckDebounceTimer;
    private CancellationTokenSource? _uniqueCheckCts;
    private string? _lastUniqueCheckedName;
    private FileClass? _pendingUniqueCheckFile;
    private string? _pendingUniqueCheckName;
    private StringComparison _pendingUniqueCheckComparison;

    protected FileViewModelBase(FileClass file)
    {
        _file = file;
        _typeName = GetTypeName();
    }

    public static void PrepareRenameTextBox(TextBox textBox)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        textBox.ClearValue(TextBox.TextProperty);
        if (textBox.GetBindingExpression(TextBox.TextProperty) is { } expression)
            expression.UpdateTarget();
        else
            textBox.Text = FileHelper.DisplayName(file);

        RenameTextChanged(textBox);
        textBox.Focus();
        textBox.SelectAll();
    }

    public static void RenameTextChanged(TextBox textBox)
    {
        if (textBox.DataContext is not FileClass file || Data.CurrentDrive is null)
            return;

        var restrictions = DriveHelper.GetRestrictions(file.FullPath);
        textBox.FilterString(restrictions.RestrictedNaming
            ? AdbExplorerConst.INVALID_NTFS_CHARS
            : AdbExplorerConst.INVALID_UNIX_CHARS);

        var vm = file.ActiveViewModel;

        vm.IsRenameUnixLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Unix);
        vm.IsRenameNamingLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.RestrictedNaming);
        vm.IsRenameWindowsLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.Windows);
        vm.IsRenameDriveRootLegal = FileHelper.FileNameLegal(textBox.Text, FileHelper.RenameTarget.WinRoot);

        var fullName = Data.Settings.ShowExtensions
            ? textBox.Text
            : textBox.Text + file.Extension;

        var comparison = restrictions.CaseInsensitiveNames
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.InvariantCulture;

        if (Data.FileActions.IsSearchMode)
        {
            // Data.DirList here is the search-results listing, not the item's real parent folder -
            // validate against the folder the item actually lives in instead (async + debounced).
            vm.QueueSearchModeUniqueNameCheck(file, fullName, comparison);
        }
        else
        {
            vm.CancelUniqueNameCheck();
            vm.IsRenameUnique = !Data.DirList.FileList.Except([file]).Any(f => f.FullName.Equals(fullName, comparison));
        }
    }

    /// <summary>
    /// Debounces (1s) a unique-name check for search mode, run against the item's actual parent
    /// folder on the device rather than the search-results listing. Shows as "checking" the whole
    /// time from the first keystroke that changed the name until a result comes back; a superseded
    /// or canceled check never overwrites <see cref="IsRenameUnique"/> with a stale answer.
    /// </summary>
    private void QueueSearchModeUniqueNameCheck(FileClass file, string candidateFullName, StringComparison comparison)
    {
        // The unmodified name needs no round trip - it's trivially unique (it already exists as
        // this very file in that folder).
        if (candidateFullName == file.FullName)
        {
            CancelUniqueNameCheck();
            _lastUniqueCheckedName = candidateFullName;
            IsRenameUnique = true;
            return;
        }

        // Already resolved for this exact text (e.g. the user retyped back to a previously-checked
        // value) - IsRenameUnique already holds the right answer; just drop any leftover timer.
        if (candidateFullName == _lastUniqueCheckedName)
        {
            CancelUniqueNameCheck();
            return;
        }

        CancelUniqueNameCheck();
        IsCheckingUniqueName = true;

        _pendingUniqueCheckFile = file;
        _pendingUniqueCheckName = candidateFullName;
        _pendingUniqueCheckComparison = comparison;

        _uniqueCheckDebounceTimer ??= new DispatcherTimer();
        _uniqueCheckDebounceTimer.Interval = TimeSpan.FromSeconds(1);
        _uniqueCheckDebounceTimer.Tick -= UniqueCheckDebounceTimer_Tick;
        _uniqueCheckDebounceTimer.Tick += UniqueCheckDebounceTimer_Tick;
        _uniqueCheckDebounceTimer.Start();
    }

    private void UniqueCheckDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _uniqueCheckDebounceTimer?.Stop();

        var file = _pendingUniqueCheckFile;
        var candidateFullName = _pendingUniqueCheckName;
        var comparison = _pendingUniqueCheckComparison;

        if (file is null || candidateFullName is null || Data.DevicesObject.Current is not { } device)
        {
            IsCheckingUniqueName = false;
            return;
        }

        var deviceId = device.ID;
        var parentPath = FileHelper.GetParentPath(file.FullPath);
        var originalFullPath = file.FullPath;

        _uniqueCheckCts = new CancellationTokenSource();
        var token = _uniqueCheckCts.Token;

        Task.Run(() =>
        {
            bool isUnique;
            try
            {
                isUnique = !ADBService.ListDirectoryEntries(deviceId, parentPath, token)
                    .Any(entry => entry.FullPath != originalFullPath && entry.FullName.Equals(candidateFullName, comparison));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Device / listing error: don't block the rename on an inconclusive check.
                isUnique = true;
            }

            App.SafeBeginInvoke(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                _lastUniqueCheckedName = candidateFullName;
                IsRenameUnique = isUnique;
                IsCheckingUniqueName = false;
            });
        }, token);
    }

    /// <summary>Stops any pending debounce timer / in-flight check without touching <see cref="IsRenameUnique"/>.</summary>
    public void CancelUniqueNameCheck()
    {
        _uniqueCheckDebounceTimer?.Stop();
        _uniqueCheckCts?.Cancel();
        _uniqueCheckCts?.Dispose();
        _uniqueCheckCts = null;
        IsCheckingUniqueName = false;
    }

    public static void RenameKeyDown(TextBox textBox, Key key, Action<FileClass> exitEditMode)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        if (key is Key.Escape or Key.F2)
        {
            file.ActiveViewModel.CancelUniqueNameCheck();

            if (file.IsTemp && key is Key.Escape)
            {
                FileActionLogic.CancelPendingCompress(file);
                FileActionLogic.CancelPendingClipboardImage(file);
                Data.DirList.FileList.Remove(file);
            }
            else
            {
                var name = FileHelper.DisplayName(textBox);
                if (string.IsNullOrEmpty(name))
                {
                    FileActionLogic.CancelPendingCompress(file);
                    FileActionLogic.CancelPendingClipboardImage(file);
                    Data.DirList.FileList.Remove(file);
                }
                else
                    textBox.Text = name;
            }

            exitEditMode(file);
        }
        else if (key is Key.Enter)
        {
            RenameCommit(textBox, exitEditMode);
        }
    }

    /// <summary>
    /// Commits the rename - unless a search-mode unique-name check is still debouncing / in flight,
    /// in which case the commit (from Enter or clicking away) is refused until it resolves.
    /// </summary>
    public static void RenameCommit(TextBox textBox, Action<FileClass> exitEditMode)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        if (file.ActiveViewModel.IsCheckingUniqueName)
            return;

        FileActionLogic.Rename(textBox);
        file.ActiveViewModel.CancelUniqueNameCheck();
        exitEditMode(file);
    }

    public virtual void UpdateType()
    {
        TypeName = GetTypeName();
    }

    internal string GetTypeName()
    {
        var type = _file.Type switch
        {
            AbstractFile.FileType.File => GetTypeName(_file.FullName),
            AbstractFile.FileType.Folder => Strings.Resources.S_MENU_FOLDER,
            AbstractFile.FileType.Unknown => "",
            _ => AbstractFile.GetFileTypeName(_file.Type),
        };

        if (_file.IsLink && _file.Type is not AbstractFile.FileType.BrokenLink)
            type = string.IsNullOrEmpty(type)
                ? Strings.Resources.S_FILE_TYPE_LINK
                : string.Format(Strings.Resources.S_KNOWN_TYPE_LINK, type);

        return type;
    }

    private string GetTypeName(string fileName)
    {
        if (_file.IsApk)
            return Strings.Resources.S_FILE_TYPE_APK;

        if (string.IsNullOrEmpty(fileName) || (_file.IsHidden && _file.FullName.Count(c => c == '.') == 1))
            return Strings.Resources.S_MENU_FILE;

        if (_file.Extension.Equals(".exe", StringComparison.CurrentCultureIgnoreCase))
            return Strings.Resources.S_FILE_TYPE_EXE;

        if (!Ascii.IsValid(_file.Extension))
        {
            if (ShortExtension.Length is 0)
                return $"{_file.Extension[1..]} {Strings.Resources.S_MENU_FILE}";

            return $"{ShortExtension} {Strings.Resources.S_MENU_FILE}";
        }
        else
        {
            return NativeMethods.GetShellFileType(ArchiveHelper.GetShellAssociationName(fileName));
        }
    }

    private static string GetPermissionsString(UnixFileMode fileMode, UnixFileMode read, UnixFileMode write, UnixFileMode execute)
    {
        var userPermissions = fileMode & (read | write | execute);
        string result = "";

        if (userPermissions.HasFlag(read))
            result += "r";
        else
            result += "-";

        if (userPermissions.HasFlag(write))
            result += "w";
        else
            result += "-";

        if (userPermissions.HasFlag(execute))
            result += "x";
        else
            result += "-";

        return result;
    }

    public void OnModifiedTimeChanged()
    {
        OnPropertyChanged(nameof(ModifiedTimeString));
        OnPropertyChanged(nameof(ModifiedTimeWithOffsetString));
        OnPropertyChanged(nameof(ContentViewModifiedTimeText));
    }

    public void OnSizeChanged()
    {
        OnPropertyChanged(nameof(SizeString));
        OnPropertyChanged(nameof(ContentViewSizeText));
    }

    public virtual void Dispose()
    {
        TypeName = null!;
    }
}
