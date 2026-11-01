using System.ComponentModel;
using System.Runtime.InteropServices;
using static RamManager.Native.NativeMethods;

namespace RamManager.Services;

public enum CleanAction
{
    TrimWorkingSets,
    FlushModified,
    PurgeStandby,
    CombinePages,
}

public static unsafe class MemoryCleaner
{
    public static string DisplayName(this CleanAction action) => action switch
    {
        CleanAction.TrimWorkingSets => "Trim working sets",
        CleanAction.FlushModified => "Flush modified list",
        CleanAction.PurgeStandby => "Purge standby list",
        CleanAction.CombinePages => "Combine memory pages",
        _ => action.ToString(),
    };

    /// <summary>Runs a system-wide memory operation. Throws <see cref="Win32Exception"/> on failure.</summary>
    public static void Run(CleanAction action)
    {
        switch (action)
        {
            case CleanAction.TrimWorkingSets:
                Command(MemoryEmptyWorkingSets);
                break;
            case CleanAction.FlushModified:
                Command(MemoryFlushModifiedList);
                break;
            case CleanAction.PurgeStandby:
                Command(MemoryPurgeStandbyList);
                break;
            case CleanAction.CombinePages:
                var info = new MEMORY_COMBINE_INFORMATION_EX();
                Check(NtSetSystemInformation(SystemCombinePhysicalMemoryInformation, &info, (uint)sizeof(MEMORY_COMBINE_INFORMATION_EX)));
                break;
        }
    }

    /// <summary>Trims the working set of a single process.</summary>
    public static void TrimProcess(int pid)
    {
        nint handle = OpenProcess(PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!EmptyWorkingSet(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>Releases this app's own unused memory (used while hidden in the tray).</summary>
    public static void TrimSelf() => EmptyWorkingSet(GetCurrentProcess());

    private static void Command(int command) =>
        Check(NtSetSystemInformation(SystemMemoryListInformation, &command, sizeof(int)));

    private static void Check(int status)
    {
        if (status < 0) throw new Win32Exception(RtlNtStatusToDosError(status));
    }
}
