using static RamManager.Native.NativeMethods;

namespace RamManager.Services;

public readonly record struct MemorySnapshot(
    ulong Total,
    ulong Available,
    ulong CommitTotal,
    ulong CommitLimit,
    ulong Free,
    ulong Standby,
    ulong Modified,
    bool HasBreakdown)
{
    public ulong Used => Total > Available ? Total - Available : 0;

    /// <summary>Memory actively used by processes, drivers and the OS.</summary>
    public ulong InUse
    {
        get
        {
            if (!HasBreakdown) return Used;
            ulong other = Free + Standby + Modified;
            return Total > other ? Total - other : 0;
        }
    }

    public double LoadPercent => Total == 0 ? 0 : Used * 100.0 / Total;

    public double CommitPercent => CommitLimit == 0 ? 0 : CommitTotal * 100.0 / CommitLimit;
}

public static unsafe class MemoryService
{
    public static MemorySnapshot GetSnapshot()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)sizeof(MEMORYSTATUSEX) };
        GlobalMemoryStatusEx(ref status);

        var perf = new PERFORMANCE_INFORMATION { cb = (uint)sizeof(PERFORMANCE_INFORMATION) };
        ulong pageSize = 4096, commitTotal = 0, commitLimit = 0;
        if (GetPerformanceInfo(ref perf, perf.cb))
        {
            pageSize = perf.PageSize;
            commitTotal = perf.CommitTotal * pageSize;
            commitLimit = perf.CommitLimit * pageSize;
        }

        // Standby / modified / free page lists (needs SeProfileSingleProcessPrivilege).
        SYSTEM_MEMORY_LIST_INFORMATION lists;
        int result = NtQuerySystemInformation(SystemMemoryListInformation, &lists, (uint)sizeof(SYSTEM_MEMORY_LIST_INFORMATION), out _);

        ulong free = 0, standby = 0, modified = 0;
        bool hasBreakdown = result >= 0;
        if (hasBreakdown)
        {
            free = (ulong)(lists.ZeroPageCount + lists.FreePageCount) * pageSize;
            modified = (ulong)(lists.ModifiedPageCount + lists.ModifiedNoWritePageCount) * pageSize;
            foreach (nuint count in lists.PageCountByPriority)
                standby += (ulong)count * pageSize;
        }

        return new MemorySnapshot(
            status.ullTotalPhys,
            status.ullAvailPhys,
            commitTotal,
            commitLimit,
            free,
            standby,
            modified,
            hasBreakdown);
    }
}
