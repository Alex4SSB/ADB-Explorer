using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ADB_Explorer.Converters;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Helpers;

namespace ADB_Explorer.Models
{
    public class FileClass : FileStat
    {
        public FileClass() { }

        public static FileClass GenerateAndroidFile(FileStat fileStat)
        {
            return new FileClass
            {
                Name = fileStat.Name,
                Path = fileStat.Path,
                Type = fileStat.Type,
                Size = fileStat.Size,
                ModifiedTime = fileStat.ModifiedTime,
                Icon = fileStat.Type switch
                {
                    FileType.File => IconToBitmapSource(ShellIconManager.GetExtensionIcon(System.IO.Path.GetExtension(fileStat.Path), ShellIconManager.IconSize.Large)),
                    _ => folderIconBitmapSource
                }
            };
        }

        public static FileClass GenerateWindowsFile(string path, FileType type)
        {
            return new FileClass
            {
                Path = path,
                Type = type,
                TypeName = type.ToString(),
                Name = type switch
                {
                    FileType.Drive => path,
                    FileType.Folder => System.IO.Path.GetFileName(path),
                    FileType.File => System.IO.Path.GetFileNameWithoutExtension(path),
                    FileType.Parent => "..",
                    _ => throw new NotImplementedException()
                }
            };
        }

        public object Icon { get; set; }
        public string TypeName { get; set; }
        public string ModifiedTimeString => ModifiedTime.ToString(CultureInfo.CurrentCulture.DateTimeFormat);
        public string SizeString => Size.ToSize();

        private static BitmapSource IconToBitmapSource(System.Drawing.Icon icon)
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellIconManager.GetFileIcon(System.IO.Path.GetTempPath(), ShellIconManager.IconSize.Large));
    }
}
