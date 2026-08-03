using CPUSetSetter.Config.Models;
using CPUSetSetter.UI.Tabs.Processes;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace CPUSetSetter.Platforms.Windows
{
    public class ProcessHandlerWindows : IProcessHandler
    {
        private readonly static Dictionary<int, uint> _logicalProcessorToSetId;
        private readonly static Dictionary<uint, int> _setIdToLogicalProcessor;
        private readonly Queue<CpuTimeTimestamp> _cpuTimeMovingAverageBuffer = new();
        private readonly Dictionary<uint, ThreadCpuSample> _threadCpuSamples = [];
        private DateTime _lastPerCoreSampleTime;
        private bool _hasPreviousPerCoreSample;

        private readonly string _executableName;
        private readonly uint _pid;
        private readonly SafeProcessHandle _queryLimitedInfoHandle;
        private SafeProcessHandle? _setLimitedInfoHandle;
        private SafeProcessHandle? _setInfoHandle;
        private MaskApplyType _previousMaskType = MaskApplyType.NoMask;

        /// <summary>
        /// The query handle used for reading process information (CPU usage, current mask)
        /// </summary>
        internal SafeProcessHandle QueryHandle => _queryLimitedInfoHandle;

        /// <summary>
        /// Logical processors of the currently applied CPU Set, used to correct the per-core display:
        /// a CPU Set confined thread's reported ideal processor is not updated
        /// </summary>
        private bool[]? _activeCpuSetMask;

        static ProcessHandlerWindows()
        {
            _logicalProcessorToSetId = GetCpuSetIdPerLogicalProcessor();
            _setIdToLogicalProcessor = _logicalProcessorToSetId.ToDictionary(x => x.Value, x => x.Key);
        }

        public ProcessHandlerWindows(string executableName, uint pid, SafeProcessHandle queryHandle)
        {
            _executableName = executableName;
            _pid = pid;
            _queryLimitedInfoHandle = queryHandle;
        }

        public double GetAverageCpuUsage()
        {
            GetCpuUsage(out _, out double averageUsage);
            return averageUsage;
        }

        public void GetCpuUsage(out double currentUsage, out double averageUsage)
        {
            currentUsage = -1;
            averageUsage = -1;
            if (_queryLimitedInfoHandle.IsInvalid)
            {
                return;
            }

            DateTime now = DateTime.Now;
            // Remove datapoints older than 30 seconds from the moving average buffer
            while (_cpuTimeMovingAverageBuffer.Count > 0)
            {
                TimeSpan datapointAge = now - _cpuTimeMovingAverageBuffer.Peek().Timestamp;
                if (datapointAge.TotalSeconds > 30)
                {
                    _cpuTimeMovingAverageBuffer.Dequeue();
                }
                else
                {
                    break;
                }
            }

            // Get the current total CPU time of the process
            bool success = NativeMethods.GetProcessTimes(_queryLimitedInfoHandle, out FILETIME _, out FILETIME _, out FILETIME kernelTime, out FILETIME userTime);
            if (!success)
            {
                return;
            }
            TimeSpan totalCpuTime = TimeSpan.FromTicks((long)(kernelTime.ULong + userTime.ULong));

            // Current usage: the CPU time delta since the previous datapoint (roughly the last second)
            CpuTimeTimestamp? previousDatapoint = _cpuTimeMovingAverageBuffer.Count > 0 ? _cpuTimeMovingAverageBuffer.Last() : null;
            _cpuTimeMovingAverageBuffer.Enqueue(new() { Timestamp = now, TotalCpuTime = totalCpuTime });

            if (previousDatapoint is { } previous)
            {
                TimeSpan deltaTime = now - previous.Timestamp;
                TimeSpan deltaCpuTime = totalCpuTime - previous.TotalCpuTime;
                if (deltaTime.Ticks > 0 && deltaCpuTime.Ticks > 0)
                    currentUsage = (double)deltaCpuTime.Ticks / deltaTime.Ticks / CpuInfo.LogicalProcessorCount;
                else
                    currentUsage = 0;
            }
            else
            {
                currentUsage = 0;
            }

            // Average usage: take the CPU time from now and (up to) 30 seconds ago
            CpuTimeTimestamp startDatapoint = _cpuTimeMovingAverageBuffer.Peek();
            TimeSpan avgDeltaTime = now - startDatapoint.Timestamp;
            TimeSpan avgDeltaCpuTime = totalCpuTime - startDatapoint.TotalCpuTime;

            if (avgDeltaCpuTime.Ticks == 0)
                averageUsage = 0;
            else
                averageUsage = (double)avgDeltaCpuTime.Ticks / avgDeltaTime.Ticks / CpuInfo.LogicalProcessorCount;
        }

        /// <summary>
        /// Approximate the process' CPU usage per logical processor by sampling each thread's current
        /// processor and CPU time, attributing a thread's time since the previous sample to its current processor
        /// </summary>
        public double[]? GetPerCoreCpuUsage()
        {
            if (_queryLimitedInfoHandle.IsInvalid)
            {
                return null;
            }

            DateTime now = DateTime.Now;
            double[] result = new double[CpuInfo.LogicalProcessorCount];

            try
            {
                using SafeFileHandle snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPTHREAD, _pid);
                if (snapshot.IsInvalid)
                {
                    return null;
                }

                THREADENTRY32 threadEntry = new() { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
                if (!NativeMethods.Thread32First(snapshot, ref threadEntry))
                {
                    return null;
                }

                Dictionary<uint, ThreadCpuSample> currentSamples = [];
                do
                {
                    if (threadEntry.th32OwnerProcessID != _pid)
                        continue;

                    IntPtr rawThreadHandle = NativeMethods.OpenThread(ThreadAccessFlags.THREAD_QUERY_LIMITED_INFORMATION, false, threadEntry.th32ThreadID);
                    if (rawThreadHandle == IntPtr.Zero)
                        continue;

                    using SafeProcessHandle threadHandle = new(rawThreadHandle, true);

                    // Get the processor this thread prefers to run on, which for busy threads tracks the processor it is currently running on
                    if (!NativeMethods.GetThreadIdealProcessorEx(threadHandle, out PROCESSOR_NUMBER processorNumber))
                        continue;

                    if (!NativeMethods.GetThreadTimes(threadHandle, out FILETIME _, out FILETIME _, out FILETIME kernelTime, out FILETIME userTime))
                        continue;

                    int processorIndex = processorNumber.Group != 0 || processorNumber.Number >= CpuInfo.LogicalProcessorCount ? 0 : processorNumber.Number;

                    currentSamples[threadEntry.th32ThreadID] = new()
                    {
                        CpuTicks = (long)(kernelTime.ULong + userTime.ULong),
                        ProcessorIndex = processorIndex,
                    };
                }
                while (NativeMethods.Thread32Next(snapshot, ref threadEntry));

                // Attribute the CPU time of each thread since the previous sample to the processor it is currently on
                if (_hasPreviousPerCoreSample)
                {
                    double elapsedSeconds = (now - _lastPerCoreSampleTime).TotalSeconds;
                    if (elapsedSeconds > 0)
                    {
                        double misplacedUsage = 0;
                        foreach ((uint threadId, ThreadCpuSample currentSample) in currentSamples)
                        {
                            if (!_threadCpuSamples.TryGetValue(threadId, out ThreadCpuSample? previousSample))
                                continue;

                            double coreFraction = (currentSample.CpuTicks - previousSample.CpuTicks) / (double)TimeSpan.TicksPerSecond / elapsedSeconds;
                            if (coreFraction <= 0)
                                continue;

                            // A thread confined to a CPU Set may still report a stale ideal processor outside the
                            // CPU Set; attribute such usage to the CPU Set's cores instead
                            if (_activeCpuSetMask is not null && !_activeCpuSetMask[currentSample.ProcessorIndex])
                                misplacedUsage += coreFraction;
                            else
                                result[currentSample.ProcessorIndex] = Math.Clamp(result[currentSample.ProcessorIndex] + coreFraction, 0, 1);
                        }

                        if (misplacedUsage > 0 && _activeCpuSetMask is not null)
                        {
                            int cpuSetCount = 0;
                            for (int i = 0; i < _activeCpuSetMask.Length; ++i)
                                if (_activeCpuSetMask[i])
                                    ++cpuSetCount;

                            if (cpuSetCount > 0)
                            {
                                double perCpuSetCore = misplacedUsage / cpuSetCount;
                                for (int i = 0; i < _activeCpuSetMask.Length; ++i)
                                    if (_activeCpuSetMask[i])
                                        result[i] = Math.Clamp(result[i] + perCpuSetCore, 0, 1);
                            }
                        }
                    }
                }

                _threadCpuSamples.Clear();
                foreach ((uint threadId, ThreadCpuSample sample) in currentSamples)
                {
                    _threadCpuSamples[threadId] = sample;
                }
                _hasPreviousPerCoreSample = true;
                _lastPerCoreSampleTime = now;
                return result;
            }
            catch (Exception)
            {
                // Threads may disappear or refuse access in between calls (e.g. anti-cheat)
                return null;
            }
        }

        public bool ApplyMask(LogicalProcessorMask mask)
        {
            bool result;

            switch (mask.MaskType)
            {
                case MaskApplyType.NoMask:
                    // Clear both restriction types so the process is fully unrestricted, regardless of the tracked state
                    bool affinityCleared = ClearAffinitySkippingAllCores();
                    bool cpuSetCleared = ApplyCpuSet(mask);
                    // A partial clear is a failure: the process stays constrained to whatever was not cleared
                    result = affinityCleared && cpuSetCleared;
                    if (!result)
                        WindowLogger.Write($"WARNING: Partial clear of '{_executableName}' (Affinity cleared: {affinityCleared}, CPU Set cleared: {cpuSetCleared}); the process may still be restricted");
                    break;

                case MaskApplyType.CPUSet:
                    // Always clear the Affinity first: if a stale Affinity overlaps the CPU Set badly (e.g. empty intersection),
                    // the threads would keep running on the Affinity's cores instead of the CPU Set's
                    bool affinityClearedBeforeCpuSet = ClearAffinitySkippingAllCores();
                    result = ApplyCpuSet(mask);
                    if (!affinityClearedBeforeCpuSet)
                        WindowLogger.Write($"WARNING: Could not clear Affinity of '{_executableName}' before applying CPU Set; a stale Affinity will constrain the CPU Set");
                    break;

                case MaskApplyType.Affinity:
                    // Always clear the CPU Set first: a stale CPU Set would otherwise keep restricting the threads
                    bool cpuSetClearedBeforeAffinity = ApplyCpuSet(LogicalProcessorMask.NoMask);
                    result = ApplyAffinity(mask);
                    if (!cpuSetClearedBeforeAffinity)
                        WindowLogger.Write($"WARNING: Could not clear CPU Set of '{_executableName}' before applying Affinity; a stale CPU Set will constrain the Affinity");
                    break;

                default:
                    throw new NotImplementedException();
            }

            if (result)
                _previousMaskType = mask.MaskType;
            return result;
        }

        public bool ReapplyMask(LogicalProcessorMask mask)
        {
            // If the mask type changed since the last successful apply, apply it fresh (don't just assume it's up to date)
            if (_previousMaskType != mask.MaskType)
                return ApplyMask(mask);

            bool[] actualMask;
            try
            {
                actualMask = mask.MaskType switch
                {
                    MaskApplyType.CPUSet => GetCpuSetMask(),
                    MaskApplyType.Affinity => GetAffinityMask(),
                    _ => throw new NotImplementedException(),
                };

                // A CPU Set is constricted to the intersection of the CPU Set and the process Affinity,
                // so verify a stale/narrow Affinity is not limiting the CPU Set's effect
                if (mask.MaskType == MaskApplyType.CPUSet)
                {
                    bool[] affinityMask = GetAffinityMask();
                    bool affinityIsAllCores = affinityMask.All(enabled => enabled);
                    if (!affinityIsAllCores)
                    {
                        WindowLogger.Write($"WARNING: '{_executableName}' has a restrictive Affinity ({GetCurrentRestrictionInfo()}) constraining its CPU Set; clearing it before reapplying");
                        if (!ApplyAffinity(LogicalProcessorMask.NoMask))
                            return false;
                        return ApplyCpuSet(mask);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }

            if (actualMask.SequenceEqual(mask.BoolMask))
                return true;

            return mask.MaskType switch
            {
                MaskApplyType.CPUSet => ApplyCpuSet(mask),
                MaskApplyType.Affinity => ApplyAffinity(mask),
                _ => throw new NotImplementedException(),
            };
        }

        /// <summary>
        /// Apply a given mask as a CPU Set
        /// </summary>
        private bool ApplyCpuSet(LogicalProcessorMask mask)
        {
            int error;
            string extraHelpString;

            if (!AquireSetLimitedInfoHandle(out SafeProcessHandle setLimitedInfoHandle))
                return false;

            bool success;
            if (mask.MaskType == MaskApplyType.NoMask)
            {
                // Clear the CPU Set
                success = NativeMethods.SetProcessDefaultCpuSets(setLimitedInfoHandle, null, 0);
                if (success)
                {
                    _activeCpuSetMask = null;
                    SetCpuSetsForAllThreads(null); // Release the per-thread CPU Set assignments too
                    WindowLogger.Write($"Cleared CPU Set of '{_executableName}'");
                    return true;
                }

                error = Marshal.GetLastWin32Error();
                WindowLogger.Write($"ERROR: Could not clear CPU Set of '{_executableName}': {new Win32Exception(error).Message}");
                return false;
            }

            // Get an array of active CPU Set Ids for this mask
            List<uint> cpuSetIds = [];
            for (int i = 0; i < mask.BoolMask.Count; ++i)
            {
                try
                {
                    if (mask.BoolMask[i])
                        cpuSetIds.Add(_logicalProcessorToSetId[i]);
                }
                catch (KeyNotFoundException)
                {
                    WindowLogger.Write($"WARNING: Unable to include '{CpuInfo.LogicalProcessorNames[i]}' in Core Mask. It does not have a CPU Set ID");
                }
            }
            uint[] cpuSetIdsArray = cpuSetIds.ToArray();
            success = NativeMethods.SetProcessDefaultCpuSets(setLimitedInfoHandle, cpuSetIdsArray, (uint)cpuSetIdsArray.Length);
            if (success)
            {
                // The process default CPU Set only applies to threads created after it is set, so pin existing threads too.
                // An empty CPU Set means "no cores", so existing threads must have their per-thread pins released as well
                if (cpuSetIdsArray.Length > 0)
                {
                    _activeCpuSetMask = mask.BoolMask.ToArray();
                    SetCpuSetsForAllThreads(cpuSetIdsArray);
                }
                else
                {
                    _activeCpuSetMask = null;
                    SetCpuSetsForAllThreads(null);
                }
                WindowLogger.Write($"Applied CPU Set '{mask.Name}' to '{_executableName}'");
                return true;
            }

            error = Marshal.GetLastWin32Error();
            extraHelpString = (error == 5 && !Environment.IsPrivilegedProcess) ? " Try restarting CPU Set Setter Revamped as Admin" : " Likely due to anti-cheat";
            WindowLogger.Write($"ERROR: Could not apply CPU Set to '{_executableName}': {new Win32Exception(error).Message}{extraHelpString}");
            return false;
        }

        /// <summary>
        /// Assign every thread of this process to the given CPU Sets, or release them when <paramref name="cpuSetIds"/> is null.
        /// The process default CPU Set only applies to threads created after it is set
        /// </summary>
        private void SetCpuSetsForAllThreads(uint[]? cpuSetIds)
        {
            using SafeFileHandle snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPTHREAD, _pid);
            if (snapshot.IsInvalid)
                return;

            THREADENTRY32 threadEntry = new() { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
            if (!NativeMethods.Thread32First(snapshot, ref threadEntry))
                return;

            do
            {
                if (threadEntry.th32OwnerProcessID != _pid)
                    continue;

                IntPtr rawThreadHandle = NativeMethods.OpenThread(ThreadAccessFlags.THREAD_SET_LIMITED_INFORMATION, false, threadEntry.th32ThreadID);
                if (rawThreadHandle == IntPtr.Zero)
                    continue;

                using SafeProcessHandle threadHandle = new(rawThreadHandle, true);
                NativeMethods.SetThreadSelectedCpuSets(threadHandle, cpuSetIds, cpuSetIds is null ? 0 : (uint)cpuSetIds.Length);
            }
            while (NativeMethods.Thread32Next(snapshot, ref threadEntry));
        }

        private bool[] GetCpuSetMask()
        {
            if (_queryLimitedInfoHandle.IsInvalid)
            {
                throw new InvalidOperationException("Cannot get process CPU Sets due to invalid handle");
            }

            uint requiredIdCount = 0;
            if (!NativeMethods.GetProcessDefaultCpuSets(_queryLimitedInfoHandle, null, 0, ref requiredIdCount))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 0x7A) // ERROR_INSUFFICIENT_BUFFER
                    throw new Win32Exception(error);
            }

            uint[] cpuSetIds = new uint[requiredIdCount];
            if (!NativeMethods.GetProcessDefaultCpuSets(_queryLimitedInfoHandle, cpuSetIds, (uint)cpuSetIds.Length, ref requiredIdCount))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            bool[] result = new bool[CpuInfo.LogicalProcessorCount];
            foreach (uint cpuSetId in cpuSetIds)
            {
                if (_setIdToLogicalProcessor.TryGetValue(cpuSetId, out int logicalProcessor))
                    result[logicalProcessor] = true;
            }
            return result;
        }

        public string? GetCurrentRestrictionInfo()
        {
            try
            {
                // CPU Sets take precedence if any are applied, and are read back from the OS
                uint requiredIdCount = 0;
                bool hasCpuSets = !NativeMethods.GetProcessDefaultCpuSets(_queryLimitedInfoHandle, null, 0, ref requiredIdCount) && requiredIdCount > 0;
                if (hasCpuSets)
                {
                    uint[] cpuSetIds = new uint[requiredIdCount];
                    if (NativeMethods.GetProcessDefaultCpuSets(_queryLimitedInfoHandle, cpuSetIds, (uint)cpuSetIds.Length, ref requiredIdCount))
                    {
                        int[] cores = cpuSetIds
                            .Where(id => _setIdToLogicalProcessor.TryGetValue(id, out _))
                            .Select(id => _setIdToLogicalProcessor[id])
                            .OrderBy(core => core)
                            .ToArray();
                        return $"CPU Set: IDs {string.Join(",", cpuSetIds)} (cores {FormatCoreRanges(cores)})";
                    }
                }

                // No CPU Set applied, fall back to reporting the Affinity mask
                UIntPtr processMask = 0;
                UIntPtr systemMask = 0;
                if (NativeMethods.GetProcessAffinityMask(_queryLimitedInfoHandle, ref processMask, ref systemMask))
                {
                    int[] cores = Enumerable.Range(0, CpuInfo.LogicalProcessorCount)
                        .Where(i => (processMask & ((UIntPtr)1 << i)) != 0)
                        .ToArray();
                    if (cores.Length == CpuInfo.LogicalProcessorCount)
                        return "Affinity: all cores";
                    return $"Affinity: cores {FormatCoreRanges(cores)}";
                }
            }
            catch (Exception)
            {
                // Process may have exited or refuse access
                return null;
            }
            return null;
        }

        /// <summary>
        /// Read back the mask that is currently applied to this process, so it can be restored later.
        /// CPU Sets take precedence over Affinity. Returns NoMask when the process is unrestricted.
        /// </summary>
        public LogicalProcessorMask? GetCurrentMask()
        {
            try
            {
                bool[] cpuSetMask = GetCpuSetMask();
                if (cpuSetMask.Any(enabled => enabled))
                    return new LogicalProcessorMask("(current)", MaskApplyType.CPUSet, cpuSetMask.ToList(), []);

                bool[] affinityMask = GetAffinityMask();
                if (affinityMask.All(enabled => enabled))
                    return LogicalProcessorMask.NoMask;
                return new LogicalProcessorMask("(current)", MaskApplyType.Affinity, affinityMask.ToList(), []);
            }
            catch (Exception)
            {
                // Process may have exited or refuse access
                return null;
            }
        }

        /// <summary>
        /// Format a set of logical processor indexes as compact ranges, e.g. {0,1,2,3,6,7} -> "0-3,6-7"
        /// </summary>
        private static string FormatCoreRanges(int[] cores)
        {
            if (cores.Length == 0)
                return "none";

            List<string> parts = [];
            int start = cores[0];
            int prev = cores[0];
            for (int i = 1; i < cores.Length; ++i)
            {
                if (cores[i] == prev + 1)
                {
                    prev = cores[i];
                    continue;
                }
                parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
                start = prev = cores[i];
            }
            parts.Add(start == prev ? $"{start}" : $"{start}-{prev}");
            return string.Join(",", parts);
        }

        public bool ApplyPriority(ProcessPriorityClass? priorityClass)
        {
            if (priorityClass is null)
                return true;

            if (!AquireSetInfoHandle(out SafeProcessHandle setInfoHandle))
                return false;

            // Realtime and High priority require SeIncreaseBasePriorityPrivilege
            if (priorityClass is ProcessPriorityClass.RealTime or ProcessPriorityClass.High)
                NativeMethods.EnableIncreaseBasePriorityPrivilege();

            uint priorityClassFlag = priorityClass switch
            {
                ProcessPriorityClass.Idle => NativeMethods.IDLE_PRIORITY_CLASS,
                ProcessPriorityClass.BelowNormal => NativeMethods.BELOW_NORMAL_PRIORITY_CLASS,
                ProcessPriorityClass.Normal => NativeMethods.NORMAL_PRIORITY_CLASS,
                ProcessPriorityClass.AboveNormal => NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS,
                ProcessPriorityClass.High => NativeMethods.HIGH_PRIORITY_CLASS,
                ProcessPriorityClass.RealTime => NativeMethods.REALTIME_PRIORITY_CLASS,
                _ => NativeMethods.NORMAL_PRIORITY_CLASS,
            };

            bool success = NativeMethods.SetPriorityClass(setInfoHandle, priorityClassFlag);
            if (success)
                WindowLogger.Write($"Set priority of '{_executableName}' to {priorityClass}");
            else
                WindowLogger.Write($"ERROR: Could not set priority of '{_executableName}': {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return success;
        }

        private bool AquireSetLimitedInfoHandle(out SafeProcessHandle setLimitedInfoHandle)
        {
            if (_setLimitedInfoHandle is null)
            {
                _setLimitedInfoHandle = NativeMethods.OpenProcess(ProcessAccessFlags.PROCESS_SET_LIMITED_INFORMATION, false, _pid);
                if (_setLimitedInfoHandle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    string extraHelpString = (error == 5 && !Environment.IsPrivilegedProcess) ? " Try restarting CPU Set Setter Revamped as Admin" : "";
                    WindowLogger.Write($"ERROR: Could not open process '{_executableName}': {new Win32Exception(error).Message}{extraHelpString}");
                }
            }
            setLimitedInfoHandle = _setLimitedInfoHandle;
            return !setLimitedInfoHandle.IsInvalid;
        }

        private bool ApplyAffinity(LogicalProcessorMask mask)
        {
            int error;
            string extraHelpString;

            if (!AquireSetInfoHandle(out SafeProcessHandle setInfoHandle))
                return false;

            bool success;
            if (mask.MaskType == MaskApplyType.NoMask)
            {
                UIntPtr allMask = 0;
                for (int i = 0; i < CpuInfo.LogicalProcessorCount; ++i)
                {
                    allMask |= (UIntPtr)1 << i;
                }
                success = NativeMethods.SetProcessAffinityMask(setInfoHandle, allMask);
                if (success)
                {
                    _activeCpuSetMask = null;
                    WindowLogger.Write($"Cleared Affinity of '{_executableName}'");
                    return true;
                }

                error = Marshal.GetLastWin32Error();
                WindowLogger.Write($"ERROR: Could not clear Affinity of '{_executableName}': {new Win32Exception(error).Message}");
                return false;
            }

            UIntPtr bitMask = 0;
            for (int i = 0; i < mask.BoolMask.Count; ++i)
            {
                if (mask.BoolMask[i])
                    bitMask |= (UIntPtr)1 << i;
            }

            success = NativeMethods.SetProcessAffinityMask(setInfoHandle, bitMask);
            if (success)
            {
                _activeCpuSetMask = null;
                WindowLogger.Write($"Applied Affinity '{mask.Name}' to '{_executableName}'");
                return true;
            }

            error = Marshal.GetLastWin32Error();
            extraHelpString = (error == 5 && !Environment.IsPrivilegedProcess) ? " Try restarting CPU Set Setter Revamped as Admin" : " Likely due to anti-cheat";
            WindowLogger.Write($"ERROR: Could not apply Affinity to '{_executableName}': {new Win32Exception(error).Message}{extraHelpString}");
            return false;
        }

        /// <summary>
        /// Clear the process Affinity, but skip the syscall entirely when it is already unrestricted
        /// (every logical processor enabled). This avoids a guaranteed-failed clear for processes that
        /// only allow querying their Affinity, and removes an unnecessary syscall for every other
        /// process. When the current Affinity cannot be read, or is genuinely restricted, it falls back
        /// to the normal clear behavior
        /// </summary>
        private bool ClearAffinitySkippingAllCores()
        {
            try
            {
                bool[] affinityMask = GetAffinityMask();
                if (affinityMask.All(enabled => enabled))
                    return true;
            }
            catch (InvalidOperationException)
            {
                // The current Affinity could not be read; fall back to attempting the clear
            }
            catch (Win32Exception)
            {
                // The current Affinity could not be read; fall back to attempting the clear
            }

            return ApplyAffinity(LogicalProcessorMask.NoMask);
        }

        private bool[] GetAffinityMask()
        {
            if (_queryLimitedInfoHandle.IsInvalid)
            {
                throw new InvalidOperationException("Cannot get process Affinity due to invalid handle");
            }

            UIntPtr bitMaskProcess = 0;
            UIntPtr bitMaskSystem = 0;
            if (!NativeMethods.GetProcessAffinityMask(_queryLimitedInfoHandle, ref bitMaskProcess, ref bitMaskSystem))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (bitMaskProcess == 0)
            {
                throw new InvalidOperationException("GetProcessAffinityMask returned a mask of 0");
            }

            bool[] result = new bool[CpuInfo.LogicalProcessorCount];
            for (int i = 0; i < result.Length; ++i)
            {
                if ((bitMaskProcess & ((UIntPtr)1 << i)) != 0)
                    result[i] = true;
            }
            return result;
        }

        private bool AquireSetInfoHandle(out SafeProcessHandle setInfoHandle)
        {
            if (_setInfoHandle is null)
            {
                _setInfoHandle = NativeMethods.OpenProcess(ProcessAccessFlags.PROCESS_SET_INFORMATION, false, _pid);
                if (_setInfoHandle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    string extraHelpString = (error == 5 && !Environment.IsPrivilegedProcess) ? " Try restarting CPU Set Setter Revamped as Admin" : "";
                    WindowLogger.Write($"ERROR: Could not open process '{_executableName}': {new Win32Exception(error).Message}{extraHelpString}");
                }
            }
            setInfoHandle = _setInfoHandle;
            return !setInfoHandle.IsInvalid;
        }

        /// <summary>
        /// Get the CPU Set Id of each logical processor 
        /// </summary>
        private static Dictionary<int, uint> GetCpuSetIdPerLogicalProcessor()
        {
            uint bufferLength = 0;
            if (!NativeMethods.GetSystemCpuSetInformation(IntPtr.Zero, 0, ref bufferLength, new(), 0))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 0x7A) // ERROR_INSUFFICIENT_BUFFER
                    throw new Win32Exception(error);
            }

            Dictionary<int, uint> cpuSets = [];
            // Create the buffer and get the CPU Set information
            IntPtr buffer = Marshal.AllocHGlobal((int)bufferLength);
            try
            {
                if (!NativeMethods.GetSystemCpuSetInformation(buffer, bufferLength, ref bufferLength, new(), 0))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                IntPtr current = buffer;
                IntPtr bufferEnd = buffer + (IntPtr)bufferLength;
                int itemSize = Marshal.SizeOf<SYSTEM_CPU_SET_INFORMATION>();
                while (current < bufferEnd)
                {
                    SYSTEM_CPU_SET_INFORMATION item = Marshal.PtrToStructure<SYSTEM_CPU_SET_INFORMATION>(current);

                    if (item.Type != CPU_SET_INFORMATION_TYPE.CpuSetInformation)
                    {
                        throw new InvalidCastException("Invalid data type encountered; aborting");
                    }

                    cpuSets.Add(item.LogicalProcessorIndex, item.Id);

                    current += (IntPtr)item.Size;
                }
                return cpuSets;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose()
        {
            _queryLimitedInfoHandle.Dispose();
            _setLimitedInfoHandle?.Dispose();
            _setInfoHandle?.Dispose();
            _cpuTimeMovingAverageBuffer.Clear();
            _threadCpuSamples.Clear();
        }

        private class CpuTimeTimestamp
        {
            public DateTime Timestamp { get; init; }
            public TimeSpan TotalCpuTime { get; init; }
        }

        private class ThreadCpuSample
        {
            public long CpuTicks { get; init; }
            public int ProcessorIndex { get; init; }
        }
    }
}
