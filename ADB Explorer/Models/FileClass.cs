using ADB_Explorer.Converters;
using ADB_Explorer.Core.Models;
using ADB_Explorer.Helpers;
using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Models
{
    public class FileClass : FileStat
    {
        private const ShellIconManager.IconSize iconSize = ShellIconManager.IconSize.Small;

        public FileClass(string fileName, string path, FileType type, UInt64 size, DateTime modifiedTime) :
            base(fileName, path, type, size, modifiedTime)
        {
            icon = GetIcon();
        }

        public static FileClass GenerateAndroidFile(FileStat fileStat)
        {
            return new FileClass
            (
                fileName: fileStat.FileName,
                path: fileStat.Path,
                type: fileStat.Type,
                size: fileStat.Size,
                modifiedTime: fileStat.ModifiedTime
            );
        }

        //public static FileClass GenerateWindowsFile(string path, FileType type)
        //{
        //    return new FileClass
        //    {
        //        Path = path,
        //        Type = type,
        //        TypeName = ,
        //        FileName = type switch
        //        {
        //            FileType.Drive => path,
        //            FileType.Folder => System.IO.Path.GetFileName(path),
        //            FileType.File => System.IO.Path.GetFileNameWithoutExtension(path),
        //            FileType.Parent => "..",
        //            _ => throw new NotImplementedException()
        //        }
        //    };
        //}

        public string TypeName => Type.ToString();
        public string ModifiedTimeString => ModifiedTime.ToString(CultureInfo.CurrentCulture.DateTimeFormat);
        public string SizeString => Size.ToSize();

        private object icon;

        public object Icon
        {
            get { return icon; }
            private set
            {
                icon = value;
                NotifyPropertyChanged();
            }
        }

        private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellIconManager.GetFileIcon(System.IO.Path.GetTempPath(), iconSize));

        private static BitmapSource IconToBitmapSource(System.Drawing.Icon icon)
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private object GetIcon()
        {
            return Type switch
            {
                FileType.File => IconToBitmapSource(ExtIcon(System.IO.Path.GetExtension(FileName), iconSize)),
                _ => folderIconBitmapSource
            };
        }

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
            {
                icon = FileIcons[extension];
            }

            return icon;
        }

        protected override void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (propertyName == "FileName" || propertyName == "Type")
            {
                Icon = GetIcon();
            }
            
            base.NotifyPropertyChanged(propertyName);
        }
    }
}
