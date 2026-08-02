using CommunityToolkit.Mvvm.ComponentModel;
using CPUSetSetter.Config.Models;
using CPUSetSetter.Platforms;
using CPUSetSetter.Util;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;


namespace CPUSetSetter.UI.Tabs.Processes
{
    public partial class ProcessesTabViewModel : ObservableObject
    {
        public static ProcessesTabViewModel? Instance { get; private set; }

        private readonly Dispatcher _dispatcher;
        private readonly ListCollectionView runningProcessesView;
        private bool _sortingPausedByCtrl;
        private bool _sortingPausedByTray;

        public static PausableObservableCollection<ProcessListEntryViewModel> RunningProcesses { get; } = [];

        [ObservableProperty]
        private string _processNameFilter = string.Empty;

        [ObservableProperty]
        private ProcessListEntryViewModel? _selectedProcess;

        public ProcessesTabViewModel(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            Instance = this;

            ProcessEvents.Default.ProcessCreated += (_, e) => OnNewProcess(e.Info);
            ProcessEvents.Default.ProcessExited += (_, e) => OnExitedProcess(e.PID);
            ProcessEvents.Default.Start();

            runningProcessesView = (ListCollectionView)CollectionViewSource.GetDefaultView(RunningProcesses);
            runningProcessesView.SortDescriptions.Add(new(nameof(ProcessListEntryViewModel.AverageCpuUsage), ListSortDirection.Descending));
            runningProcessesView.IsLiveSorting = true;
            runningProcessesView.LiveSortingProperties.Add(nameof(ProcessListEntryViewModel.AverageCpuUsage));
            runningProcessesView.Filter = item => ((ProcessListEntryViewModel)item).Name.Contains(ProcessNameFilter, StringComparison.OrdinalIgnoreCase);

            Task.Run(ProcessCpuUsageUpdateLoop);
            Task.Run(ProcessReapplyLoop);
        }

        /// <summary>
        /// Triggered by a LogicalProcessorMask when its hotkeys are pressed
        /// </summary>
        public void OnMaskHotkeyPressed(LogicalProcessorMask mask)
        {
            ProcessListEntryViewModel? foregroundProcess = GetCurrentForegroundProcess();
            if (foregroundProcess is not null)
            {
                bool success = foregroundProcess.SetMask(mask, true);
                if (success)
                {
                    if (mask.MaskType == MaskApplyType.NoMask)
                        HotkeySoundPlayer.Default.PlayCleared();
                    else
                        HotkeySoundPlayer.Default.PlayApplied();
                }
                else
                {
                    HotkeySoundPlayer.PlayError();
                }
            }
        }

        /// <summary>
        /// Pause the live sorting of the Processes list
        /// </summary>
        public void PauseListUpdates()
        {
            _sortingPausedByCtrl = true;
            if (runningProcessesView != null)
            {
                runningProcessesView.IsLiveSorting = false;
                RunningProcesses.SuppressNotifications(true);
            }
        }

        /// <summary>
        /// Resume the live sorting of the Processes list
        /// </summary>
        public void ResumeListUpdates()
        {
            _sortingPausedByCtrl = false;
            if (runningProcessesView != null)
            {
                if (!_sortingPausedByTray)
                    RunningProcesses.SuppressNotifications(false);
                runningProcessesView.IsLiveSorting = !_sortingPausedByTray;
            }
        }

        partial void OnProcessNameFilterChanged(string value)
        {
            runningProcessesView.Refresh();
        }

        partial void OnSelectedProcessChanged(ProcessListEntryViewModel? oldValue, ProcessListEntryViewModel? newValue)
        {
            // Refresh the per-core usage of the newly selected process immediately, off the UI thread
            if (newValue is not null)
                _ = Task.Run(newValue.UpdatePerCoreUsage);
        }

        private void OnNewProcess(ProcessInfo pInfo)
        {
            _dispatcher.Invoke(() =>
            {
                if (!RunningProcesses.Any(x => x.Pid == pInfo.PID))
                {
                    RunningProcesses.Add(new ProcessListEntryViewModel(pInfo));
                }
                else
                {
                    // Both the trace and the polling listeners can fire for the same process;
                    // dispose the duplicate's process handler so its OS handle is released
                    pInfo.ProcessHandler.Dispose();
                }
            });
        }

        private void OnExitedProcess(uint exitedPid)
        {
            _dispatcher.Invoke(() =>
            {
                for (int i = RunningProcesses.Count - 1; i >= 0; --i)
                {
                    if (RunningProcesses[i].Pid == exitedPid)
                    {
                        RunningProcesses[i].Dispose();
                        RunningProcesses.RemoveAt(i);
                    }
                }
            });
        }

        private ProcessListEntryViewModel? GetCurrentForegroundProcess()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == 0)
            {
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            return RunningProcesses.FirstOrDefault(x => x!.Pid == pid, null);
        }

        private async Task ProcessCpuUsageUpdateLoop()
        {
            while (true)
            {
                bool windowIsVisible = false;
                await _dispatcher.InvokeAsync(() =>
                {
                    windowIsVisible = App.Current.MainWindow.Visibility == Visibility.Visible;

                    // Pause CPU usage, per-core usage and live sorting while minimized to the system tray,
                    // to reduce CPU usage in the background
                    if (!windowIsVisible)
                    {
                        _sortingPausedByTray = true;
                        runningProcessesView.IsLiveSorting = false;
                        RunningProcesses.SuppressNotifications(true);
                        return;
                    }

                    if (_sortingPausedByTray)
                    {
                        _sortingPausedByTray = false;
                        runningProcessesView.IsLiveSorting = !_sortingPausedByCtrl;
                        if (!_sortingPausedByCtrl)
                        {
                            RunningProcesses.SuppressNotifications(false);
                            runningProcessesView.Refresh();
                        }
                    }

                    foreach (ProcessListEntryViewModel pEntry in RunningProcesses)
                    {
                        pEntry.UpdateCpuUsage();
                    }
                    // Only the selected process has its per-core usage sampled, as it is the only one shown in the row details
                    SelectedProcess?.UpdatePerCoreUsage();
                });
                await Task.Delay(1000);
            }
        }

        private async Task ProcessReapplyLoop()
        {
            while (true)
            {
                await Task.Delay(15000);
                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (ProcessListEntryViewModel pEntry in RunningProcesses)
                    {
                        if (pEntry.AutoReapply)
                            pEntry.ReapplyMask();
                    }
                });
            }
        }
    }
}
