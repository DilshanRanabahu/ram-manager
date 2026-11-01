using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static RamManager.Native.NativeMethods;

namespace RamManager.Services;

public sealed record ProcessMetadata(string? Path, string? Description, ImageSource? Icon)
{
    public static readonly ProcessMetadata Empty = new(null, null, null);
}

public sealed class ProcessSample
{
    public required int Pid { get; init; }
    public required long CreateTime { get; init; }
    public required string Name { get; init; }
    public required ulong PrivateWorkingSet { get; init; }
    public required ulong WorkingSet { get; init; }
    public required ulong Commit { get; init; }
    public required double Cpu { get; init; }
    public required uint Threads { get; init; }
    public required ProcessMetadata Meta { get; init; }
}

/// <summary>
/// Reads all processes with a single NtQuerySystemInformation call (no per-process handles),
/// and caches path / description / icon for each process.
/// </summary>
public sealed unsafe class ProcessService : IDisposable
{
    private byte* _buffer;
    private int _bufferSize;

    private Dictionary<(int Pid, long Create), long> _previousCpu = new();
    private long _previousTimestamp;

    private readonly Dictionary<(int Pid, long Create), ProcessMetadata> _metadata = new();
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _descriptions = new(StringComparer.OrdinalIgnoreCase);

    public List<ProcessSample> Collect()
    {
        QuerySnapshot();

        long now = Stopwatch.GetTimestamp();
        double elapsed100ns = _previousTimestamp == 0 ? 0 : (now - _previousTimestamp) * 1e7 / Stopwatch.Frequency;
        int cpuCount = Environment.ProcessorCount;

        var samples = new List<ProcessSample>(_previousCpu.Count + 32);
        var cpuTimes = new Dictionary<(int, long), long>(_previousCpu.Count + 32);

        byte* entry = _buffer;
        while (true)
        {
            var info = (SYSTEM_PROCESS_INFORMATION*)entry;
            int pid = (int)info->UniqueProcessId;

            if (pid != 0) // skip "System Idle Process"
            {
                var key = (pid, info->CreateTime);
                string name = info->ImageName.Buffer != 0
                    ? new string((char*)info->ImageName.Buffer, 0, info->ImageName.Length / 2)
                    : pid == 4 ? "System" : $"PID {pid}";

                long cpuTime = info->UserTime + info->KernelTime;
                double cpu = 0;
                if (elapsed100ns > 0 && _previousCpu.TryGetValue(key, out long previous))
                    cpu = Math.Clamp((cpuTime - previous) / (elapsed100ns * cpuCount) * 100, 0, 100);
                cpuTimes[key] = cpuTime;

                if (!_metadata.TryGetValue(key, out var meta))
                {
                    meta = LoadMetadata(pid);
                    _metadata[key] = meta;
                }

                samples.Add(new ProcessSample
                {
                    Pid = pid,
                    CreateTime = info->CreateTime,
                    Name = name,
                    PrivateWorkingSet = (ulong)info->WorkingSetPrivateSize,
                    WorkingSet = info->WorkingSetSize,
                    Commit = info->PagefileUsage,
                    Cpu = cpu,
                    Threads = info->NumberOfThreads,
                    Meta = meta,
                });
            }

            if (info->NextEntryOffset == 0) break;
            entry += info->NextEntryOffset;
        }

        // Forget processes that have exited.
        if (_metadata.Count > cpuTimes.Count)
        {
            foreach (var key in _metadata.Keys.Where(k => !cpuTimes.ContainsKey(k)).ToList())
                _metadata.Remove(key);
        }

        _previousCpu = cpuTimes;
        _previousTimestamp = now;
        return samples;
    }

    private void QuerySnapshot()
    {
        if (_buffer == null) Allocate(1 << 20);

        while (true)
        {
            int status = NtQuerySystemInformation(SystemProcessInformation, _buffer, (uint)_bufferSize, out uint needed);
            if (status == STATUS_INFO_LENGTH_MISMATCH || status == STATUS_BUFFER_TOO_SMALL)
            {
                Allocate(Math.Max(_bufferSize * 2, (int)needed + 64 * 1024));
                continue;
            }
            if (status < 0) throw new Win32Exception(RtlNtStatusToDosError(status));
            return;
        }
    }

    private void Allocate(int size)
    {
        if (_buffer != null) NativeMemory.Free(_buffer);
        _buffer = (byte*)NativeMemory.Alloc((nuint)size);
        _bufferSize = size;
    }

    private ProcessMetadata LoadMetadata(int pid)
    {
        string? path = GetProcessPath(pid);
        if (path is null) return ProcessMetadata.Empty;

        if (!_descriptions.TryGetValue(path, out string? description))
        {
            try
            {
                description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
                if (string.IsNullOrEmpty(description)) description = null;
            }
            catch
            {
                description = null;
            }
            _descriptions[path] = description;
        }

        if (!_icons.TryGetValue(path, out var icon))
        {
            icon = LoadIcon(path);
            _icons[path] = icon;
        }

        return new ProcessMetadata(path, description, icon);
    }

    private static string? GetProcessPath(int pid)
    {
        if (pid <= 4) return null;

        nint handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return null;
        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static ImageSource? LoadIcon(string path)
    {
        nint large = 0, small = 0;
        try
        {
            uint count = ExtractIconEx(path, 0, &large, &small, 1);
            if (count == 0 || count == uint.MaxValue || large == 0) return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var bitmap = new WriteableBitmap(source); // copy so it can be frozen and shared across threads
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large != 0) DestroyIcon(large);
            if (small != 0) DestroyIcon(small);
        }
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
    }
}
