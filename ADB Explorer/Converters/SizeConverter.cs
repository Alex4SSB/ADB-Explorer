using System;

namespace ADB_Explorer.Converters
{
    public static class SizeConverter
    {
        private static readonly string[] scale_table = { "", "K", "M", "G", "T", "P", "E" };

        public static string ToSize(this UInt64 bytes, bool scaleSpace = false, int bigRound = 1, int smallRound = 0)
        {
            int scale = (bytes == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log(bytes, 1024), 2))); // 0 <= scale <= 6
            double value = bytes / Math.Pow(1024, scale);
            return $"{(Math.Round(value, value < 100 ? bigRound : smallRound))}{(scaleSpace ? " " : "")}{scale_table[scale]}B";
        }
    }
}
