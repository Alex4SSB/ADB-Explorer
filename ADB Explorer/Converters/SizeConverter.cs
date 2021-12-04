using System;
using System.Collections.Generic;

namespace ADB_Explorer.Converters
{
    public static class SizeConverter
    {
        private static readonly Dictionary<int,string> scale_table = new() { { -3, "n" }, { -2, "u" }, { -1, "m" }, { 0, "" }, { 1, "K" }, { 2, "M" }, { 3, "G" }, { 4, "T" }, { 5, "P" }, { 6, "E" } };

        public static string ToSize(this UInt64 bytes, bool scaleSpace = false, int bigRound = 1, int smallRound = 0)
        {
            int scale = (bytes == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log(bytes, 1024), 2))); // 0 <= scale <= 6
            double value = bytes / Math.Pow(1024, scale);
            return $"{(Math.Round(value, value < 100 ? bigRound : smallRound))}{(scaleSpace ? " " : "")}{scale_table[scale]}B";
        }

        public static string ToTime(this decimal? milli, bool scaleSpace = false)
        {
            if (milli is null)
                return "";

            int scale = (milli == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log((double)milli, 1000), 2)));
            double value = (double)milli / Math.Pow(1000, scale);

            return $"{value}{(scaleSpace ? " " : "")}{scale_table[scale]}S";
        }
    }
}
