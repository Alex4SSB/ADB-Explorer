using System;

namespace ADB_Explorer.Converters
{
    public static class SizeConverter
    {
        private static readonly string[] logs = { "", "K", "M", "G" };

        public static string ToSize(this ulong num) => ((double)num).ToSize();

        public static string ToSize(this double num)
        {
            int n = 0;

            while (num >= 1000 && n < logs.Length - 1)
            {
                num /= 1000;
                n++;
            }

            return $"{Math.Round(num)}{logs[n]}B";
        }
    }
}
