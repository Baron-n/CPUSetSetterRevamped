using CommunityToolkit.Mvvm.ComponentModel;
using CPUSetSetter.Config.Models;
using CPUSetSetter.Util;
using CPUSetSetter.Platforms;
using CPUSetSetter.UI.Tabs.Processes.CoreUsage;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CoreUsageModel = CPUSetSetter.UI.Tabs.Processes.CoreUsage.CoreUsage;


namespace CPUSetSetter.UI.Tabs.Processes
{
    /// <summary>
    /// Represents a row in the Processes list
    /// </summary>
    public partial class ProcessListEntryViewModel : ObservableObject, IDisposable
    {
        private readonly IProcessHandler _processHandler;
        private readonly ListCollectionView _perCoreUsagesView;
        private LogicalProcessorMask _lastAppliedMask = LogicalProcessorMask.NoMask;

        public uint Pid { get; }
        public string Name { get; }
        public string ImagePath { get; }
        public bool AutoReapply { get; set; }

        /// <summary>
        /// The CPU usage attributed to each logical processor of this process. Only refreshed for the selected row
        /// </summary>
        public ObservableCollection<CoreUsageModel> PerCoreUsages { get; } = new(CpuInfo.LogicalProcessorNames.Select(cpuName => new CoreUsageModel(cpuName)));

