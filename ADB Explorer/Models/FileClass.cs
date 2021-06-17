using ADB_Explorer.Converters;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Helpers;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Models
{
    public class FileClass : FileStat
    {
        public FileClass() { }

        private const ShellIconManager.IconSize iconSize = ShellIconManager.IconSize.Small;

        public static FileClass GenerateAndroidFile(FileStat fileStat)
        {
            return new FileClass
            {
                FileName = fileStat.FileName,
                Path = fileStat.Path,
                Type = fileStat.Type,
                Size = fileStat.Size,
                ModifiedTime = fileStat.ModifiedTime,
                Icon = fileStat.Type switch
                {
                    FileType.File => IconToBitmapSource(ExtIcon(System.IO.Path.GetExtension(fileStat.Path), iconSize)),
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
                FileName = type switch
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

        private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellIconManager.GetFileIcon(System.IO.Path.GetTempPath(), iconSize));

        private static Icon ExtIcon(string extension, ShellIconManager.IconSize iconSize)
        {
            // No extension -> "*" which means unknown file 
            if (extension == string.Empty)
            {
                extension = "*";
            }

            Icon icon;
            if (!FileIcons.ContainsKey(extension))
            {
                icon = ShellIconManager.GetExtensionIcon(extension, iconSize);
                FileIcons.Add(extension, icon);
            }
            else
                icon = FileIcons[extension];

            return icon;
        }
    }
}
