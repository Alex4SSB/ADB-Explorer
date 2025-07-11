﻿namespace ADB_Explorer.Converters
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

        public static string ToSize(this double source, bool scaleSpace = false, int bigRound = 1, int smallRound = 0)
        {
            int scale = (source == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log(Math.Abs(source), 1000), 2)));
            double value = source / Math.Pow(1000, scale);
            return $"{(Math.Round(value, value < 100 ? bigRound : smallRound))}{(scaleSpace ? " " : "")}{scale_table[scale]}";
        }

        public static string ToTime(this decimal? seconds, bool scaleSpace = false, bool useMilli = true, int digits = 2)
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
                        if (useMilli && span.Seconds == 0)
                        {
                            value = $"{Math.Round(span.TotalMilliseconds, span.TotalMilliseconds < 100 ? digits : 1)}";
                            resolution = Strings.Resources.S_MILLISECONDS_SHORT;
                        }
                        else
                        {
                            value = $"{Math.Round(span.TotalSeconds, digits)}";
                            resolution = Strings.Resources.S_SECONDS_SHORT;
                        }
                    }
                    else
                    {
                        value = $"{span.Minutes}:{span.Seconds:00}";
                        resolution = Strings.Resources.S_MINUTES_SHORT;
                    }
                }
                else
                {
                    value = $"{span.Hours}:{span.Minutes:00}:{span.Seconds:00}";
                    resolution = Strings.Resources.S_HOURS_SHORT;
                }
            }
            else
            {
                value = $"{Math.Round(span.TotalHours)}";
                resolution = Strings.Resources.S_HOURS_SHORT;
            }

            return string.Format(resolution, $"{value}{(scaleSpace ? " " : "")}");
        }
    }
}
