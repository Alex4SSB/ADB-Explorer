using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{

    /// <summary>
    /// One device staging folder for an entire package icon load. All APK unzips go into the
    /// same root (no per-APK mkdir, no mid-load cleanup). Dispose removes that single folder.
    /// </summary>
    private sealed class ApkIconExtractSession(LogicalDeviceViewModel device) : IDisposable
    {
        private string? _stagingRoot;
        private readonly Dictionary<(string Apk, string Member), byte[]> _cache = new();
        private readonly HashSet<(string Apk, string Member)> _absent = new();
        /// <summary>Members present under staging from any APK (shared tree).</summary>
        private readonly HashSet<string> _onDeviceMembers = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public byte[]? TryGetCached(string apkPath, string member)
        {
            member = ArchivePath.NormalizeInternal(member);
            if (_cache.TryGetValue((apkPath, member), out var bytes))
                return bytes;

            // Shared staging reuses identical drawable paths across density splits, but
            // resources.arsc / AndroidManifest.xml differ per APK — never cross-read those.
            if (IsPerApkExclusiveMember(member))
                return null;

            // Shared staging: another APK may have supplied this path already.
            foreach (var kv in _cache)
            {
                if (string.Equals(kv.Key.Member, member, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }

            return null;
        }

        public bool HasMemberAnywhere(string member)
        {
            member = ArchivePath.NormalizeInternal(member);
            if (_onDeviceMembers.Contains(member))
                return true;
            foreach (var key in _cache.Keys)
            {
                if (string.Equals(key.Member, member, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public IReadOnlyList<string> PresentMembers(string apkPath, IEnumerable<string> candidates)
        {
            List<string> found = [];
            foreach (var raw in candidates)
            {
                var member = ArchivePath.NormalizeInternal(raw);
                // Require cached bytes — _onDeviceMembers alone can list staging leftovers
                // that failed to sync, which made heuristic picks unreadable.
                if (TryGetCached(apkPath, member) is { Length: > 0 })
                    found.Add(member);
            }

            return found;
        }

        private static bool IsPerApkExclusiveMember(string member)
            => member.Equals(RESOURCES, StringComparison.OrdinalIgnoreCase)
               || member.Equals(MANIFEST, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// One <c>unzip -o -d staging</c> of pending suspects from <paramref name="apkPath"/> into
        /// the shared package staging root. Missing members are tolerated.
        /// </summary>
        public async Task EnsureMembersAsync(
            string apkPath,
            IReadOnlyList<string> members,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var pending = members
                .Select(ArchivePath.NormalizeInternal)
                .Where(static m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !_cache.ContainsKey((apkPath, m))
                            && !_absent.Contains((apkPath, m))
                            // resources.arsc is per-APK; do not skip because base already staged one.
                            && (IsPerApkExclusiveMember(m) || !_onDeviceMembers.Contains(m)))
                .ToList();

            if (pending.Count == 0)
                return;

            #if DEBUG
            MarkLoadStep(
                $"batch EnsureMembers ({pending.Count}) from {Path.GetFileName(apkPath)}: {string.Join(',', pending)}");
            #endif

            var stagingRoot = EnsureStagingRoot(cancellationToken);
            await Task.Run(
                () => ArchiveExtract.ExtractZipMembersInto(
                    device.ID,
                    apkPath,
                    stagingRoot,
                    pending,
                    cancellationToken,
                    allowMissingMembers: true),
                cancellationToken).ConfigureAwait(false);

            // One find after unzip — avoid N failed sync pulls for missing members.
            var onDevice = ListRelativeFilesUnder(device.ID, stagingRoot, cancellationToken);
            foreach (var path in onDevice)
                _onDeviceMembers.Add(path);

            foreach (var member in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!onDevice.Contains(member))
                {
                    _absent.Add((apkPath, member));
                    continue;
                }

                var devicePath = FileHelper.ConcatPaths(stagingRoot, member);
                await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                    device, devicePath, cancellationToken).ConfigureAwait(false);
                var bytes = ToByteArray(stream);
                if (bytes is { Length: > 0 })
                    _cache[(apkPath, member)] = bytes;
                else
                    _absent.Add((apkPath, member));
            }
        }

        public async Task PrefetchFromBundleAsync(
            IReadOnlyList<string> apkFiles,
            IReadOnlyList<string> members,
            CancellationToken cancellationToken)
        {
            if (members.Count == 0 || apkFiles.Count == 0)
                return;

            var needed = members
                .Select(ArchivePath.NormalizeInternal)
                .Where(static m => !string.IsNullOrEmpty(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => !HasMemberAnywhere(m))
                .ToList();

            if (needed.Count == 0)
                return;

            foreach (var apk in PreferApksForIconMember(apkFiles))
            {
                await EnsureMembersAsync(apk, needed, cancellationToken).ConfigureAwait(false);
                needed = needed.Where(m => !HasMemberAnywhere(m)).ToList();
                if (needed.Count == 0)
                    break;
            }
        }

        public async Task<byte[]?> TryGetFromBundleAsync(
            IReadOnlyList<string> apkFiles,
            string member,
            CancellationToken cancellationToken)
        {
            member = ArchivePath.NormalizeInternal(member);
            if (string.IsNullOrEmpty(member) || apkFiles.Count == 0)
                return null;

            // TryGetCached falls back across APKs; any path works for the probe key.
            var existing = TryGetCached(apkFiles[0], member);
            if (existing is not null)
                return existing;

            await PrefetchFromBundleAsync(apkFiles, [member], cancellationToken).ConfigureAwait(false);
            return TryGetCached(apkFiles[0], member);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_stagingRoot is not null)
            {
                #if DEBUG
                MarkLoadStep($"ApkIconExtractSession dispose: {_stagingRoot}");
                #endif
                try { ArchiveExtract.CleanupStaging(device.ID, _stagingRoot, CancellationToken.None); }
                catch { /* best-effort */ }
                _stagingRoot = null;
            }

            _cache.Clear();
            _absent.Clear();
            _onDeviceMembers.Clear();
        }

        private string EnsureStagingRoot(CancellationToken cancellationToken)
        {
            if (_stagingRoot is not null)
                return _stagingRoot;

            _stagingRoot = ArchiveExtract.CreateStagingRoot(device.ID, cancellationToken);
            // Single mkdir for the package; unzip -d writes members under this root.
            ShellFileOperation.MakeDirs(device.ID, [_stagingRoot]).GetAwaiter().GetResult();
            #if DEBUG
            MarkLoadStep($"package staging created: {_stagingRoot}");
            #endif
            return _stagingRoot;
        }

        private static HashSet<string> ListRelativeFilesUnder(
            string deviceId,
            string stagingRoot,
            CancellationToken cancellationToken)
        {
            var find = ShellCommands.TranslateCommand("find");
            _ = ADBService.ExecuteDeviceAdbShellCommand(
                deviceId,
                find,
                out var stdout,
                out _,
                cancellationToken,
                ADBService.EscapeAdbShellString(stagingRoot),
                "-type",
                "f");

            var prefix = stagingRoot.TrimEnd('/') + "/";
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
            {
                var path = line.Trim();
                if (path.StartsWith(prefix, StringComparison.Ordinal))
                    result.Add(path[prefix.Length..]);
            }

            #if DEBUG
            MarkLoadStep($"staging find under {stagingRoot}: {result.Count} file(s)");
            #endif
            return result;
        }
    }
}
