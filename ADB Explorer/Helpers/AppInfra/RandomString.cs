using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Helpers;

public class RandomString
{
    public static string GetUniqueKey(int size)
    {
        byte[] data = new byte[4 * size];
        RandomNumberGenerator.Create().GetBytes(data);
        
        StringBuilder result = new();
        for (int i = 0; i < size; i++)
        {
            var rnd = BitConverter.ToUInt32(data, i * 4);
            var idx = rnd % ALPHABET.Length;

            result.Append(ALPHABET[idx]);
        }

        return result.ToString();
    }
}
