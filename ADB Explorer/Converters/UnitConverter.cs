using ADB_Explorer.Models;

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
        };

        public static string BytesToSize(this long bytes,
                                         bool scaleSpace = false,
                                         Services.AppSettings.FileSizeDisplay? fileSizeMode = null,
                                         int? fileSizeDecimal = null)
        {
            if (bytes < 0)
                return "";

            var mode = fileSizeMode ?? Data.Settings.FileSizeMode;
            int scale = 0;
            int round = 0;

            if (bytes == 0 || mode is Services.AppSettings.FileSizeDisplay.B)
            {

            }
            else if (mode is Services.AppSettings.FileSizeDisplay.K)
            {
                scale = 1;
            }
            else if (mode > Services.AppSettings.FileSizeDisplay.K)
            {
                scale = Convert.ToInt32(Math.Floor(Math.Round(Math.Log(bytes, 1024), 2)));
                var max = Math.Min(byteScaleTable.Keys.Last(), (int)mode);

                scale = Math.Clamp(scale, 0, max);
                round = fileSizeDecimal ?? Data.Settings.FileSizeDecimal;
            }

            double value = bytes / Math.Pow(1024, scale);
            var format = scaleSpace
                ? byteScaleTable[scale]
                : byteScaleTable[scale].Replace(" ", "");

            return string.Format(format, FormatFileSizeNumber(Math.Round(value, round), round));
        }

        public static string BytesToDriveSize(this long bytes, bool scaleSpace = false) =>
            bytes.BytesToSize(scaleSpace, Services.AppSettings.FileSizeDisplay.KMG, 1);

        private static string FormatFileSizeNumber(double value, int decimalPlaces)
        {
            var pattern = decimalPlaces == 0
                ? "#,##0"
                : $"#,##0.{new string('#', decimalPlaces)}";

            return value.ToString(pattern, Data.Settings.ActualFormatCulture);
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

        public static string ToTime(this double? seconds, bool scaleSpace = false, bool useMilli = true, int digits = 2)
        {
            if (seconds is null)
                return "";

            TimeSpan span = TimeSpan.FromSeconds(seconds.Value);
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
