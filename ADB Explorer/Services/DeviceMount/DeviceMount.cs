using System.Security.Principal;
using ContentDialog = Wpf.Ui.Controls.ContentDialog;
using IContentDialogService = Wpf.Ui.IContentDialogService;
using ProgressRing = Wpf.Ui.Controls.ProgressRing;
using Vanara.PInvoke;
using StorageFolder = Windows.Storage.StorageFolder;
using FileAttributes = System.IO.FileAttributes;
using Windows.Storage.Provider;
using static Vanara.PInvoke.CldApi;

namespace ADB_Explorer.Services;

/// <summary>
/// A device's storage as a Cloud Files sync root: a folder in File Explorer (with a navigation pane entry) whose folders
/// are listed from the device on first browse and whose files are pulled on open. Read-only: deletes and renames are refused.
/// </summary>
public sealed partial class DeviceMount : IDisposable
{
    private const string PROVIDER_ID = "ADBExplorer";

    private const string DEVICE_ROOT = "/sdcard";

    // Folders: not populated yet. Files: dehydrated, or hydrated (a cloud reparse point)
    private const FileAttributes RECALL_ON_OPEN = (FileAttributes)0x40000;

    private const FileAttributes RECALL_ON_DATA_ACCESS = (FileAttributes)0x400000;

    private static readonly Guid PROVIDER_GUID = new("6c0b8c5e-4f6a-4c2b-9a51-2f1f0c7a9d13");

    private static readonly object _lock = new();

    private CF_CALLBACK_REGISTRATION[]? _callbacks;

    private CF_CONNECTION_KEY? _connectionKey;

    private bool _registeredWithShell;

    /// <summary>Registered through the shell (with a navigation pane entry), not only as a plain sync root.</summary>
    public bool IsShellRegistered => _registeredWithShell;

    private DeviceMount(LogicalDeviceViewModel device, string localRoot)
    {
        Device = device;
        LocalRoot = localRoot;
        SyncRootId = $"{PROVIDER_ID}!{WindowsIdentity.GetCurrent().User}!{SanitizeIdPart(device.ID)}";
    }

    /// <summary>The single mounted device, if any.</summary>
    public static DeviceMount? Current { get; private set; }

    public LogicalDeviceViewModel Device { get; }

    public string DeviceId => Device.ID;

    public string LocalRoot { get; }

    public string SyncRootId { get; }

