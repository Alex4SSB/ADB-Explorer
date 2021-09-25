using System.Windows;

namespace ADB_Explorer.Converters
{
    public static class PathButtonLength
    {
        /// <summary>
        /// Returns the length of a path button.
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public static double ButtonLength(UIElement item)
        {
            item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return item.DesiredSize.Width;
        }
    }
}
