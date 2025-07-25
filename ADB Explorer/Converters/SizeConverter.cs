namespace ADB_Explorer.Converters
{
    public static class UnitConverter
    {
        private static readonly Dictionary<int,string> byteScaleTable = new()
        {
            { 0, Strings.Resources.BYTES },
            { 1, Strings.Resources.KILO },
            { 2, Strings.Resources.MEGA },
            { 3, Strings.Resources.GIGA },
            { 4, Strings.Resources.TERA },
            { 5, Strings.Resources.PETA },
            { 6, Strings.Resources.EXA }
        };

        /// <summary>
        /// Converts a byte value to a human-readable size string with appropriate units.
        /// </summary>
        /// <remarks>The method uses a logarithmic scale to determine the appropriate unit (e.g., KB, MB,
        /// GB) based on the input size. The result is formatted with the specified rounding rules and optional spacing
        /// between the value and the unit.</remarks>
        /// <param name="bytes">The size in bytes to be converted. Must be a non-negative value.</param>
        /// <param name="scaleSpace">A boolean value indicating whether to include a space between the numeric value and the unit. <see
        /// langword="true"/> to include a space; otherwise, <see langword="false"/>.</param>
        /// <param name="bigRound">The number of decimal places to round to when the numeric value is less than 100.</param>
        /// <param name="smallRound">The number of decimal places to round to when the numeric value is 100 or greater.</param>
        /// <returns>A string representing the size in a human-readable format, including the appropriate unit (e.g., KB, MB,
        /// GB).</returns>
        public static string BytesToSize(this UInt64 bytes, bool scaleSpace = false, int bigRound = 1, int smallRound = 0)
        {
            int scale = (bytes == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log(bytes, 1024), 2))); // 0 <= scale <= 6
            double value = bytes / Math.Pow(1024, scale);
            var format = scaleSpace
                ? byteScaleTable[scale]
                : byteScaleTable[scale].Replace(" ", "");

            return string.Format(format, Math.Round(value, value < 100 ? bigRound : smallRound));
        }

        private static readonly Dictionary<int, string> ampScaleTable = new()
        {
            { -3, Strings.Resources.NANO },
            { -2, Strings.Resources.MICRO },
            { -1, Strings.Resources.MILLI },
            { 0, "" },
        };

        public static string AmpsToSize(this double source, bool scaleSpace = false, int bigRound = 1, int smallRound = 0)
        {
            int scale = (source == 0) ? 0 : Convert.ToInt32(Math.Floor(Math.Round(Math.Log(Math.Abs(source), 1000), 2)));
            double value = source / Math.Pow(1000, scale);

            // Currently, this is designed to handle values of nano Ampere up to Ampere.
            // If your device draws more than that and requires a dedicated GFCI - you probably have bigger problems.
            return $"{(Math.Round(value, value < 100 ? bigRound : smallRound))}{(scaleSpace ? " " : "")}{ampScaleTable[scale]}";
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
