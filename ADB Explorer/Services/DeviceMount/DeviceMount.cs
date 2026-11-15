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
/// A device's drives as a Cloud Files sync root: a folder in File Explorer (with a navigation pane entry), holding a folder
/// per drive, whose folders are listed from the device on first browse and whose files are pulled on open.
/// </summary>
public sealed partial class DeviceMount : IDisposable
{
    private const string PROVIDER_ID = "ADBExplorer";

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
        SyncRootId = SyncRootIdOf(device);
        _drives = DriveFolders(device);
    }

    private readonly List<MountedDrive> _drives;

    private static string SyncRootIdOf(LogicalDeviceViewModel device)
        => $"{PROVIDER_ID}!{WindowsIdentity.GetCurrent().User}!{SanitizeIdPart(device.ID)}";

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
                Current = StartMount(device);
            }

            return Current.ToLocalPath(devicePath);
        }
    }

    private const int MAX_FOLDER_ATTEMPTS = 5;

    /// <summary>
    /// Mounts where the device is still registered from before, else in a free folder. A registration is never moved
    /// to another folder while placeholders remain in its own: that would orphan them, beyond any removal.
    /// </summary>
    private static DeviceMount StartMount(LogicalDeviceViewModel device)
    {
        var folder = RegisteredFolder(SyncRootIdOf(device)) ?? FreeFolder(device);
        for (var attempt = 1; ; attempt++)
        {
            var mount = new DeviceMount(device, folder);

            // Orphaned placeholders would stand in for the device's own entries: those leave for a free folder
            if (mount.Start(allowOrphans: attempt == MAX_FOLDER_ATTEMPTS))
                return mount;

            folder = FreeFolder(device);
        }
    }

    private static string? RegisteredFolder(string syncRootId)
    {
        try
        {
            return StorageProviderSyncRootManager.GetCurrentSyncRoots().FirstOrDefault(root => root.Id == syncRootId)?.Path.Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The device's folder, or the next "Name (2)"… that doesn't exist or is empty.</summary>
    private static string FreeFolder(LogicalDeviceViewModel device)
    {
        var name = SanitizeFileName(device.Name);
        for (var number = 1; ; number++)
        {
            var folder = Path.Combine(MountsFolder, number == 1 ? name : $"{name} ({number})");
            try
            {
                if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
                    return folder;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { }
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
                await Task.Run(() =>
                {
                    if (WaitForExplorerWindow(localPath, TimeSpan.FromSeconds(10)) is { } window)
                        Current?.MoveToNamespaceNode(window, localPath);
                });
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
    private static object? WaitForExplorerWindow(string localPath, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (ExplorerWindows().FirstOrDefault(window => PathsEqual(LocationOf(window), localPath)) is { } window)
                return window;

            Thread.Sleep(200);
        }

        return null;
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

    /// <returns>False, unregistered again, when the folder holds orphaned placeholders and those aren't allowed.</returns>
    private bool Start(bool allowOrphans)
    {
        Directory.CreateDirectory(LocalRoot);
        Register();

        // Leftovers of an earlier mount; any still open elsewhere are this registration's own, and harmless
        if (EmptySyncRoot(LocalRoot).HasOrphans && !allowOrphans)
        {
            Unregister();
            return false;
        }

        _callbacks =
        [
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, Callback = OnFetchPlaceholders },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, Callback = OnFetchData },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, Callback = OnCancelFetchData },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, Callback = OnNotifyDelete },
            new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, Callback = OnNotifyRename },
            CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END,
        ];

        // Hydration finds the device file by the placeholder's path, which data fetches only get with this flag
        CfConnectSyncRoot(LocalRoot, _callbacks, default, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH, out var key).ThrowIfFailed();
        _connectionKey = key;

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
        StopPopulating();
        CancelAllFetches();

        if (_connectionKey is { } key)
        {
            CfDisconnectSyncRoot(key);
            _connectionKey = null;
        }

        // Disconnected, so these deletes aren't refused by our own callback.
        // Any left (e.g. still open in an app) keep the registration, for the next startup's cleanup to finish
        if (EmptySyncRoot(LocalRoot).IsBlocked)
            return;

        Unregister();

        // Left in place when it holds files of the user's own
        DeleteIfEmpty(LocalRoot);
        DeleteIfEmpty(MountsFolder);
    }

    private void Unregister()
    {
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
                if (EmptySyncRoot(path).IsBlocked)
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

    /// <summary>What a cleanup left: placeholders it couldn't delete yet (e.g. open elsewhere), or orphaned ones it never can.</summary>
    private sealed class CleanupResult
    {
        /// <summary>A placeholder of the registration is left: unregistering now would orphan it.</summary>
        public bool IsBlocked { get; set; }

        /// <summary>Placeholders whose registration was gone before: no registration can remove them.</summary>
        public bool HasOrphans { get; set; }

        public void Record(Exception e)
        {
            if ((e.HResult & 0xFFFF) == ERROR_CLOUD_FILE_METADATA_CORRUPT)
                HasOrphans = true;
            else
                IsBlocked = true;
        }
    }

    private const int ERROR_CLOUD_FILE_METADATA_CORRUPT = 363;

    /// <summary>
    /// <see cref="RemovePlaceholders"/> in a registered sync root, with the cleanup provider connected:
    /// without a provider, folders that were never listed can't even be enumerated.
    /// </summary>
    private static CleanupResult EmptySyncRoot(string folder)
    {
        // Connected elsewhere (another instance of the app): leave it to that one
        if (CfConnectSyncRoot(folder, CleanupCallbacks, default, CF_CONNECT_FLAGS.CF_CONNECT_FLAG_NONE, out var key).Failed)
            return new() { IsBlocked = true };

        try
        {
            return RemovePlaceholders(folder);
        }
        finally
        {
            CfDisconnectSyncRoot(key);
        }
    }

    /// <summary>Deletes placeholders (never the user's own files), and folders left empty by that.</summary>
    private static CleanupResult RemovePlaceholders(string folder)
    {
        var result = new CleanupResult();
        if (!Directory.Exists(folder))
            return result;

        // Placeholders are disguised as plain files by default, which would hide them among the user's own
        var previous = NativeMethods.RtlSetThreadPlaceholderCompatibilityMode(NativeMethods.PHCM_EXPOSE_PLACEHOLDERS);
        try
        {
            RemovePlaceholdersCore(folder, result);
        }
        finally
        {
            if (previous >= 0)
                NativeMethods.RtlSetThreadPlaceholderCompatibilityMode(previous);
        }

        return result;
    }

    /// <returns>Whether anything is left in the folder.</returns>
    private static bool RemovePlaceholdersCore(string folder, CleanupResult result)
    {
        var left = false;
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                var isPlaceholder = (entry.Attributes & (FileAttributes.ReparsePoint | RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS)) != 0;

                if (entry is DirectoryInfo dir)
                {
                    // An unpopulated folder can't be enumerated without the provider, and has nothing of the user's
                    var innerLeft = !dir.Attributes.HasFlag(RECALL_ON_OPEN) && RemovePlaceholdersCore(dir.FullName, result);

                    // Not emptied, its delete would only fail for what's inside, which was already accounted for
                    if (innerLeft)
                        left = true;
                    else if (isPlaceholder)
                    {
                        // Drive folders are read-only, for their icon files to count
                        left |= !TryDelete(() =>
                        {
                            dir.Attributes &= ~FileAttributes.ReadOnly;
                            dir.Delete(dir.Attributes.HasFlag(RECALL_ON_OPEN));
                        }, result);
                    }
                    else if (!dir.EnumerateFileSystemInfos().Any())
                        TryDelete(() => dir.Delete(), new());
                    else
                        left = true;
                }
                else if (isPlaceholder)
                {
                    left |= !TryDelete(() =>
                    {
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                        entry.Delete();
                    }, result);
                }
                else
                {
                    left = true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Placeholder cleanup of {folder} failed: {e.Message}");
#endif
            result.Record(e);
            return true;
        }

        return left;
    }

    private static bool TryDelete(Action delete, CleanupResult result)
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
            result.Record(e);
            return false;
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
