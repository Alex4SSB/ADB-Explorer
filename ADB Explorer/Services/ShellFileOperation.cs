using ADB_Explorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADB_Explorer.Services
{
    public static class ShellFileOperation
    {
        public static void DeleteItems(ADBService.AdbDevice device, IEnumerable<FilePath> items)
        {
            var args = new[] { "-rf" }.Concat(GetEscapedPaths(items)).ToArray();
            var exitCode = ADBService.ExecuteDeviceAdbShellCommand(device.ID, "rm", out string stdout, out string stderr, args);
            
            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }

            return;
        }

        public static IEnumerable<string> GetEscapedPaths(IEnumerable<FilePath> items) => items.Select(item => ADBService.EscapeAdbShellString(item.FullPath)).ToArray();
    }
}
