using ADB_Explorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Explorer.Services
{
    public static class ShellFileOperation
    {
        /// <summary>
        /// Moves item(s) or renames an item
        /// </summary>
        /// <param name="device"></param>
        /// <param name="items">Original FilePath items</param>
        /// <param name="targetPath">New parent path for single / multiple items, or full path for single file (rename)</param>
        /// <exception cref="Exception"></exception>
        public static void MoveItems(ADBService.AdbDevice device, IEnumerable<FilePath> items, string targetPath)
        {
            var escapedTarget = ADBService.EscapeAdbShellString(targetPath);

            // rename (one file)
            if (items.Count() == 1 && targetPath[(targetPath.LastIndexOf('/') + 1)..] != items.First().FullName)
            {
                var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                       "mv",
                                                                       out string stdout,
                                                                       out string stderr,
                                                                       new[] { ADBService.EscapeAdbShellString(items.First().FullPath), escapedTarget });

                if (exitCode != 0)
                {
                    throw new Exception(stderr);
                }

                return;
            }
            else
            {
                foreach (var item in items)
                {
                    var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                       "mv",
                                                                       out string stdout,
                                                                       out string stderr,
                                                                       new[] { ADBService.EscapeAdbShellString(item.FullPath),
                                                                           ADBService.EscapeAdbShellString($"{targetPath}{(targetPath.EndsWith('/') ? "" : "/")}{item.FullName}") });

                    if (exitCode != 0)
                    {
                        throw new Exception(stderr);
                    }
                }
            }
            
        }

        public static void MakeDir(ADBService.AdbDevice device, string fullPath)
        {
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID,
                                                                   "mkdir",
                                                                   out string stdout,
                                                                   out string stderr,
                                                                   ADBService.EscapeAdbShellString(fullPath));

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
    }
}
