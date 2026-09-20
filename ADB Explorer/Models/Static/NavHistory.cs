using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using ADB_Explorer.Views.Pages;

namespace ADB_Explorer.Models
{
    public abstract class Navigation
    {
        public enum SpecialLocation
        {
            None,
            DriveView,
            Back,
            Forward,
            Up,
            RecycleBin,
            PackageDrive,
            SearchMode,
            devNull,
            Unknown,
            Devices,
            Settings,
            Terminal,
            Operations,
            Log,
        }
    }

    public class AdbLocation : Navigation
    {
        public AdbLocation(SpecialLocation location)
        {
            Location = location;
        }

        public AdbLocation(string path)
        {
            var specialLocation = LocationFromString(path);
            if (specialLocation is not SpecialLocation.None)
                Location = specialLocation;
            else
                Path = path;
        }

        private AdbLocation(AdbLocation other)
        {
            Path = other.Path;
            Location = other.Location;
        }

        public string Path { get; private set; } = "";

        public SpecialLocation Location { get; private set; }

        /// <summary>The device this location belongs to, once it's part of a tab's history. Null for pages and not-yet-stamped locations.</summary>
        public string? DeviceId { get; private set; }

        public AdbLocation WithDevice(string? deviceId) => new(this) { DeviceId = deviceId };

        private static readonly Dictionary<SpecialLocation, (Type Page, string Glyph)> PageLocations = new()
        {
            { SpecialLocation.Devices, (typeof(DevicesPage), "\uE8CC") },
            { SpecialLocation.Settings, (typeof(SettingsPage), "\uE713") },
            { SpecialLocation.Terminal, (typeof(TerminalPage), "\uE756") },
            { SpecialLocation.Operations, (typeof(OperationsPage), "\uEADF") },
            { SpecialLocation.Log, (typeof(LogPage), "\uE8FD") },
        };

        public bool IsPage => PageLocations.ContainsKey(Location);

        /// <summary>The app page this location stands for, or null for a folder / drive location (shown by the Explorer page).</summary>
        public Type? PageType => PageLocations.TryGetValue(Location, out var page) ? page.Page : null;

        public static AdbLocation? ForPage(Type? pageType)
        {
            foreach (var (location, page) in PageLocations)
            {
                if (page.Page == pageType)
                    return new(location);
            }

            return null;
        }

        private LogicalDeviceViewModel? DeviceFor(LogicalDeviceViewModel? fallback)
        {
            if (DeviceId is null)
                return fallback;

            return Data.DevicesObject?.LogicalDeviceViewModels?.FirstOrDefault(device => device.ID == DeviceId);
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(Path))
                    return Path;

