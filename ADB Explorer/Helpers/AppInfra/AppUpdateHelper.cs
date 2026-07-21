using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Helpers;

/// <summary>
/// Portable self-update: extract <c>update.7z</c> through the Windows shell namespace
/// (Explorer <c>ArchiveFolder</c>, 7-Zip, NanaZip, etc.), verify the staged executable's
/// Authenticode certificate matches this installation, then schedule a helper that waits for
/// this process to exit, copies files into the app directory, and optionally relaunches.
/// </summary>
public static class AppUpdateHelper
{
    private static string? _stagingRoot;
    private static string? _stagingContentDir;
    private static bool _swapScheduled;

    private static readonly ShellFileOperations.OperationFlags SilentCopyFlags =
        ShellFileOperations.OperationFlags.Silent
        | ShellFileOperations.OperationFlags.NoConfirmation
        | ShellFileOperations.OperationFlags.NoConfirmMkDir
        | ShellFileOperations.OperationFlags.NoErrorUI;

    public static string UpdateArchivePath =>
        Path.Combine(AppContext.BaseDirectory, AdbExplorerConst.UPDATE_ARCHIVE_FILE);

    /// <summary>Localized reason for the last failed <see cref="ApplyPendingUpdate"/> call, if any.</summary>
    public static string? LastFailureReason { get; private set; }

    /// <summary>
    /// Extract the downloaded archive via the shell (if needed) and either shut down for an
    /// immediate swap+relaunch, or mark the update to be applied when the process exits.
    /// </summary>
    public static bool ApplyPendingUpdate(bool restartAfter)
    {
        LastFailureReason = null;

        if (!PrepareStaging())
        {
            LastFailureReason ??= Strings.Resources.S_UPDATE_APPLY_FAILED;
            return false;
        }

        if (restartAfter)
        {
            if (!ScheduleSwap(restartAfter: true))
            {
                LastFailureReason = Strings.Resources.S_UPDATE_APPLY_FAILED;
                return false;
            }

            App.Services.GetService<SettingsService>()?.SaveSettingsFile();
            Application.Current.Shutdown();
            return true;
        }

        Data.RuntimeSettings.ApplyUpdateOnExit = true;
        return true;
    }

    /// <summary>
    /// Called from <see cref="App"/> exit when the user chose "Update on Close".
    /// </summary>
    public static void ScheduleSwapOnExitIfNeeded()
    {
        if (!Data.RuntimeSettings.ApplyUpdateOnExit || _swapScheduled)
            return;

        if (!PrepareStaging())
            return;

        ScheduleSwap(restartAfter: false);
    }

    private static bool PrepareStaging()
    {
        if (_stagingContentDir is not null && Directory.Exists(_stagingContentDir))
            return true;

        var archivePath = UpdateArchivePath;
        if (!File.Exists(archivePath))
            return false;

        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "AdbExplorer",
            "update_staging",
            Environment.ProcessId.ToString());

        try
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);

            Directory.CreateDirectory(stagingRoot);

            using var archiveItem = ShellItem.Open(archivePath);
            // .7z (and other archives) are shell folders when a namespace handler is registered
            // for the extension — e.g. Explorer ArchiveFolder, 7-Zip, NanaZip.
            if (!archiveItem.IsFolder)
                return false;

            using var archiveFolder = new ShellFolder(archiveItem);
            using var destFolder = new ShellFolder(stagingRoot);

            var children = archiveFolder.ToArray();
            if (children.Length == 0)
                return false;

            try
            {
                ShellFileOperations.Copy(children, destFolder, SilentCopyFlags);
            }
            finally
            {
                foreach (var child in children)
                    child.Dispose();
            }

            var contentRoot = ResolveContentRoot(stagingRoot);
            if (!Directory.Exists(contentRoot)
                || !Directory.EnumerateFileSystemEntries(contentRoot).Any())
            {
                return false;
            }

            if (!VerifyStagedExecutable(contentRoot))
            {
                LastFailureReason = Strings.Resources.S_UPDATE_SIGNATURE_INVALID;
                try { Directory.Delete(stagingRoot, recursive: true); } catch { }
                try { File.Delete(archivePath); } catch { }
                return false;
            }

            _stagingRoot = stagingRoot;
            _stagingContentDir = contentRoot;
            return true;
        }
        catch
        {
            try
            {
                if (Directory.Exists(stagingRoot))
                    Directory.Delete(stagingRoot, recursive: true);
            }
            catch
            { }

            _stagingRoot = null;
            _stagingContentDir = null;
            return false;
        }
    }

    private static bool VerifyStagedExecutable(string contentRoot)
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentPath))
            return false;

        var stagedPath = Path.Combine(contentRoot, Path.GetFileName(currentPath));
        if (!File.Exists(stagedPath))
            return false;

        return Security.VerifyAuthenticodeMatches(currentPath, stagedPath);
    }

    /// <summary>
    /// Release archives wrap files in a single framework folder (e.g. net10.0-windows...).
    /// Use that folder as the copy source so files land directly in the app directory.
    /// </summary>
    private static string ResolveContentRoot(string stagingRoot)
    {
        var entries = Directory.GetFileSystemEntries(stagingRoot);
        if (entries.Length == 1 && Directory.Exists(entries[0]))
            return entries[0];

        return stagingRoot;
    }

    private static bool ScheduleSwap(bool restartAfter)
    {
        if (_stagingContentDir is null || _stagingRoot is null || !Directory.Exists(_stagingContentDir))
            return false;

        if (_swapScheduled)
            return true;

        var appDir = Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"adb-explorer-update-{Guid.NewGuid():N}.ps1");
        var restartFlag = restartAfter ? "1" : "0";

        var script = $$"""
            $ErrorActionPreference = 'Continue'
            $waitPid = {{Environment.ProcessId}}
            $source = {{PsLiteral(_stagingContentDir)}}
            $dest = {{PsLiteral(appDir)}}
            $archive = {{PsLiteral(UpdateArchivePath)}}
            $stagingRoot = {{PsLiteral(_stagingRoot)}}
            $exe = {{PsLiteral(exePath)}}
            $restart = {{PsLiteral(restartFlag)}}
            $script = {{PsLiteral(scriptPath)}}

            while (Get-Process -Id $waitPid -ErrorAction SilentlyContinue) {
                Start-Sleep -Milliseconds 400
            }
            Start-Sleep -Milliseconds 800

            & robocopy $source $dest /E /IS /IT /R:3 /W:1 /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { exit $LASTEXITCODE }

            Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue

            if ($restart -eq '1') {
                Start-Process -FilePath $exe -WorkingDirectory $dest
            }

            Remove-Item -LiteralPath $script -Force -ErrorAction SilentlyContinue
            """;

        try
        {
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            _swapScheduled = true;
            return true;
        }
        catch
        {
            try { File.Delete(scriptPath); } catch { }
            return false;
        }
    }

    private static string PsLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
