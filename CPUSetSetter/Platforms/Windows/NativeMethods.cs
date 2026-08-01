using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;


namespace CPUSetSetter.Platforms
{
    internal static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial SafeProcessHandle OpenProcess(ProcessAccessFlags access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetSystemCpuSetInformation(IntPtr Information, uint BufferLength, ref uint ReturnedLength, SafeProcessHandle Process, uint Flags);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetProcessDefaultCpuSets(SafeProcessHandle Process, uint[]? CpuSetIds, uint CpuSetIdCount);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetProcessDefaultCpuSets(SafeProcessHandle Process, uint[]? CpuSetIds, uint CpuSetIdCount, ref uint RequiredIdCount);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetProcessAffinityMask(SafeProcessHandle hProcess, UIntPtr dwProcessAffinityMask);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetProcessAffinityMask(SafeProcessHandle hProcess, ref UIntPtr lpProcessAffinityMask, ref UIntPtr lpSystemAffinityMask);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool QueryFullProcessImageNameW(SafeProcessHandle hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetForegroundWindow();

        [LibraryImport("user32.dll")]
        public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool UnhookWindowsHookEx(IntPtr hhk);

        [LibraryImport("user32.dll", SetLastError = true)]
        public static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        public static partial IntPtr GetModuleHandleW(string lpModuleName);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetProcessTimes(SafeProcessHandle hProcess, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetThreadTimes(SafeProcessHandle hThread, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial IntPtr OpenThread(ThreadAccessFlags dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwThreadId);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        public static partial SafeFileHandle CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool Thread32First(SafeFileHandle hSnapshot, ref THREADENTRY32 lpte);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool Thread32Next(SafeFileHandle hSnapshot, ref THREADENTRY32 lpte);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetThreadIdealProcessorEx(SafeProcessHandle hThread, out PROCESSOR_NUMBER lpIdealProcessor);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetThreadSelectedCpuSets(SafeProcessHandle hThread, uint[]? CpuSetIds, uint CpuSetIdCount);

        [LibraryImport("user32.dll")]
        public static partial short GetAsyncKeyState(int vKey);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType, IntPtr Buffer, ref uint ReturnedLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetPriorityClass(SafeProcessHandle hProcess, uint dwPriorityClass);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr GetCurrentProcess();

        public const uint TH32CS_SNAPTHREAD = 0x00000004;

        public const uint IDLE_PRIORITY_CLASS = 0x00000040;
        public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
        public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
        public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
        public const uint HIGH_PRIORITY_CLASS = 0x00000080;
        public const uint REALTIME_PRIORITY_CLASS = 0x00000100;

        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const uint TOKEN_QUERY = 0x0008;

        /// <summary>
        /// Enable SeIncreaseBasePriorityPrivilege in the current process token so that
        /// SetPriorityClass can set High or Realtime priority
        /// </summary>
        public static bool EnableIncreaseBasePriorityPrivilege()
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle) || tokenHandle == IntPtr.Zero)
                return false;

            try
            {
                if (!LookupPrivilegeValue(null, "SeIncreaseBasePriorityPrivilege", out LUID luid))
                    return false;

                TOKEN_PRIVILEGES tp = new()
                {
                    PrivilegeCount = 1,
                    Privileges = new LUID_AND_ATTRIBUTES
                    {
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED,
                    }
                };

                return AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
    }

    [Flags]
    public enum ProcessAccessFlags : uint
    {
        PROCESS_SET_INFORMATION = 0x00000200,
        PROCESS_QUERY_LIMITED_INFORMATION = 0x00001000,
        PROCESS_SET_LIMITED_INFORMATION = 0x00002000
    }

    [Flags]
    public enum ThreadAccessFlags : uint
    {
        THREAD_QUERY_LIMITED_INFORMATION = 0x0800,
        THREAD_SET_LIMITED_INFORMATION = 0x0400,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSOR_NUMBER
    {
        public ushort Group;
        public byte Number;
        public byte Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public int tpBasePri;
        public int tpDeltaPri;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GROUP_AFFINITY
    {
        public UIntPtr Mask;
        public ushort Group;
        public fixed ushort Reserved[3];
    }

    [Flags]
    public enum KBDLLHOOKSTRUCTFlags : uint
    {
        LLKHF_EXTENDED = 0x01,
        LLKHF_INJECTED = 0x10,
        LLKHF_ALTDOWN = 0x20,
        LLKHF_UP = 0x80,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public KBDLLHOOKSTRUCTFlags flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public readonly ulong ULong => (((ulong)dwHighDateTime) << 32) + dwLowDateTime;
    }

    public enum LOGICAL_PROCESSOR_RELATIONSHIP : int
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationProcessorDie = 5,
        RelationNumaNodeEx = 6,
        RelationProcessorModule = 7,
        RelationAll = 0xffff // sometimes used as a query value
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX_Header
    {
        public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
        public uint Size; // size of this block in bytes
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PROCESSOR_RELATIONSHIP
    {
        public byte Flags;                          // LTP_PC_SMT flag if SMT is enabled
        public byte EfficiencyClass;                // Efficiency class (0–15)
        public fixed byte Reserved[20];             // Reserved[20]
        public ushort GroupCount;                   // Number of entries in GroupMask[]
        // Followed by GROUP_AFFINITY GroupMask[GroupCount] (variable length)
    }

    public enum PROCESSOR_CACHE_TYPE : int
    {
        CacheUnified = 0,
        CacheInstruction = 1,
        CacheData = 2,
        CacheTrace = 3
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CACHE_RELATIONSHIP
    {
        public byte Level;                          // Cache level (1 = L1, 2 = L2, etc.)
        public byte Associativity;                  // Associativity (0xFF = fully associative)
        public ushort LineSize;                     // Cache line size in bytes
        public uint CacheSize;                      // Total size in bytes
        public PROCESSOR_CACHE_TYPE Type;           // Data / Instruction / Unified
        public fixed byte Reserved[18];             // Reserved[18]
        public ushort GroupCount;                   // Number of entries in GroupMask[]
        // Followed by GROUP_AFFINITY GroupMask[GroupCount] (variable length)
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_CPU_SET_INFORMATION
    {
        public uint Size;
        public CPU_SET_INFORMATION_TYPE Type;
        public uint Id;
        public ushort Group;
        public byte LogicalProcessorIndex;
        public byte CoreIndex;
        public byte LastLevelCacheIndex;
        public byte NumaNodeIndex;
        public byte EfficiencyClass;
        public byte AllFlags;
        public uint Reserved; // union with `byte SchedulingClass`
        public ulong AllocationTag;
    }

    public enum CPU_SET_INFORMATION_TYPE : int
    {
        CpuSetInformation = 0
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct LUID
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
public struct LUID_AND_ATTRIBUTES
{
    public LUID Luid;
    public uint Attributes;
}

[StructLayout(LayoutKind.Sequential)]
public struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public LUID_AND_ATTRIBUTES Privileges;
}
