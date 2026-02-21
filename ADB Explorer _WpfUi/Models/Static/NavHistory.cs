using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

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
            devNull,
            Unknown,
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
            Path = path;
        }

        public string Path { get; private set; }

        public SpecialLocation Location { get; private set; }

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
                    SpecialLocation.devNull => Strings.Resources.S_LOCATION_PERM_DEL,
                    SpecialLocation.Unknown => Strings.Resources.S_LOCATION_NA,
                    _ => "",
                };
            }
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
                    _ => false,
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

        public static SpecialLocation LocationFromString(string location)
        {
            if (location is string loc && loc.EndsWith(']') && loc.StartsWith('[') && Enum.TryParse<SpecialLocation>(loc.Trim('[', ']'), out var result))
            {
                return result;
            }
            
            return SpecialLocation.None;
        }

        public string HistoryName
        {
            get
            {
                if (Data.CurrentDisplayNames.TryGetValue(DisplayName, out var name))
                    return name;

                return DisplayName;
            }
        }

        public string NavigationName
        {
            get
            {
                if (Data.CurrentDisplayNames.TryGetValue(StringFromLocation(Location), out var name))
                    return name;

                if (Data.CurrentDisplayNames.TryGetValue(DisplayName, out var display))
                    return display;

                return FileHelper.GetFullName(DisplayName);
            }
        }

        public string Icon
        {
            get
            {
                if (Location is SpecialLocation.DriveView)
                    return AppActions.Icons[FileAction.FileActionType.Home];

                return Data.DevicesObject.Current?.Drives.FirstOrDefault(d => d.Path == Path)?.DriveIcon
                    ?? null;
            }
        }

        public SubMenu IconSubMenu =>
            new SubMenu(new FileAction(FileAction.FileActionType.None, new(() => true, () => Data.RuntimeSettings.LocationToNavigate = this), HistoryName), Icon);

        public SubMenu ExcessSubMenu =>
            new SubMenu(new FileAction(FileAction.FileActionType.None, new(() => true, () => Data.RuntimeSettings.LocationToNavigate = this), NavigationName), Icon);

        public TextMenu NameSubMenu =>
            new TextMenu(new FileAction(FileAction.FileActionType.None, new(() => true, () => Data.RuntimeSettings.LocationToNavigate = this), NavigationName));

        public override bool Equals(object other)
        {
            if (other is not AdbLocation location)
                return false;

            if (string.IsNullOrEmpty(Path) && string.IsNullOrEmpty(location.Path))
                return Location == location.Location;

            return Path == location.Path;
        }

        public override int GetHashCode() => 
            HashCode.Combine(Path, Location);
    }

    public class NavHistory : Navigation
    {
        public static List<AdbLocation> PathHistory { get; private set; } = [];

        public static ObservableProperty<IEnumerable<SubMenu>> MenuHistory { get; private set; } = new() { Value = [] };

        private static void UpdateMenuHistory()
        {
            MenuHistory.Value = PathHistory
                .Distinct()
                .Where(path => !path.Equals(Current))
                .Select(path => path.IconSubMenu);
        }

        private static int historyIndex = -1;

        public static bool BackAvailable { get { return historyIndex > 0; } }
        public static bool ForwardAvailable { get { return historyIndex < PathHistory.Count - 1; } }

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
            {
                if (Data.FileActions.IsDriveViewVisible)
                    DriveHelper.ClearSelectedDrives();

                return false;
            }

            var fileAction = direction is SpecialLocation.Forward
                ? FileAction.FileActionType.Forward
                : FileAction.FileActionType.Back;
            var command = AppActions.List.First(action => action.Name == fileAction).Command.Command as CommandHandler;

            Data.RuntimeSettings.LocationToNavigate = new(direction);
            command.OnExecute.Value ^= true;

            return true;
        }

        public static AdbLocation GoBack()
        {
            if (!BackAvailable) return null;

            historyIndex--;

            UpdateMenuHistory();

            return PathHistory[historyIndex];
        }

        public static AdbLocation GoForward()
        {
            if (!ForwardAvailable) return null;

            historyIndex++;

            UpdateMenuHistory();

            return PathHistory[historyIndex];
        }

        public static AdbLocation Current => PathHistory.Count > 0 ? PathHistory[historyIndex] : null;

        public static void Navigate(string path) => Navigate(new AdbLocation(path));

        public static void Navigate(SpecialLocation location) => Navigate(new AdbLocation(location));

        /// <summary>
        /// For any non back / forward navigation
        /// </summary>
        /// <param name="path"></param>
        public static void Navigate(AdbLocation path)
        {
            if (PathHistory.Count > 0 && path.Equals(PathHistory[historyIndex]))
            {
                return;
            }
            
            if (ForwardAvailable)
            {
                PathHistory.RemoveRange(historyIndex + 1, PathHistory.Count - historyIndex - 1);
            }

            PathHistory.Add(path);
            historyIndex++;

            UpdateMenuHistory();
        }

        public static void Reset()
        {
            PathHistory.Clear();
            historyIndex = -1;

            UpdateMenuHistory();
        }
    }
}
