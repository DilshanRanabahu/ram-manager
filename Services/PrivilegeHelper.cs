using System.Security.Principal;
using static RamManager.Native.NativeMethods;

namespace RamManager.Services;

public static class PrivilegeHelper
{
    public static bool IsAdministrator { get; } = CheckAdministrator();

    private static bool CheckAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Enables the token privileges needed to query and purge memory lists.</summary>
    public static void EnableMemoryPrivileges()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out nint token)) return;
        try
        {
            foreach (var name in new[] { "SeProfileSingleProcessPrivilege", "SeIncreaseQuotaPrivilege", "SeDebugPrivilege" })
            {
                if (!LookupPrivilegeValue(null, name, out var luid)) continue;
                var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                AdjustTokenPrivileges(token, false, ref privileges, 0, 0, 0);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
