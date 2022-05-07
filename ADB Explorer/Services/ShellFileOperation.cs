using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public static class ShellFileOperation
    {
        public static void DeleteItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, ObservableList<FileClass> fileList, Dispatcher dispatcher)
        {
            foreach (var item in items)
            {
                Data.fileOperationQueue.AddOperation(new FileDeleteOperation(dispatcher, device, item, fileList));
            }
        }

        /// <summary>
        /// Moves item(s) or renames an item
        /// </summary>
        /// <param name="device"></param>
        /// <param name="items">Original FilePath items</param>
        /// <param name="targetPath">New parent path for single / multiple items, or full path for single file (rename)</param>
        /// <exception cref="Exception"></exception>
        public static void MoveItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, string targetPath, string currentPath, ObservableList<FileClass> fileList, Dispatcher dispatcher)
        {
            foreach (var item in items)
            {
                Data.fileOperationQueue.AddOperation(new FileMoveOperation(dispatcher, device, item, targetPath, currentPath, fileList));
            }
        }

        public static void RenameItem(ADBService.AdbDevice device, FilePath item, string targetPath)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                                   "mv",
                                                                                   out string stdout,
                                                                                   out string stderr,
                                                                                   new[] { ADBService.EscapeAdbShellString(item.FullPath), ADBService.EscapeAdbShellString(targetPath) });

            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }

            return;
        }

        public static void MakeDir(ADBService.AdbDevice device, string fullPath)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                   "mkdir",
                                                                   out string stdout,
                                                                   out string stderr,
                                                                   new[] { "-p", ADBService.EscapeAdbShellString(fullPath) });

            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }
        }

        public static void MakeFile(ADBService.AdbDevice device, string fullPath)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                   "touch",
                                                                   out string stdout,
                                                                   out string stderr,
                                                                   ADBService.EscapeAdbShellString(fullPath));

            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }
        }

        public static void WriteLine(ADBService.AdbDevice device, string fullPath, string newLine)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                   "echo",
                                                                   out string stdout,
                                                                   out string stderr,
                                                                   new[] { newLine, ">>", ADBService.EscapeAdbShellString(fullPath) });

            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }
        }
    }
}
