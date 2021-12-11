using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using static ADB_Explorer.Converters.FileTypeClass;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Models
{
    public class FileClass : FileStat
    {
        private const ShellInfoManager.IconSize iconSize = ShellInfoManager.IconSize.Small;

        public FileClass(string fileName, string path, FileType type, bool isLink = false, UInt64? size = null, DateTime? modifiedTime = null) :
            base(fileName, path, type, isLink, size, modifiedTime)
        {
            icon = GetIcon();
            typeName = GetTypeName();
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

        private bool? isApk = null;
        public bool IsApk
        {
            get
            {
                if (isApk is null)
                {
                    isApk = Array.IndexOf(AdbExplorerConst.APK_NAMES, Extension.ToUpper()) > -1;
                }

                return (bool)isApk;
            }
        }

        public string NoExtName
        {
            get
            {
                if (Type is not FileType.File || IsHidden && FileName.Split('.').Length == 2)
                    return FileName;
                else
                    return FileName[..(FileName.Length - Extension.Length)];
            }
        }

        public bool IsHidden
        {
            get
            {
                return FileName.StartsWith('.');
            }
        }

        private string extension;
        public string Extension
        {
            get
            {
                if (Type is not FileType.File)
                    return "";

                if (string.IsNullOrEmpty(extension))
                    extension = System.IO.Path.GetExtension(FileName);

                return extension;
            }
        }

        public string GetTypeName(string fileName)
        {
            if (IsApk)
                return "Android Application Package";
            {
                var name = ShellInfoManager.GetShellFileType(fileName);

                if (name.EndsWith("?? File"))
                    return $"{fileName[(fileName.LastIndexOf('.') + 1)..]} File";
                else
                    return name;
            }
        }

        private string typeName;
        public string TypeName
        {
            get { return typeName; }
            private set
            {
                typeName = value;
                NotifyPropertyChanged();
            }
        }
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

        private static readonly BitmapSource folderIconBitmapSource = IconToBitmapSource(ShellInfoManager.GetFileIcon(System.IO.Path.GetTempPath(), iconSize, false));
        private static readonly BitmapSource folderLinkIconBitmapSource = IconToBitmapSource(ShellInfoManager.GetFileIcon(System.IO.Path.GetTempPath(), iconSize, true));
        private static readonly BitmapSource unknownFileIconBitmapSource = IconToBitmapSource(ShellInfoManager.ExtractIconByIndex("Shell32.dll", 175, iconSize));

        private static BitmapSource IconToBitmapSource(Icon icon)
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }

        private object GetIcon()
        {
            return Type switch
            {
                FileType.File => IconToBitmapSource(ExtIcon(Extension, iconSize, IsLink, IsApk)),
                FileType.Folder => IsLink ? folderLinkIconBitmapSource : folderIconBitmapSource,
                FileType.Unknown => unknownFileIconBitmapSource,
                _ => IconToBitmapSource(ExtIcon(string.Empty, iconSize, IsLink))
            };
        }

        private string GetTypeName()
        {
            return Type switch
            {
                FileType.File => IsLink ? "Link" : GetTypeName(FileName),
                FileType.Folder => IsLink ? "Link" : "Folder",
                FileType.Unknown => "",
                _ => Type.Name(),
            };
        }

        private static Icon ExtIcon(string extension, ShellInfoManager.IconSize iconSize, bool isLink, bool isApk = false)
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
                if (isApk)
                {
                    icon = Properties.Resources.APK_icon;
                }
                else
                    icon = ShellInfoManager.GetExtensionIcon(extension, iconSize, isLink);

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
                TypeName = GetTypeName();
            }

            base.NotifyPropertyChanged(propertyName);
        }
    }
}
