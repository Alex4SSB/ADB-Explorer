namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    private static readonly AsyncLocal<ApkLoadTiming?> CurrentTiming = new();

    /// <summary>
    /// Records a step on the active force-reload timing log (no-op when none is active).
    /// Call from ADB / archive / sync helpers so every device round-trip is measured.
    /// </summary>
    [Conditional("DEBUG")]
    public static void MarkLoadStep(string step) => CurrentTiming.Value?.Mark(step);

    /// <summary>Records a step on <paramref name="timing"/>; compiled out of release builds.</summary>
    [Conditional("DEBUG")]
    private static void Mark(ApkLoadTiming? timing, string step) => timing?.Mark(step);

    /// <summary>Step log for DEBUG force-reload; thread-safe for parallel sub-tasks.</summary>
    private sealed class ApkLoadTiming(string packageName, string apkPath)
    {
        private readonly object _lock = new();
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly DateTime _startedAt = DateTime.Now;
        private readonly List<(long AtMs, long StepMs, string Step)> _steps = [];
        private long _lastMark;

        public void Mark(string step)
        {
            lock (_lock)
            {
                var at = _sw.ElapsedMilliseconds;
                var stepMs = at - _lastMark;
                _lastMark = at;
                _steps.Add((at, stepMs, step));
            }
        }

        public string Format(bool success, string? note = null)
        {
            lock (_lock)
            {
                _sw.Stop();
                var sb = new StringBuilder();
                sb.AppendLine($"APK icon reload — {packageName}");
                sb.AppendLine($"Path: {apkPath}");
                sb.AppendLine($"Started: {_startedAt:yyyy-MM-dd HH:mm:ss.fff}");
                var resultText = success ? "ok" : "failed";
                if (!string.IsNullOrEmpty(note))
                    resultText += $" ({note})";
                sb.AppendLine($"Result: {resultText}");
                sb.AppendLine("Steps (step ms / cumulative ms):");
                foreach (var (atMs, stepMs, step) in _steps)
                    sb.AppendLine($"  +{stepMs,7} ms  (t={atMs,7})  {step}");
                sb.AppendLine($"Total: {_sw.ElapsedMilliseconds} ms");
                return sb.ToString();
            }
        }
    }
}
