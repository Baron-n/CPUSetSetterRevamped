using CPUSetSetter.Platforms.Windows;
using CPUSetSetter.UI.Tabs.Processes;
using Microsoft.Win32.SafeHandles;
using System.Management;


namespace CPUSetSetter.Platforms
{
    public class ProcessEventsWindows : IProcessEvents
    {
        private bool _hasStarted = false;

        private ManagementEventWatcher? _traceStartWatcher;
        private ManagementEventWatcher? _traceStopWatcher;
        private ManagementEventWatcher? _pollStartWatcher;
        private ManagementEventWatcher? _pollStopWatcher;

        public event EventHandler<NewProcessEventArgs>? ProcessCreated;
        public event EventHandler<ExitedProcessEventArgs>? ProcessExited;

        public void Start()
        {
            if (_hasStarted)
                return;
            _hasStarted = true;

            // Always start the polling listeners as a reliable baseline: they work regardless of elevation,
            // and new processes must show up in the list even if the trace events are not delivered
            StartPollingListeners();

            // Additionally try the low-latency ETW trace listeners, which update the list instantly when they work
            if (TryStartTraceListeners())
                WindowLogger.Write("Process event trace active, low-latency updates enabled");
            else
                WindowLogger.Write("Using 5-second process polling (trace unavailable)");

            ListCurrentProcesses();
        }

        private void ListCurrentProcesses()
        {
            ManagementObjectSearcher searcher = new("SELECT Name, ProcessId, CreationDate FROM Win32_Process");

            foreach (ManagementBaseObject process in searcher.Get())
            {
                AddNewProcess(ParseName(process), ParsePid(process), "");
            }
        }

        public void Rescan()
        {
            ListCurrentProcesses();
        }

        /// <summary>
        /// Enumerate the PIDs of all current processes
        /// </summary>
        public static HashSet<uint> GetCurrentProcessPids()
        {
            HashSet<uint> pids = [];
            ManagementObjectSearcher searcher = new("SELECT ProcessId FROM Win32_Process");
            foreach (ManagementBaseObject process in searcher.Get())
            {
                pids.Add(ParsePid(process));
            }
            return pids;
        }

        /// <summary>
        /// Subscribe to the low-latency process events via the ETW-backed Win32_ProcessStartTrace and
        /// Win32_ProcessStopTrace classes, which fire as soon as a process starts or exits.
        /// Returns false when the subscription fails, which happens when the app is not running elevated.
        /// </summary>
        private bool TryStartTraceListeners()
        {
            try
            {
                _traceStartWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _traceStartWatcher.EventArrived += (_, e) =>
                {
                    ManagementBaseObject data = (ManagementBaseObject)e.NewEvent;
                    AddNewProcess((string)data["ProcessName"], (uint)data["ProcessID"], data["ExecutablePath"] as string ?? "");
                };
                _traceStartWatcher.Start();

                _traceStopWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
                _traceStopWatcher.EventArrived += (_, e) =>
                {
                    ManagementBaseObject data = (ManagementBaseObject)e.NewEvent;
                    ProcessExited?.Invoke(this, new((uint)data["ProcessID"]));
                };
                _traceStopWatcher.Start();
                return true;
            }
            catch (ManagementException ex)
            {
                // Subscribing to the ETW trace requires elevation, so fall back to polling when not running as admin
                WindowLogger.Write($"Process event trace unavailable (running without admin?), falling back to polling: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                WindowLogger.Write($"Failed to start process event trace, falling back to polling: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Fall back to polling-based process events, which fire every ~5 seconds but work without elevation
        /// </summary>
        private void StartPollingListeners()
        {
            string startQuery = "SELECT * FROM __InstanceCreationEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'";
            _pollStartWatcher = new ManagementEventWatcher(new WqlEventQuery(startQuery));
            _pollStartWatcher.EventArrived += (_, e) =>
            {
                ManagementBaseObject process = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                AddNewProcess(ParseName(process), ParsePid(process), "");
            };
            _pollStartWatcher.Start();

            string stopQuery = "SELECT * FROM __InstanceDeletionEvent WITHIN 5 WHERE TargetInstance ISA 'Win32_Process'";
            _pollStopWatcher = new ManagementEventWatcher(new WqlEventQuery(stopQuery));
            _pollStopWatcher.EventArrived += (_, e) =>
            {
                ManagementBaseObject process = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                ProcessExited?.Invoke(this, new(ParsePid(process)));
            };
            _pollStopWatcher.Start();
        }

        private void AddNewProcess(string name, uint pid, string knownExecutablePath)
        {
            ProcessInfo pInfo = ParseManagementProcess(name, pid, knownExecutablePath);
            ProcessCreated?.Invoke(this, new NewProcessEventArgs(pInfo));
        }

        private static string ParseName(ManagementBaseObject process)
        {
            return (string)process["Name"];
        }

        private static uint ParsePid(ManagementBaseObject process)
        {
            return (uint)process["ProcessId"];
        }

        private static ProcessInfo ParseManagementProcess(string name, uint pid, string knownExecutablePath)
        {
            string exePath = knownExecutablePath;
            IProcessHandler? processHandler = CreateProcessHandler(name, pid);
            if (processHandler is ProcessHandlerWindows windowsHandler)
            {
                SafeProcessHandle hProcess = windowsHandler.QueryHandle;
                if (!hProcess.IsInvalid)
                {
                    char[] buffer = new char[1024];
                    uint size = 1024;
                    bool success = NativeMethods.QueryFullProcessImageNameW(hProcess, 0, buffer, ref size);
                    if (success)
                        exePath = new string(buffer[..(int)size]);
                }
            }

            // The process may have exited or denied access between enumeration and opening it.
            // Keep a handler wrapping an invalid query handle so the process still shows up in the list
            // (with no CPU data); set handles are still opened lazily when a mask is applied
            processHandler ??= new ProcessHandlerWindows(name, pid, new SafeProcessHandle(IntPtr.Zero, false));
            return new(name, exePath, pid, processHandler);
        }

        /// <summary>
        /// Open a query-limited-information handle to the process, so CPU usage can be sampled and masks applied.
        /// Returns null when the process cannot be opened (e.g. it exited or access is denied)
        /// </summary>
        public static IProcessHandler? CreateProcessHandler(string name, uint pid)
        {
            SafeProcessHandle hProcess = NativeMethods.OpenProcess(ProcessAccessFlags.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess.IsInvalid)
                return null;
            return new ProcessHandlerWindows(name, pid, hProcess);
        }
    }
}
