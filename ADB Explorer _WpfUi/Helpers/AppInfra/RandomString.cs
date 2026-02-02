using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Helpers;

public class RandomString
{
    public static string GetUniqueKey(int size, char[] chars = null)
    {
        if (chars is null)
        {
            chars = WIFI_PAIRING_ALPHABET;
        }

        byte[] data = new byte[4 * size];
        RandomNumberGenerator.Create().GetBytes(data);
        
        StringBuilder result = new();
        for (int i = 0; i < size; i++)
        {
            var rnd = BitConverter.ToUInt32(data, i * 4);
            var idx = rnd % chars.Length;

            result.Append(chars[idx]);
        }

        return result.ToString();
    }
}
