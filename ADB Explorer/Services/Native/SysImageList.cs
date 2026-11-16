// Part of FileToIcon from Code Project article by Leung Yat Chun
// https://www.codeproject.com/Articles/32059/WPF-Filename-To-Icon-Converter
// Used and modified under the LGPLv3 license

using System.Drawing;

namespace ADB_Explorer.Services;

using Vanara.PInvoke;
using static Vanara.PInvoke.ComCtl32;
using static Vanara.PInvoke.Shell32;

public static partial class NativeMethods
{
    /// <summary>
    /// The shell's system image list at one size, for the icons File Explorer shows for files and folders.
    /// </summary>
    public sealed class SysImageList : IDisposable
    {
        private IImageList? _imageList;
        private SHIL _size;

        public SysImageList(SHIL size)
        {
            _size = size;
            Create();
        }

        /// <summary>The size of the system image list to read from.</summary>
        public SHIL ImageListSize
        {
            get => _size;
            set
            {
                _size = value;
                Create();
            }
        }

        /// <summary>A copy of the icon at <paramref name="index"/>; <see langword="null"/> when there is none.</summary>
        public Icon? Icon(int index)
        {
            if (_imageList is null)
                return null;

            try
            {
                // SafeHICON destroys its icon once disposed, so the returned icon is a copy
                using var hIcon = _imageList.GetIcon(index, IMAGELISTDRAWFLAGS.ILD_TRANSPARENT);
                if (hIcon.IsInvalid)
                    return null;

                return (Icon)System.Drawing.Icon.FromHandle(hIcon.DangerousGetHandle()).Clone();
            }
            catch (COMException)
            {
                return null;
            }
        }

        /// <summary>The index of the icon for <paramref name="fileName"/>, using the cached one where possible.</summary>
        public int IconIndex(string fileName) => IconIndex(fileName, false);

        /// <param name="forceLoadFromDisk">Reads the file itself, instead of only when no cached icon exists.</param>
        public int IconIndex(string fileName, bool forceLoadFromDisk)
            => IconIndex(fileName, forceLoadFromDisk, SHGFI.SHGFI_LARGEICON);

        /// <param name="forceLoadFromDisk">Reads the file itself, instead of only when no cached icon exists.</param>
        /// <param name="iconState">Flags for the state of the returned icon.</param>
        public int IconIndex(string fileName, bool forceLoadFromDisk, SHGFI iconState)
        {
            var flags = SHGFI.SHGFI_SYSICONINDEX;
            if (_size == SHIL.SHIL_SMALL)
                flags |= SHGFI.SHGFI_SMALLICON;

            // Without the disk, the file needn't exist, but an icon that isn't cached may come out wrong
            FileFlagsAndAttributes attributes = 0;
            if (!forceLoadFromDisk)
            {
                flags |= SHGFI.SHGFI_USEFILEATTRIBUTES;
                attributes = FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL;
            }

            return GetIconIndex(fileName, attributes, flags, iconState);
        }

        private void Create()
        {
            Release();

            if (SHGetImageList(_size, typeof(IImageList).GUID, out var imageList).Succeeded)
                _imageList = imageList as IImageList;
        }

        private void Release()
        {
            if (_imageList is not null)
                Marshal.ReleaseComObject(_imageList);

            _imageList = null;
        }

        public void Dispose() => Release();
    }
}
