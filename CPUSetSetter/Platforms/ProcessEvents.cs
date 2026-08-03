namespace CPUSetSetter.Platforms
{
    /// <summary>
    /// Provides listeners for newly created and exited processes
    /// </summary>
    public static class ProcessEvents
    {
        public static event EventHandler<NewProcessEventArgs> ProcessCreated
        {
            add => Default.ProcessCreated += value;
            remove => Default.ProcessCreated -= value;
        }

        public static event EventHandler<ExitedProcessEventArgs> ProcessExited
        {
            add => Default.ProcessExited += value;
            remove => Default.ProcessExited -= value;
        }

        public static void Start() => Default.Start();

        /// <summary>
        /// Re-enumerate all running processes, firing <see cref="ProcessCreated"/> for each of them so the
        /// process list is resynchronized with the OS. Existing entries are deduplicated by PID on the receiving side
        /// </summary>
        public static void Rescan() => Default.Rescan();

        /// <summary>
        /// Enumerate the PIDs of all current processes
        /// </summary>
        public static HashSet<uint> GetCurrentProcessPids()
        {
#if WINDOWS
            return ProcessEventsWindows.GetCurrentProcessPids();
#else
            return [];
#endif
        }

        /// <summary>
        /// Create a process handler for a given PID, e.g. for the Benchmark feature.
        /// Returns null when the process cannot be opened
        /// </summary>
        public static IProcessHandler? CreateProcessHandler(string name, uint pid)
        {
#if WINDOWS
            return ProcessEventsWindows.CreateProcessHandler(name, pid);
#else
            return null;
#endif
        }

        private static IProcessEvents? _default;

#if WINDOWS
        public static IProcessEvents Default => _default ??= new ProcessEventsWindows();
#endif
    }

    public interface IProcessEvents
    {
        event EventHandler<NewProcessEventArgs>? ProcessCreated;
        event EventHandler<ExitedProcessEventArgs>? ProcessExited;

        void Start();
        void Rescan();
    }

    public class ProcessInfo
    {
        public string Name { get; }
        public string ImagePath { get; }
        public uint PID { get; }
        public IProcessHandler ProcessHandler { get; }

        public ProcessInfo(string name, string imagePath, uint pid, IProcessHandler processHandler)
        {
            Name = name;
            ImagePath = imagePath;
            PID = pid;
            ProcessHandler = processHandler;
        }
    }

    public class NewProcessEventArgs : EventArgs
    {
        public ProcessInfo Info { get; }

        public NewProcessEventArgs(ProcessInfo processInfo)
        {
            Info = processInfo;
        }
    }

    public class ExitedProcessEventArgs : EventArgs
    {
        public uint PID { get; }

        public ExitedProcessEventArgs(uint pid)
        {
            PID = pid;
        }
    }
}
