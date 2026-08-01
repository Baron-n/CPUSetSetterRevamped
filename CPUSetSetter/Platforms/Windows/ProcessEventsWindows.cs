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

            // Try the low-latency ETW-based listeners first, falling back to polling when they cannot be started
            if (!TryStartTraceListeners())
                StartPollingListeners();

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
            SafeProcessHandle hProcess = NativeMethods.OpenProcess(ProcessAccessFlags.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);

            string exePath = knownExecutablePath;
            if (!hProcess.IsInvalid)
            {
                char[] buffer = new char[1024];
                uint size = 1024;
                bool success = NativeMethods.QueryFullProcessImageNameW(hProcess, 0, buffer, ref size);
                if (success)
                    exePath = new string(buffer[..(int)size]);
            }

            return new(name, exePath, pid, new ProcessHandlerWindows(name, pid, hProcess));
        }
    }
}
