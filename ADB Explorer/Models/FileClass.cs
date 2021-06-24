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

        public FileClass(string fileName, string path, FileType type, bool isLink = false, UInt64? size = null, DateTime? modifiedTime = null) :
            base(fileName, path, type, isLink, size, modifiedTime)
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
                modifiedTime: fileStat.ModifiedTime,
                isLink: fileStat.IsLink
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
        public string ModifiedTimeString => ModifiedTime?.ToString(CultureInfo.CurrentCulture.DateTimeFormat);
        public string SizeString => Size?.ToSize();

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

        private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellIconManager.GetFileIcon(System.IO.Path.GetTempPath(), iconSize, false));
        private static readonly BitmapSource folderLinkIconBitmapSource = IconToBitmapSource(ShellIconManager.GetFileIcon(System.IO.Path.GetTempPath(), iconSize, true));
        private static readonly BitmapSource parentIconBitmapSource = IconToBitmapSource(ShellIconManager.ExtractIconByIndex("Shell32.dll", 45, iconSize));
        private static readonly BitmapSource unknownFileIconBitmapSource = IconToBitmapSource(ShellIconManager.ExtractIconByIndex("Shell32.dll", 175, iconSize));

        private static BitmapSource IconToBitmapSource(System.Drawing.Icon icon)
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private object GetIcon()
        {
            return Type switch
            {
                FileType.File => IconToBitmapSource(ExtIcon(System.IO.Path.GetExtension(FileName), iconSize, IsLink)),
                FileType.Folder => IsLink ? folderLinkIconBitmapSource : folderIconBitmapSource,
                FileType.Parent => parentIconBitmapSource,
                FileType.Unknown => unknownFileIconBitmapSource,
                _ => IconToBitmapSource(ExtIcon(string.Empty, iconSize, IsLink))
            };
        }

        private static Icon ExtIcon(string extension, ShellIconManager.IconSize iconSize, bool isLink)
        {
            // No extension -> "*" which means unknown file 
            if (extension == string.Empty)
            {
                extension = "*";
            }

            Icon icon;
            var iconId = new Tuple<string, bool>(extension, isLink);
            if (!FileIcons.ContainsKey(iconId))
            {
                icon = ShellIconManager.GetExtensionIcon(extension, iconSize, isLink);
                FileIcons.Add(iconId, icon);
            }
            else
            {
                icon = FileIcons[iconId];
            }

            return icon;
        }

        protected override void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (propertyName == "FileName" || propertyName == "Type" || propertyName == "IsLink")
            {
                Icon = GetIcon();
            }
            
            base.NotifyPropertyChanged(propertyName);
        }
    }
}
