using System.Collections.Generic;

namespace ADB_Explorer.Models
{
    public static class NavHistory
    {
        public enum SpecialLocation
        {
            DriveView,
        }

        public static List<object> PathHistory { get; set; } = new();

        private static int historyIndex = -1;

        public static bool BackAvailable { get { return historyIndex > 0; } }
        public static bool ForwardAvailable { get { return historyIndex < PathHistory.Count - 1; } }

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

        public static object Current => PathHistory[historyIndex];

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
            if (lval.GetType() != rval.GetType())
                return false;

            if (lval is string lstr && rval is string rstr)
                return lstr == rstr;

            return lval == rval;
        }
    }
}
