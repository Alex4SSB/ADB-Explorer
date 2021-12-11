namespace ADB_Explorer.Converters
{
    public static class FileTypeClass
    {
        public enum FileType
        {
            Socket,
            File,
            BlockDevice,
            Folder,
            CharDevice,
            FIFO,
            Drive,
            Unknown
        }

        public static string Name(this FileType type)
        {
            var original = type.ToString();
            string name = original[0] + "";

            for (int i = 1; i < original.Length; i++)
            {
                if (char.IsUpper(original[i]) && char.IsLower(original[i - 1]))
                    name += $" {original[i]}";
                else
                    name += original[i];
            }

            return name;
        }
    }
}
