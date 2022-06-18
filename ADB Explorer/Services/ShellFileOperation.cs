using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ADB_Explorer.Services
{
    public static class ShellFileOperation
    {
        public static void SilentDelete(ADBService.AdbDevice device, FilePath item) => SilentDelete(device, new List<FilePath>() { item });

        public static void SilentDelete(ADBService.AdbDevice device, string fullPath)
        {
            ADBService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out _, out _, "-rf", ADBService.EscapeAdbShellString(fullPath));
        }

        public static void SilentDelete(ADBService.AdbDevice device, IEnumerable<FilePath> items)
        {
            var args = new[] { "-rf" }.Concat(GetEscapedPaths(items)).ToArray();
            ADBService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out _, out _, args);
        }

        public static void DeleteItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, ObservableList<FileClass> fileList, Dispatcher dispatcher)
        {
            foreach (var item in items)
            {
                Data.fileOperationQueue.AddOperation(new FileDeleteOperation(dispatcher, device, item, fileList));
            }
        }

        public static bool MoveItem(ADBService.AdbDevice device, FilePath item, string targetPath) => MoveItem(device, item.FullPath, targetPath);

        public static bool MoveItem(ADBService.AdbDevice device, string fullPath, string targetPath, bool throwOnError = true)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "mv", out string stdout, out string stderr, new[] { ADBService.EscapeAdbShellString(fullPath), ADBService.EscapeAdbShellString(targetPath) });

            if (exitCode != 0 && throwOnError)
            {
                throw new Exception(stderr);
            }

            return exitCode == 0;
        }

        public static void MoveItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, string targetPath, string currentPath, ObservableList<FileClass> fileList, Dispatcher dispatcher, LogicalDevice logical, bool isCopy = false)
        {
            if (targetPath == AdbExplorerConst.RECYCLE_PATH) // Recycle
            {
                var mdTask = Task.Run(() => MakeDir(device, AdbExplorerConst.RECYCLE_PATH));
                mdTask.ContinueWith((t) =>
                {
                    dispatcher.Invoke(() =>
                    {
                        foreach (var item in items)
                        {
                            Data.fileOperationQueue.AddOperation(new FileMoveOperation(dispatcher, device, item, targetPath, item.FullName, currentPath, fileList, logical));
                        }
                    });
                });
            }
            else if (targetPath is null && currentPath == AdbExplorerConst.RECYCLE_PATH) // Restore
            {
                foreach (var item in items)
                {
                    if (AdbExplorerConst.RECYCLE_INDEX_PATHS.Contains(item.FullPath))
                        continue;

                    Data.fileOperationQueue.AddOperation(new FileMoveOperation(dispatcher, device, item, ((FileClass)item).TrashIndex.ParentPath, item.FullName, currentPath, fileList, logical));
                }
            }
            else
            {
                foreach (var item in items)
                {
                    var targetName = item.FullName;
                    if (item.ParentPath == targetPath)
                    {
                        targetName = $"{((FileClass)item).NoExtName}{FileClass.ExistingIndexes(fileList, ((FileClass)item).NoExtName, isCopy)}{((FileClass)item).Extension}";
                    }
                    Data.fileOperationQueue.AddOperation(new FileMoveOperation(dispatcher, device, item, targetPath, targetName, currentPath, fileList, logical, isCopy));
                }
            }
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

        public static string ReadAllText(ADBService.AdbDevice device, string fullPath)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                    "cat",
                                                                    out string stdout,
                                                                    out string stderr,
                                                                    ADBService.EscapeAdbShellString(fullPath));

            if (exitCode != 0)
                throw new Exception(stderr);

            return stdout;
        }

        public static IEnumerable<string> GetEscapedPaths(IEnumerable<FilePath> items) => items.Select(item => ADBService.EscapeAdbShellString(item.FullPath)).ToArray();
    }
}
