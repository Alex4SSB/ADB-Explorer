using System;

namespace ADB_Explorer.Converters
{
    public static class SizeConverter
    {
        private static readonly string[] scale_table = { "", "K", "M", "G", "T", "P", "E" };

        public static string ToSize(this UInt64 bytes)
        {
            int scale = (bytes == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024))); // 0 <= scale <= 6
            return $"{Math.Round(bytes / Math.Pow(1024, scale), 1)}{scale_table[scale]}B";
        }
    }
}
