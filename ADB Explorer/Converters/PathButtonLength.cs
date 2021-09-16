namespace ADB_Explorer.Converters
{
    public static class PathButtonLength
    {
        /// <summary>
        /// Returns the approximate length of the path button containing the supplied text.
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public static int ButtonLength(string content) => (content.Length * 7) + 32;
    }
}