                return Location switch
                {
                    SpecialLocation.RecycleBin => AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Trash),
                    SpecialLocation.PackageDrive => AbstractDrive.GetDriveDisplayName(AbstractDrive.DriveType.Package),
                    SpecialLocation.Back or SpecialLocation.Forward or SpecialLocation.Up => Location.ToString(),
                    SpecialLocation.DriveView => Strings.Resources.S_BUTTON_DRIVES,
                    SpecialLocation.SearchMode => SearchResultsLabel(),
                    SpecialLocation.devNull => Strings.Resources.S_LOCATION_PERM_DEL,
                    SpecialLocation.Unknown => Strings.Resources.S_LOCATION_NA,
                    SpecialLocation.Devices => Strings.Resources.S_BUTTON_DEVICES,
                    SpecialLocation.Settings => Strings.Resources.S_SETTINGS_TITLE,
                    SpecialLocation.Terminal => Strings.Resources.S_TERMINAL,
                    SpecialLocation.Operations => Strings.Resources.S_ACTION_OPERATION_PLURAL,
                    SpecialLocation.Log => Strings.Resources.S_BUTTON_LOG,
                    _ => "",
                };
            }
        }

        public string BreadcrumbLabel => GetBreadcrumbLabel(Data.ActiveDevice);

        /// <summary>Same as <see cref="BreadcrumbLabel"/>, against an explicit device - see <see cref="GetIcon"/>.</summary>
        public string GetBreadcrumbLabel(LogicalDeviceViewModel? device)
        {
            device = DeviceFor(device);

            if (IsPage)
                return DisplayName;

            if (!string.IsNullOrEmpty(Path) && ArchivePath.IsArchivePath(Path, device?.ID))
                return ArchivePath.GetBreadcrumbLabel(Path, device?.ID);

            if (Location is SpecialLocation.SearchMode)
                return SearchResultsLabel(device);

            return GetNavigationName(device);
        }

        public bool IsNavigable
        {
            get
            {
                if (!string.IsNullOrEmpty(Path))
                    return true;

                return Location switch
                {
                    SpecialLocation.DriveView => true,
                    SpecialLocation.Back => true,
                    SpecialLocation.Forward => true,
                    SpecialLocation.Up => true,
                    SpecialLocation.RecycleBin => true,
                    SpecialLocation.PackageDrive => true,
                    SpecialLocation.SearchMode => true,
                    _ => IsPage,
                };
            }
        }

        public bool IsNoneOrNavigable
        {
            get
            {
                if (!string.IsNullOrEmpty(Path))
                    return true;

                return Location is SpecialLocation.None || IsNavigable;
            }
        }

        public string StringFromLocation() => !string.IsNullOrEmpty(Path)
            ? Path
            : StringFromLocation(Location);

        public static string StringFromLocation(SpecialLocation location) =>
            $"[{Enum.GetName(location)}]";

        public static SpecialLocation LocationFromString(string? location)
        {
            if (location is string loc && loc.EndsWith(']') && loc.StartsWith('[') && Enum.TryParse<SpecialLocation>(loc.Trim('[', ']'), out var result))
            {
                return result;
            }
            
            return SpecialLocation.None;
        }

        static string SearchResultsLabel(LogicalDeviceViewModel? device = null)
        {
            var root = Data.SearchOriginPath;
            if (string.IsNullOrEmpty(root))
                return Strings.Resources.S_SEARCH;

            var pathLabel = Data.CurrentDisplayNames.TryGetValue((device?.ID ?? Data.ActiveDevice?.ID, root), out var displayName)
                ? displayName
                : FileHelper.GetFullName(root);

            return string.Format(Strings.Resources.S_SEARCH_RESULTS_IN, pathLabel);
        }

        public string HistoryName => GetHistoryName(Data.ActiveDevice);

        /// <summary>Same as <see cref="HistoryName"/>, against an explicit device - see <see cref="GetIcon"/>.</summary>
        public string GetHistoryName(LogicalDeviceViewModel? device)
        {
            device = DeviceFor(device);

            if (Data.CurrentDisplayNames.TryGetValue((device?.ID, DisplayName), out var name))
                return name;

            return DisplayName;
        }

        public string NavigationName => GetNavigationName(Data.ActiveDevice);

        /// <summary>Same as <see cref="NavigationName"/>, against an explicit device - see <see cref="GetIcon"/>.</summary>
        public string GetNavigationName(LogicalDeviceViewModel? device)
        {
            device = DeviceFor(device);

            if (IsPage)
                return DisplayName;

            // The device's own name, not the display-name cache, which is only filled once its
            // props are loaded and can be emptied while another device is being opened.
            if (Location is SpecialLocation.DriveView && device is not null)
                return device.Name;

            if (Data.CurrentDisplayNames.TryGetValue((device?.ID, StringFromLocation(Location)), out var name))
                return name;

            if (Data.CurrentDisplayNames.TryGetValue((device?.ID, DisplayName), out var display))
                return display;

            return FileHelper.GetFullName(DisplayName);
        }

        public BaseIcon? Icon => GetIcon(Data.ActiveDevice);

        /// <summary>Same as <see cref="Icon"/>, against an explicit device instead of the app-wide
        /// current one - for chrome (e.g. the tab strip) that renders a location for a tab that
        /// isn't necessarily the active one.</summary>
        public BaseIcon? GetIcon(LogicalDeviceViewModel? device)
        {
            device = DeviceFor(device);

            if (PageLocations.TryGetValue(Location, out var page))
                return new BaseIcon(page.Glyph, 16);

            if (Location is SpecialLocation.DriveView)
                return new BaseIcon(FileToIconConverter.GetPhoneIcon(16), 16);

            const int size = 16;
            var lookupKey = !string.IsNullOrEmpty(Path)
                ? Path
                : Location is not SpecialLocation.None
                    ? StringFromLocation(Location)
                    : null;

            if (lookupKey is not null && AdbExplorerConst.DRIVE_TYPES.TryGetValue(lookupKey, out var driveType))
            {
                var trashEmpty = driveType is AbstractDrive.DriveType.Trash
                    && TrashHelper.GetTrashDrive(device)?.ItemsCount is null or <= 0;

                return DriveViewModel.GetDriveIcon(driveType, size, trashEmpty);
            }

            var drive = device?.Drives.FirstOrDefault(d => d.Path == Path);
            return drive?.GetIcon(size);
        }

        public SubMenu IconSubMenu => GetIconSubMenu(Data.ActiveDevice);

        /// <summary>Same as <see cref="IconSubMenu"/>, against an explicit device - see <see cref="GetIcon"/>.</summary>
        public SubMenu GetIconSubMenu(LogicalDeviceViewModel? device, bool includeDevice = false)
        {
            var name = GetHistoryName(device);
            if (includeDevice && !IsPage && DeviceFor(device) is { } owner)
                name = $"{name} - {owner.Name}";

            return new SubMenu(new FileAction(FileAction.FileActionType.None, new(() => true, () => Data.RuntimeSettings.LocationToNavigate = this), name), GetIcon(device));
        }

        public SubMenu ExcessSubMenu => GetExcessSubMenu(Data.ActiveDevice);

        /// <summary>Same as <see cref="ExcessSubMenu"/>, against an explicit device - see <see cref="GetIcon"/>.</summary>
        public SubMenu GetExcessSubMenu(LogicalDeviceViewModel? device) =>
            new SubMenu(new FileAction(FileAction.FileActionType.None, new(() => true, () => Data.RuntimeSettings.LocationToNavigate = this), GetNavigationName(device)), GetIcon(device));

        public TextMenu NameSubMenu => GetNameSubMenu(Data.ActiveDevice);

        /// <summary>Same as <see cref="NameSubMenu"/>, against an explicit device - see <see cref="GetIcon"/>.</summary>
        public TextMenu GetNameSubMenu(LogicalDeviceViewModel? device) =>
            new TextMenu(new FileAction(FileAction.FileActionType.None, new(() => true, () => Data.RuntimeSettings.LocationToNavigate = this), GetBreadcrumbLabel(device)));

        public override bool Equals(object? other)
        {
            if (other is not AdbLocation location)
                return false;

            if (IsPage || location.IsPage)
                return Location == location.Location;

            if (DeviceId != location.DeviceId)
                return false;

            if (string.IsNullOrEmpty(Path) && string.IsNullOrEmpty(location.Path))
                return Location == location.Location;

            return Path == location.Path;
        }

        public override int GetHashCode() => 
            HashCode.Combine(Path, Location, DeviceId);
    }

    public class NavHistory : Navigation
    {
        /// <summary>Delegates to the active instance's own history — was static state directly.</summary>
        private static InstanceNavHistory Instance => Data.ActiveExplorerInstance.History;

        public static List<AdbLocation> PathHistory => Instance.PathHistory;

        public static ObservableProperty<IEnumerable<SubMenu>> MenuHistory => Instance.MenuHistory;

        /// <summary>Rebuilds history menu icons, e.g. after the recycle bin's empty/full state changes.</summary>
        public static void RefreshMenuHistory() => Instance.RefreshMenuHistory();

        /// <summary>Returns and clears the path to select after a back-navigation completes.</summary>
        public static string? TakePendingSelectionPath() => Instance.TakePendingSelectionPath();

        public static FileClass? FindBackNavigationItem(string path)
        {
            if (Data.DirList!.FileList.FirstOrDefault(item => item.FullPath == path) is { } exact)
                return exact;

            var deviceId = Data.ActiveDevice?.ID;
            if (deviceId is null || ArchivePath.IsArchivePath(Data.CurrentPath, deviceId))
                return null;

            if (!ArchivePath.IsArchivePath(path, deviceId))
                return null;

            var archivePath = ArchivePath.GetArchivePath(path, deviceId);
            return Data.DirList!.FileList.FirstOrDefault(item => item.FullPath == archivePath);
        }

        public static bool BackAvailable => Instance.BackAvailable;
        public static bool ForwardAvailable => Instance.ForwardAvailable;

        public static bool NavigationAvailable(SpecialLocation direction) => direction switch
        {
            SpecialLocation.Back => BackAvailable,
            SpecialLocation.Forward => ForwardAvailable,
            _ => throw new ArgumentException("Only Back & Forward navigation is accepted"),
        };

        public static bool NavigateBF(SpecialLocation direction)
        {
            if (direction is not SpecialLocation.Back and not SpecialLocation.Forward)
                throw new ArgumentException("Only Back & Forward navigation is accepted");

            if (!NavigationAvailable(direction))
                return false;

            var fileAction = direction is SpecialLocation.Forward
                ? FileAction.FileActionType.Forward
                : FileAction.FileActionType.Back;
            var command = AppActions.List.First(action => action.Name == fileAction).Command.Command as CommandHandler;

            Data.RuntimeSettings.LocationToNavigate = new(direction);
            command.OnExecute.Value ^= true;

            return true;
        }

        public static AdbLocation GoBack() => Instance.GoBack();

        public static AdbLocation GoForward() => Instance.GoForward();

        public static AdbLocation Current => Instance.Current;

        public static void Navigate(string path) => Instance.Navigate(path);

        public static void Navigate(SpecialLocation location) => Instance.Navigate(location);

        /// <summary>
        /// For any non back / forward navigation
        /// </summary>
        /// <param name="path"></param>
        public static void Navigate(AdbLocation path) => Instance.Navigate(path);

        public static void Reset() => Instance.Reset();
    }
}
