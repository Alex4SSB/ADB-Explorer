using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models
{
    public static class NavHistory
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
        }

        public static string DisplayName(this SpecialLocation location) => location switch
        {
            SpecialLocation.RecycleBin => AdbExplorerConst.DRIVE_DISPLAY_NAMES[AbstractDrive.DriveType.Trash],
            SpecialLocation.PackageDrive => AdbExplorerConst.DRIVE_DISPLAY_NAMES[AbstractDrive.DriveType.Package],
            SpecialLocation.Back or SpecialLocation.Forward or SpecialLocation.Up => location.ToString(),
            SpecialLocation.DriveView => "Device Drives",
            SpecialLocation.devNull => "Permanent Deletion",
            _ => "",
        };

        public static bool IsNavigable(this SpecialLocation location) => location switch
        {
            SpecialLocation.DriveView => true,
            SpecialLocation.Back => true,
            SpecialLocation.Forward => true,
            SpecialLocation.Up => true,
            SpecialLocation.RecycleBin => true,
            SpecialLocation.PackageDrive => true,
            _ => false,
        };

        public static string StringFromLocation(SpecialLocation location) => $"[{Enum.GetName(typeof(SpecialLocation), location)}]";

        public static SpecialLocation LocationFromString(object location)
        {
            if (location is string loc && loc.EndsWith(']') && loc.StartsWith('[') && Enum.TryParse<SpecialLocation>(loc.Trim('[', ']'), out var result))
            {
                return result;
            }
            else if (location is SpecialLocation special)
                return special;
            else
                return SpecialLocation.None;
        }

        public static List<object> PathHistory { get; set; } = new();

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

            var fileAction = direction is not SpecialLocation.Forward
                ? FileAction.FileActionType.Back
                : FileAction.FileActionType.Forward;
            var command = AppActions.List.First(action => action.Name == fileAction).Command.Command as CommandHandler;

            Data.RuntimeSettings.LocationToNavigate = direction;
            command.OnExecute.Value ^= true;

            return true;
        }

        public static object GoBack()
        {
            if (!BackAvailable) return null;

            historyIndex--;

            return PathHistory[historyIndex];
        }

        public static object GoForward()
        {
            if (!ForwardAvailable) return null;

            historyIndex++;

            return PathHistory[historyIndex];
        }

        public static object Current => PathHistory.Count > 0 ? PathHistory[historyIndex] : null;

        /// <summary>
        /// For any non back / forward navigation
        /// </summary>
        /// <param name="path"></param>
        public static void Navigate(object path)
        {
            if (PathHistory.Count > 0 && PathEquals(path, PathHistory[historyIndex]))
            {
                return;
            }

            if (ForwardAvailable)
            {
                PathHistory.RemoveRange(historyIndex + 1, PathHistory.Count - historyIndex - 1);
            }

            PathHistory.Add(path);
            historyIndex++;
        }

        public static void Reset()
        {
            PathHistory.Clear();
            historyIndex = -1;
        }

        public static bool PathEquals(object lval, object rval)
        {
            if (lval is string lstr && rval is string rstr)
                return lstr == rstr;

            return lval.GetType() == rval.GetType();
        }
    }
}