        /// <summary>
        /// A filtered view of <see cref="PerCoreUsages"/> that only shows the logical processors actually being used
        /// </summary>
        public ICollectionView PerCoreUsagesView => _perCoreUsagesView;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AverageCpuPercentageStr))]
        private double _averageCpuUsage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CurrentCpuPercentageStr))]
        private double _currentCpuUsage;

        [ObservableProperty]
        private LogicalProcessorMask _mask;

        [ObservableProperty]
        private bool _previousApplyFailed = false;

        /// <summary>
        /// The restriction actually read back from the OS (CPU Set IDs or Affinity), for verification. Empty when unavailable
        /// </summary>
        [ObservableProperty]
        private string _activeRestrictionInfo = "";

        /// <summary>Whether a CPU Set restriction is currently active on this process</summary>
        [ObservableProperty]
        private bool _hasCpuSetRestriction;

        /// <summary>Whether an Affinity restriction is currently active on this process</summary>
        [ObservableProperty]
        private bool _hasAffinityRestriction;

        /// <summary>Whether a ProgramRule matches this process (a Core Mask is applied automatically)</summary>
        [ObservableProperty]
        private bool _hasRuleMatch;

        public string AverageCpuPercentageStr => AverageCpuUsage == -1 ? "" : $"{AverageCpuUsage * 100:F1}%";

        public string CurrentCpuPercentageStr => CurrentCpuUsage == -1 ? "" : $"{CurrentCpuUsage * 100:F1}%";

        /// <summary>
        /// The 3 most used logical processors of this process, for a quick overview
        /// </summary>
        public string BusiestCoresStr
        {
            get
            {
                IEnumerable<CoreUsageModel> busiest = PerCoreUsages
                    .Where(c => c.Utility > 0.01)
                    .OrderByDescending(c => c.Utility)
                    .Take(3);
                return string.Join("  ·  ", busiest.Select(c => $"{c.Name} {c.Utility * 100:F0}%"));
            }
        }

        public ProcessListEntryViewModel(ProcessInfo pInfo)
        {
            Pid = pInfo.PID;
            Name = pInfo.Name;
            ImagePath = pInfo.ImagePath;
            _processHandler = pInfo.ProcessHandler;

            _perCoreUsagesView = (ListCollectionView)CollectionViewSource.GetDefaultView(PerCoreUsages);
            _perCoreUsagesView.Filter = item => ((CoreUsageModel)item).Utility > 0.01;

            ProgramRule? programRule = RuleHelpers.GetProgramRuleOrNull(pInfo.ImagePath);
            programRule?.AddRunningProcess(this);
            HasRuleMatch = programRule is not null;

            LogicalProcessorMask mask = programRule?.Mask ?? LogicalProcessorMask.NoMask;
            SetMask(mask, false);
            _mask = mask; // _mask is already set by SetMask, this just suppresses a warning
            AutoReapply = programRule?.AutoReapply ?? false;

            AverageCpuUsage = _processHandler.GetAverageCpuUsage();
        }

        /// <summary>
        /// Refresh the current (instantaneous) and average CPU usage in a single OS query. Call from the UI thread
        /// </summary>
        public void UpdateCpuUsage()
        {
            _processHandler.GetCpuUsage(out double currentUsage, out double averageUsage);
            CurrentCpuUsage = currentUsage;
            AverageCpuUsage = averageUsage;
        }

        /// <summary>
        /// Refresh the per-core CPU usage bars. Call from the UI thread; returns quickly if the process is not accessible
        /// </summary>
        public void UpdatePerCoreUsage()
        {
            ApplyPerCoreUsage(SamplePerCoreUsage());
            UpdateRestrictionInfo();
        }

        /// <summary>
        /// Sample the per-core CPU usage off the UI thread. Expensive: enumerates every thread of the process
        /// </summary>
        public double[]? SamplePerCoreUsage()
        {
            return _processHandler.GetPerCoreCpuUsage();
        }

        /// <summary>
        /// Refresh the restriction text and chips. Call from the UI thread; does not sample per-core usage
        /// </summary>
        public void UpdateRestrictionInfo()
        {
            ActiveRestrictionInfo = _processHandler.GetCurrentRestrictionInfo() ?? "";
            UpdateRestrictionChips(ActiveRestrictionInfo);
        }

        /// <summary>
        /// Apply a sampled per-core usage array to the heat cells. Call from the UI thread
        /// </summary>
        public void ApplyPerCoreUsage(double[]? perCoreUsages)
        {
            if (perCoreUsages is null)
                return;

            for (int i = 0; i < PerCoreUsages.Count; ++i)
            {
                PerCoreUsages[i].Utility = perCoreUsages[i];
            }
            OnPropertyChanged(nameof(BusiestCoresStr));

            // Re-apply the filter so only the logical processors that are actually being used stay visible
            if (_perCoreUsagesView.Dispatcher.CheckAccess())
                _perCoreUsagesView.Refresh();
            else
                _perCoreUsagesView.Dispatcher.Invoke(() => _perCoreUsagesView.Refresh());
        }

        private void UpdateRestrictionChips(string restrictionInfo)
        {
            HasCpuSetRestriction = restrictionInfo.StartsWith("CPU Set:", StringComparison.Ordinal);
            HasAffinityRestriction = restrictionInfo.StartsWith("Affinity:", StringComparison.Ordinal)
                && !restrictionInfo.Contains("all cores", StringComparison.Ordinal);
        }

        public void ReapplyMask()
        {
            if (Mask.MaskType == MaskApplyType.NoMask)
                return;

            PreviousApplyFailed = !_processHandler.ReapplyMask(Mask);
        }

        /// <summary>
        /// Apply a priority class to this process. Null leaves it untouched
        /// </summary>
        public void ApplyPriority(ProcessPriorityClass? priorityClass)
        {
            _processHandler.ApplyPriority(priorityClass);
        }

        public bool SetMask(LogicalProcessorMask newMask, bool updateRule)
        {
            if (newMask == _lastAppliedMask) // Return the previous status if the mask is still the same
                return !PreviousApplyFailed;

            _lastAppliedMask = newMask;
            Mask = newMask;

            bool ruleSuccess = true;
            if (updateRule && ImagePath.Length != 0)
            {
                // SetMask was called from the Processes tab UI, so the ProgramRule needs to be updated or created too
                ProgramRule? programRule = RuleHelpers.GetProgramRuleOrNull(ImagePath);
                if (programRule is null)
                {
                    // No ProgramRule exists for this process' ImagePath yet. Create a new ProgramRule
                    programRule = new(ImagePath, newMask, false, true);
                    AppConfig.Instance.ProgramRules.Add(programRule);
                }
                ruleSuccess = programRule.SetMask(newMask, true);
            }
            bool success = _processHandler.ApplyMask(newMask);
            PreviousApplyFailed = !success;
            return success && ruleSuccess;
        }

        /// <summary>
        /// The UI picked a different mask
        /// </summary>
        partial void OnMaskChanged(LogicalProcessorMask? oldValue, LogicalProcessorMask newValue)
        {
            if (oldValue is not null)
                oldValue.MaskChanged -= OnMaskEdited;
            newValue.MaskChanged += OnMaskEdited;
            SetMask(newValue, true);
        }

        private void OnMaskEdited(object? sender, EventArgs e)
        {
            // One of the logical processors in the mask or the type has changed, apply it
            PreviousApplyFailed = !_processHandler.ApplyMask(Mask);
        }

        /// <summary>
        /// The process has exited
        /// </summary>
        public void Dispose()
        {
            RuleHelpers.GetProgramRuleOrNull(ImagePath)?.RemoveRunningProcess(this);
            Mask.MaskChanged -= OnMaskEdited;
            _processHandler.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
