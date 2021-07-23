using System.Collections.Generic;

namespace ADB_Explorer.Models
{
    public static class NavHistory
    {
        public static List<string> PathHistory = new();

        private static int historyIndex = -1;

        public static bool BackAvailable { get { return historyIndex > 0; } }
        public static bool ForwardAvailable { get { return historyIndex < PathHistory.Count - 1; } }

        public static string GoBack()
        {
            if (!BackAvailable) return null;

            historyIndex--;

            return PathHistory[historyIndex];
        }

        public static string GoForward()
        {
            if (!ForwardAvailable) return null;

            historyIndex++;

            return PathHistory[historyIndex];
        }

        /// <summary>
        /// For any non back / forward navigation
        /// </summary>
        /// <param name="path"></param>
        public static void Navigate(string path)
        {
            if (PathHistory.Count > 0 && path == PathHistory[historyIndex])
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
    }
}