    public static string MountsFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Properties.AppGlobal.AppDisplayName);

    /// <summary>Mounts the device - replacing another mounted device - and returns its local folder for a device path.</summary>
    public static string? MountAndMap(LogicalDeviceViewModel device, string devicePath)
    {
        lock (_lock)
        {
            if (Current?.DeviceId != device.ID)
            {
                Current?.Dispose();
                Current = null;
                Current = StartInFreeFolder(device);
            }

            return Current.ToLocalPath(devicePath);
        }
    }

    private const int MAX_FOLDER_ATTEMPTS = 5;

    /// <summary>
    /// Mounts in the device's folder, or the next one ("Name (2)"…) while a folder holds what can't be cleared,
    /// such as placeholders orphaned by an earlier version: those would stand in for the device's own entries.
    /// </summary>
    private static DeviceMount StartInFreeFolder(LogicalDeviceViewModel device)
    {
        var name = SanitizeFileName(device.Name);
        for (var attempt = 1; ; attempt++)
        {
            var folder = Path.Combine(MountsFolder, attempt == 1 ? name : $"{name} ({attempt})");
            var mount = new DeviceMount(device, folder);
            if (mount.Start(requireEmpty: attempt < MAX_FOLDER_ATTEMPTS))
                return mount;

            mount.Dispose();
        }
    }

    /// <summary>
    /// Mounts the device in the background and opens File Explorer at the device path's local folder,
    /// behind a progress dialog that closes once the File Explorer window is up.
    /// </summary>
    public static async void OpenInFileExplorer(LogicalDeviceViewModel device, string devicePath)
    {
        var dialog = CreateOpeningDialog();
        _ = App.Services.GetRequiredService<IContentDialogService>().ShowAsync(dialog, CancellationToken.None);

        try
        {
            var localPath = await Task.Run(() => MountAndMap(device, devicePath));
            if (localPath is not null)
            {
                Process.Start("explorer.exe", localPath);
                await Task.Run(() => WaitForExplorerWindow(localPath, TimeSpan.FromSeconds(10)));
            }

            dialog.Hide();
        }
        catch (Exception e)
        {
            dialog.Hide();
            DialogService.ShowMessage(e.Message,
                                      Strings.Resources.S_MOUNT_FAILED_TITLE,
                                      DialogService.DialogIcon.Critical,
                                      copyToClipboard: true);
        }
    }

    private static ContentDialog CreateOpeningDialog()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new ProgressRing { IsIndeterminate = true, Width = 24, Height = 24, Margin = new(0, 0, 12, 0) });
        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = Strings.Resources.S_MOUNT_OPENING,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        return new ContentDialog
        {
            Content = content,
            IsFooterVisible = false,
            FlowDirection = Data.RuntimeSettings.IsRTL ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
    }

    /// <summary>Waits until a File Explorer window shows the folder, or the timeout passes.</summary>
    private static void WaitForExplorerWindow(string localPath, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (ExplorerWindows().Any(window => PathsEqual(LocationOf(window), localPath)))
                return;

            Thread.Sleep(200);
        }
    }

    private static bool PathsEqual(string? a, string b)
        => a is not null && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    public static void Unmount(string? deviceId = null)
    {
        lock (_lock)
        {
            if (Current is null || (deviceId is not null && !Current.Device.Matches(deviceId)))
                return;

            Current.Dispose();
            Current = null;
        }
    }

    /// <summary>Whether a device folder is inside the mounted storage, and not left out of it like the recycle bin.</summary>
    public static bool CanMount(string? devicePath)
        => devicePath is not null
        && (devicePath == DEVICE_ROOT || devicePath.StartsWith($"{DEVICE_ROOT}/"))
        && !FileHelper.IsInTrash(devicePath);

    /// <summary>The local path for a device path under the mounted root, or the root itself for any other path.</summary>
    public string ToLocalPath(string devicePath)
    {
        if (!devicePath.StartsWith($"{DEVICE_ROOT}/"))
            return LocalRoot;

        var relative = devicePath[(DEVICE_ROOT.Length + 1)..].TrimEnd('/');

        return Path.Combine(LocalRoot, relative.Replace('/', '\\'));
    }

    /// <summary>The device path for a full local path under <see cref="LocalRoot"/>, or null outside it.</summary>
    private string? ToDevicePath(string localPath)
    {
        var relative = Path.GetRelativePath(LocalRoot, localPath);
        if (relative == ".")
            return DEVICE_ROOT;

        if (relative.StartsWith("..") || Path.IsPathRooted(relative))
            return null;

        return FileHelper.ConcatPaths(DEVICE_ROOT, relative.Replace('\\', '/'));
    }

    /// <returns>False, unconnected, when <paramref name="requireEmpty"/> and something in the folder couldn't be cleared.</returns>
    private bool Start(bool requireEmpty)
    {
        Directory.CreateDirectory(LocalRoot);
        Register();

        // Leftovers of an earlier mount: placeholders outside a registered sync root can't even have their attributes changed
        EmptySyncRoot(LocalRoot);

        _callbacks =
        [
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, Callback = OnFetchPlaceholders },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, Callback = OnFetchData },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, Callback = OnCancelFetchData },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, Callback = OnNotifyDelete },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, Callback = OnNotifyRename },
            CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END,
        ];

        CfConnectSyncRoot(LocalRoot, _callbacks, default, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_NONE, out var key).ThrowIfFailed();
        _connectionKey = key;

        // Listed while connected, which the provider's own I/O never triggers a fetch for: only what's really on disk
        if (requireEmpty && Directory.EnumerateFileSystemEntries(LocalRoot).Any())
        {
            CfDisconnectSyncRoot(key);
            _connectionKey = null;
            return false;
        }

        return true;
    }

    private void Register()
    {
        try
        {
            RegisterWithShell();
            _registeredWithShell = true;
        }
        catch (Exception e)
        {
            // Without package identity the shell registration may be refused: a plain sync root still works, minus the navigation pane entry
#if !DEPLOY
            DebugLog.PrintLine($"Shell sync root registration failed, registering directly: {e.Message}");
#endif
            RegisterDirectly();
        }
    }

    private void RegisterWithShell()
    {
        var identity = Encoding.UTF8.GetBytes(DeviceId);
        var info = new StorageProviderSyncRootInfo
        {
            Id = SyncRootId,
            Path = StorageFolder.GetFolderFromPathAsync(LocalRoot).AsTask().GetAwaiter().GetResult(),
            DisplayNameResource = Device.Name,
            IconResource = $"{Environment.ProcessPath},0",
            HydrationPolicy = StorageProviderHydrationPolicy.Full,
            HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.None,
            PopulationPolicy = StorageProviderPopulationPolicy.Full,
            InSyncPolicy = StorageProviderInSyncPolicy.Default,
            HardlinkPolicy = StorageProviderHardlinkPolicy.None,
            ShowSiblingsAsGroup = false,
            Version = "1.0",
            Context = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(identity),
        };

        StorageProviderSyncRootManager.Register(info);
    }

    private void RegisterDirectly()
    {
        var identity = Encoding.UTF8.GetBytes(DeviceId);
        var pIdentity = Marshal.AllocHGlobal(identity.Length);
        try
        {
            Marshal.Copy(identity, 0, pIdentity, identity.Length);

            var registration = new CF_SYNC_REGISTRATION
            {
                StructSize = (uint)Marshal.SizeOf<CF_SYNC_REGISTRATION>(),
                ProviderName = PROVIDER_ID,
                ProviderVersion = "1.0",
                SyncRootIdentity = pIdentity,
                SyncRootIdentityLength = (uint)identity.Length,
                ProviderId = PROVIDER_GUID,
            };

            var policies = new CF_SYNC_POLICIES
            {
                StructSize = (uint)Marshal.SizeOf<CF_SYNC_POLICIES>(),
                Hydration = new() { Primary = CF_HYDRATION_POLICY_PRIMARY.CF_HYDRATION_POLICY_FULL },
                Population = new() { Primary = CF_POPULATION_POLICY_PRIMARY.CF_POPULATION_POLICY_FULL },
                InSync = CF_INSYNC_POLICY.CF_INSYNC_POLICY_NONE,
                HardLink = CF_HARDLINK_POLICY.CF_HARDLINK_POLICY_NONE,
            };

            CfRegisterSyncRoot(LocalRoot, registration, policies, CF_REGISTER_FLAGS.CF_REGISTER_FLAG_UPDATE).ThrowIfFailed();
        }
        finally
        {
            Marshal.FreeHGlobal(pIdentity);
        }
    }

    public void Dispose()
    {
        CloseExplorerWindows();
        CancelAllFetches();

        if (_connectionKey is { } key)
        {
            CfDisconnectSyncRoot(key);
            _connectionKey = null;
        }

        // Disconnected, so these deletes aren't refused by our own callback.
        // Any left (e.g. still open in an app) keep the registration, for the next startup's cleanup to finish
        if (!EmptySyncRoot(LocalRoot))
            return;

        try
        {
            if (_registeredWithShell)
                StorageProviderSyncRootManager.Unregister(SyncRootId);
            else
                CfUnregisterSyncRoot(LocalRoot);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Sync root unregistration failed: {e.Message}");
#endif
        }

        // Left in place when it holds files of the user's own
        DeleteIfEmpty(LocalRoot);
        DeleteIfEmpty(MountsFolder);
    }

    /// <summary>Closes the File Explorer windows showing this mount, which would otherwise be left on a removed folder.</summary>
    private void CloseExplorerWindows()
    {
        foreach (var window in ExplorerWindows())
        {
            try
            {
                if (IsUnderRoot(LocationOf(window)))
                    ((dynamic)window).Quit();
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Closing a File Explorer window failed: {e.Message}");
#endif
            }
        }
    }

    /// <summary>The open File Explorer windows, late bound: Vanara has no IWebBrowser2 for their location and Quit.</summary>
    private static List<object> ExplorerWindows()
    {
        List<object> result = [];
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            foreach (var window in shell.Windows())
            {
                if (window is not null)
                    result.Add(window);
            }
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Listing File Explorer windows failed: {e.Message}");
#endif
        }

        return result;
    }

    /// <summary>The folder a File Explorer window shows - also for one opened from the navigation pane entry, which has no file URL.</summary>
    private static string? LocationOf(object window)
    {
        try
        {
            return ((dynamic)window).Document.Folder.Self.Path as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool IsUnderRoot(string? path) => !string.IsNullOrEmpty(path) && ToDevicePath(path) is not null;

    /// <summary>Unregisters sync roots a previous run left behind (e.g. after a crash) and clears their placeholders.</summary>
    public static void CleanUpStaleMounts()
    {
        // Not alongside a mount being set up in one of these folders
        lock (_lock)
            CleanUpStaleMountsCore();
    }

    private static void CleanUpStaleMountsCore()
    {
        try
        {
            foreach (var root in StorageProviderSyncRootManager.GetCurrentSyncRoots())
            {
                if (!root.Id.StartsWith($"{PROVIDER_ID}!"))
                    continue;

                var path = root.Path.Path;
                if (Current?.SyncRootId == root.Id)
                    continue;

                // Kept registered while any placeholder is left, so a later run can still remove it
                if (!EmptySyncRoot(path))
                    continue;

                StorageProviderSyncRootManager.Unregister(root.Id);
                DeleteIfEmpty(path);
            }
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Stale sync root cleanup failed: {e.Message}");
#endif
        }

        DeleteIfEmpty(MountsFolder);
    }

    private static void DeleteIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (IOException)
        { }
    }

    /// <summary>
    /// <see cref="RemovePlaceholders"/> in a registered sync root, with the cleanup provider connected:
    /// without a provider, folders that were never listed can't even be enumerated.
    /// </summary>
    private static bool EmptySyncRoot(string folder)
    {
        // Connected elsewhere (another instance of the app): leave it to that one
        if (CfConnectSyncRoot(folder, CleanupCallbacks, default, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_NONE, out var key).Failed)
            return false;

        try
        {
            return RemovePlaceholders(folder);
        }
        finally
        {
            CfDisconnectSyncRoot(key);
        }
    }

    /// <summary>
    /// Deletes placeholders (never the user's own files), and folders left empty by that. Returns whether none is left:
    /// only then may the sync root be unregistered, as placeholders outside one can never be removed.
    /// </summary>
    private static bool RemovePlaceholders(string folder)
    {
        if (!Directory.Exists(folder))
            return true;

        // Placeholders are disguised as plain files by default, which would hide them among the user's own
        var previous = NativeMethods.RtlSetThreadPlaceholderCompatibilityMode(NativeMethods.PHCM_EXPOSE_PLACEHOLDERS);
        try
        {
            return RemovePlaceholdersCore(folder);
        }
        finally
        {
            if (previous >= 0)
                NativeMethods.RtlSetThreadPlaceholderCompatibilityMode(previous);
        }
    }

    private static bool RemovePlaceholdersCore(string folder)
    {
        var allRemoved = true;
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                var isPlaceholder = (entry.Attributes & (FileAttributes.ReparsePoint | RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS)) != 0;

                if (entry is DirectoryInfo dir)
                {
                    // An unpopulated folder can't be enumerated without the provider, and has nothing of the user's
                    if (!dir.Attributes.HasFlag(RECALL_ON_OPEN))
                        allRemoved &= RemovePlaceholdersCore(dir.FullName);

                    if (isPlaceholder)
                        allRemoved &= TryDelete(() => dir.Delete(dir.Attributes.HasFlag(RECALL_ON_OPEN)));
                    else if (!dir.EnumerateFileSystemInfos().Any())
                        TryDelete(() => dir.Delete());
                }
                else if (isPlaceholder)
                {
                    allRemoved &= TryDelete(() =>
                    {
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                        entry.Delete();
                    });
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Placeholder cleanup of {folder} failed: {e.Message}");
#endif
            return (e.HResult & 0xFFFF) == ERROR_CLOUD_FILE_METADATA_CORRUPT;
        }

        return allRemoved;
    }

    // A placeholder whose sync root registration is gone: no registration can make it removable again
    private const int ERROR_CLOUD_FILE_METADATA_CORRUPT = 363;

    /// <summary>Whether the entry is gone, or can never be removed - so it needn't hold the registration back.</summary>
    private static bool TryDelete(Action delete)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Placeholder delete failed: {e.Message}");
#endif
            return (e.HResult & 0xFFFF) == ERROR_CLOUD_FILE_METADATA_CORRUPT;
        }
    }

    private static string SanitizeIdPart(string value)
        => new([.. value.Select(c => char.IsLetterOrDigit(c) ? c : '_')]);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]).Trim().TrimEnd('.');

        return string.IsNullOrEmpty(clean) ? "Device" : clean;
    }
}
