using RamManager.Helpers;

namespace RamManager.Services;

public enum AlertKind { HighMemory, GrowingApp, Test }

public sealed record Alert(AlertKind Kind, string Title, string Message, string? ProcessName = null);

/// <summary>
/// Decides when to notify about high memory usage or an app whose memory keeps growing (possible leak).
/// Pure logic: the caller feeds it memory snapshots and process samples, and shows the returned alerts.
/// </summary>
public sealed class AlertService(AppSettings settings)
{
    // High memory
    private static readonly TimeSpan HighMemoryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HighMemoryCooldown = TimeSpan.FromMinutes(10);
    private const double HighMemoryHysteresis = 5;

    // Growing apps
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GrowthWindow = TimeSpan.FromMinutes(10);
    private const ulong MinGrowthBytes = 400UL * 1024 * 1024;
    private const double MinGrowthRatio = 0.30;
    private const double MinIncreasingSteps = 0.60;
    private const ulong StepEpsilonBytes = 1024 * 1024;
    private const ulong RealertGrowthBytes = 1024UL * 1024 * 1024;

    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Secure System", "Memory Compression", "Idle",
    };

    private DateTime? _highSince;
    private bool _highNotified;
    private DateTime _lastHighAlert = DateTime.MinValue;

    private readonly Dictionary<(int Pid, long Create), GrowthTrack> _tracks = new();
    private DateTime _lastSampleTime = DateTime.MinValue;
    private List<ProcessSample> _latestSamples = [];

    private bool AnyEnabled => settings.HighMemoryAlerts || settings.GrowingAppAlerts;

    /// <summary>True when the detector needs a fresh process sample (used while the window is hidden).</summary>
    public bool WantsProcessSamples => AnyEnabled && DateTime.UtcNow - _lastSampleTime >= SampleInterval;

    public Alert? CheckMemory(MemorySnapshot memory)
    {
        if (!settings.HighMemoryAlerts)
        {
            _highSince = null;
            _highNotified = false;
            return null;
        }

        var now = DateTime.UtcNow;
        double load = memory.LoadPercent;
        int threshold = settings.HighMemoryThreshold;

        if (_highNotified)
        {
            // Re-arm only after memory has clearly dropped back below the limit.
            if (load < threshold - HighMemoryHysteresis) _highNotified = false;
            _highSince = null;
            return null;
        }

        if (load < threshold)
        {
            _highSince = null;
            return null;
        }

        _highSince ??= now;
        if (now - _highSince < HighMemoryDelay || now - _lastHighAlert < HighMemoryCooldown) return null;

        _highNotified = true;
        _lastHighAlert = now;

        string message = $"{load:0}% of memory is in use, only {Format.Gb(memory.Available)} available.";
        if (TopApp() is { } top) message += $" Biggest app: {top.Name} ({Format.Bytes(top.Bytes)}).";
        return new Alert(AlertKind.HighMemory, "Memory usage is high", message);
    }

    /// <summary>Feeds the latest process list. Internally sampled every 30 seconds.</summary>
    public Alert? RecordProcesses(List<ProcessSample> samples)
    {
        var now = DateTime.UtcNow;
        if (now - _lastSampleTime < SampleInterval) return null;
        _lastSampleTime = now;
        _latestSamples = samples;

        if (!settings.GrowingAppAlerts)
        {
            _tracks.Clear();
            return null;
        }

        int ownPid = Environment.ProcessId;
        var seen = new HashSet<(int, long)>(samples.Count);
        Alert? strongest = null;
        ulong strongestGrowth = 0;

        foreach (var sample in samples)
        {
            if (sample.Pid <= 4 || sample.Pid == ownPid || IgnoredProcesses.Contains(sample.Name)) continue;

            var key = (sample.Pid, sample.CreateTime);
            seen.Add(key);
            if (!_tracks.TryGetValue(key, out var track))
            {
                track = new GrowthTrack();
                _tracks[key] = track;
            }

            track.Add(now, sample.Commit);
            if (track.Evaluate(now) is { } growth && growth > strongestGrowth)
            {
                strongestGrowth = growth;
                track.MarkAlerted();
                string name = sample.Meta.Description ?? sample.Name;
                strongest = new Alert(
                    AlertKind.GrowingApp,
                    $"{name} keeps using more memory",
                    $"{sample.Name} grew by {Format.Bytes(growth)} in the last {GrowthWindow.TotalMinutes:0} minutes " +
                    $"and now uses {Format.Bytes(sample.Commit)}. It may have a memory leak. Restarting it will free the memory.",
                    sample.Name);
            }
        }

        foreach (var key in _tracks.Keys.Where(k => !seen.Contains(k)).ToList())
            _tracks.Remove(key);

        return strongest;
    }

    private (string Name, double Bytes)? TopApp()
    {
        if (_latestSamples.Count == 0) return null;
        var top = _latestSamples
            .Where(s => !IgnoredProcesses.Contains(s.Name))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Meta.Description ?? g.Key, Bytes: g.Sum(s => (double)s.PrivateWorkingSet)))
            .MaxBy(g => g.Bytes);
        return top.Bytes > 0 ? top : null;
    }

    private sealed class GrowthTrack
    {
        private readonly Queue<(DateTime Time, ulong Bytes)> _history = new();
        private ulong _alertedAt;

        public void Add(DateTime time, ulong bytes)
        {
            _history.Enqueue((time, bytes));
            while (_history.Count > 0 && time - _history.Peek().Time > GrowthWindow + SampleInterval)
                _history.Dequeue();
        }

        /// <summary>Returns the growth in bytes if this process looks like it is steadily leaking.</summary>
        public ulong? Evaluate(DateTime now)
        {
            if (_history.Count < 3) return null;
            var first = _history.Peek();
            if (now - first.Time < GrowthWindow - TimeSpan.FromSeconds(15)) return null;

            ulong last = 0, previous = first.Bytes;
            int steps = 0, increasing = 0;
            foreach (var (_, bytes) in _history.Skip(1))
            {
                steps++;
                if (bytes > previous + StepEpsilonBytes) increasing++;
                previous = bytes;
                last = bytes;
            }

            if (last <= first.Bytes) return null;
            ulong growth = last - first.Bytes;

            bool steady = (double)increasing / steps >= MinIncreasingSteps;
            bool large = growth >= MinGrowthBytes && growth >= first.Bytes * MinGrowthRatio;
            bool newSinceLastAlert = _alertedAt == 0 || last >= _alertedAt + RealertGrowthBytes;

            return steady && large && newSinceLastAlert ? growth : null;
        }

        public void MarkAlerted() => _alertedAt = _history.Last().Bytes;
    }
}
