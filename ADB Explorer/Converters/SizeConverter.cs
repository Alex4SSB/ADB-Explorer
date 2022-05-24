using System;
using System.Collections.Generic;

namespace ADB_Explorer.Converters
{
    public static class UnitConverter
    {
        private static readonly Dictionary<int,string> scale_table = new() { { -3, "n" }, { -2, "u" }, { -1, "m" }, { 0, "" }, { 1, "K" }, { 2, "M" }, { 3, "G" }, { 4, "T" }, { 5, "P" }, { 6, "E" } };

        public static string ToSize(this UInt64 bytes, bool scaleSpace = false, int bigRound = 1, int smallRound = 0)
        {
            int scale = (bytes == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log(bytes, 1024), 2))); // 0 <= scale <= 6
            double value = bytes / Math.Pow(1024, scale);
            return $"{(Math.Round(value, value < 100 ? bigRound : smallRound))}{(scaleSpace ? " " : "")}{scale_table[scale]}B";
        }

        public static string ToTime(this decimal? seconds, bool scaleSpace = false)
        {
            if (seconds is null)
                return "";

            TimeSpan span = TimeSpan.FromSeconds((double)seconds);
            string resolution;
            string value;

            if (span.Days == 0)
            {
                if (span.Hours == 0)
                {
                    if (span.Minutes == 0)
                    {
                        if (span.Seconds == 0)
                        {
                            value = $"{Math.Round(span.TotalMilliseconds, span.TotalMilliseconds < 100 ? 2 : 1)}";
                            resolution = "ms";
                        }
                        else
                        {
                            value = $"{Math.Round(span.TotalSeconds, 2)}";
                            resolution = "s";
                        }
                    }
                    else
                    {
                        value = $"{span.Minutes}:{span.Seconds:00}";
                        resolution = "m";
                    }
                }
                else
                {
                    value = $"{span.Hours}:{span.Minutes:00}:{span.Seconds:00}";
                    resolution = "h";
                }
            }
            else
            {
                value = $"{Math.Round(span.TotalHours)}";
                resolution = "h";
            }

            return $"{value}{(scaleSpace ? " " : "")}{resolution}";
        }
    }
}
