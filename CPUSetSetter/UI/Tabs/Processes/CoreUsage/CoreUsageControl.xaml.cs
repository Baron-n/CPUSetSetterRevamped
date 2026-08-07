using CPUSetSetter.Platforms;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;


namespace CPUSetSetter.UI.Tabs.Processes.CoreUsage
{
    /// <summary>
    /// Shows the usage with a bar for every logical processor in the system, with a different
    /// color when a processor is parked. Though technically not correct, "Core" just sounds a
    /// lot better than "logical processor"
    /// </summary>
    public partial class CoreUsageControl : UserControl
    {
        private static bool _isRunning = false;
        private static List<CoreUsage> coreUsages = CpuInfo.LogicalProcessorNames.Select(cpuName => new CoreUsage(cpuName)).ToList();
        private static List<CoreUsageGroup> coreGroups = BuildCoreGroups();
        private static CoreUsageControl? _instance;

        // DependencyProperties
        public static readonly DependencyProperty BarBackgroundProperty =
            DependencyProperty.Register(
                nameof(BarBackground),
                typeof(Brush),
                typeof(CoreUsageControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(192, 192, 192))));

        public static readonly DependencyProperty BarForegroundProperty =
            DependencyProperty.Register(
                nameof(BarForeground),
                typeof(Brush),
                typeof(CoreUsageControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0, 173, 218))));

        public static readonly DependencyProperty BarBorderBrushProperty =
            DependencyProperty.Register(
                nameof(BarBorderBrush),
                typeof(Brush),
                typeof(CoreUsageControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(105, 105, 105))));

        public static readonly DependencyProperty BarParkedBackgroundProperty =
            DependencyProperty.Register(
                nameof(BarParkedBackground),
                typeof(Brush),
                typeof(CoreUsageControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(128, 128, 128))));

        public static readonly DependencyProperty BarParkedForegroundProperty =
            DependencyProperty.Register(
                nameof(BarParkedForeground),
                typeof(Brush),
                typeof(CoreUsageControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(84, 94, 94))));

        // Properties
        public Brush BarBackground
        {
            get => (Brush)GetValue(BarBackgroundProperty);
            set => SetValue(BarBackgroundProperty, value);
        }

        public Brush BarForeground
        {
            get => (Brush)GetValue(BarForegroundProperty);
            set => SetValue(BarForegroundProperty, value);
        }

        public Brush BarBorderBrush
        {
            get => (Brush)GetValue(BarBorderBrushProperty);
            set => SetValue(BarBorderBrushProperty, value);
        }

        public Brush BarParkedBackground
        {
            get => (Brush)GetValue(BarParkedBackgroundProperty);
            set => SetValue(BarParkedBackgroundProperty, value);
        }

        public Brush BarParkedForeground
        {
            get => (Brush)GetValue(BarParkedForegroundProperty);
            set => SetValue(BarParkedForegroundProperty, value);
        }

        /// <summary>
        /// The average usage of all logical processors, shown in the header
        /// </summary>
        public static readonly DependencyProperty TotalUsageStrProperty =
            DependencyProperty.Register(nameof(TotalUsageStr), typeof(string), typeof(CoreUsageControl), new PropertyMetadata("0%"));

        public string TotalUsageStr
        {
            get => (string)GetValue(TotalUsageStrProperty);
            set => SetValue(TotalUsageStrProperty, value);
        }

        /// <summary>
        /// Number of core groups, used to lay the groups out side by side
        /// </summary>
        public static readonly DependencyProperty GroupCountProperty =
            DependencyProperty.Register(nameof(GroupCount), typeof(int), typeof(CoreUsageControl), new PropertyMetadata(1));

        public int GroupCount
        {
            get => (int)GetValue(GroupCountProperty);
            set => SetValue(GroupCountProperty, value);
        }

        public CoreUsageControl()
        {
            InitializeComponent();
            _instance = this;

            coreUsageItemsControl.ItemsSource = coreGroups;
            GroupCount = coreGroups.Count;

            if (!_isRunning)
            {
                _isRunning = true;
                Task.Run(async () => await PerCoreUsageUpdateLoop(Dispatcher));
            }
        }

        /// <summary>
        /// Split the logical processors into named groups, using whatever the topology makes available:
        /// P-Cores / E-Cores (plus LPE) on Intel hybrid CPUs, and a per-die group (CCD on AMD) on
        /// multi-die CPUs. Everything else falls back to a single "Cores" group. The returned groups
        /// share the same <see cref="CoreUsage"/> instances as the flat sampler list, so updating the
        /// sampler automatically updates the grouped display
        /// </summary>
        private static List<CoreUsageGroup> BuildCoreGroups()
        {
            IReadOnlyList<LogicalProcessorTopologyInfo> topology = CpuInfo.LogicalProcessorTopology;
            if (topology.Count != coreUsages.Count)
                return [new("Cores", coreUsages)];

            // Intel hybrid CPUs: group by efficiency class (P-Cores / E-Cores, plus LPE if present)
            if (CpuInfo.Manufacturer == Manufacturer.Intel)
            {
                List<int> efficiencyClasses = topology
                    .Select(t => t.EfficiencyClass)
                    .Distinct()
                    .OrderByDescending(e => e)
                    .ToList();

                if (efficiencyClasses.Count > 1)
                {
                    string[] groupNames = ["P-Cores", "E-Cores", "LPE-Cores"];
                    List<CoreUsageGroup> groups = [];
                    for (int i = 0; i < efficiencyClasses.Count && i < groupNames.Length; ++i)
                    {
                        int efficiencyClass = efficiencyClasses[i];
                        groups.Add(new(groupNames[i], CoresInGroup(topology, j => topology[j].EfficiencyClass == efficiencyClass)));
                    }
                    groups[^1].ShowSeparator = false;
                    return groups;
                }
            }

            // Multi-die CPUs (e.g. multi-CCD AMD): group by die/CCX when more than one die is present
            List<int> dies = topology
                .Select(t => t.DieIndex)
                .Where(die => die >= 0)
                .Distinct()
                .OrderBy(die => die)
                .ToList();
            if (dies.Count > 1)
            {
                string dieName = CpuInfo.Manufacturer == Manufacturer.AMD ? "CCD" : "Die";
                List<CoreUsageGroup> dieGroups = [];
                foreach (int die in dies)
                    dieGroups.Add(new($"{dieName} {die}", CoresInGroup(topology, j => topology[j].DieIndex == die)));
                dieGroups[^1].ShowSeparator = false;
                return dieGroups;
            }

            return [new("Cores", coreUsages)];
        }

        /// <summary>
        /// Collect the <see cref="CoreUsage"/> instances whose matching topology entry passes the predicate
        /// </summary>
        private static List<CoreUsage> CoresInGroup(IReadOnlyList<LogicalProcessorTopologyInfo> topology, Func<int, bool> predicate)
        {
            List<CoreUsage> groupCores = [];
            for (int j = 0; j < coreUsages.Count; ++j)
            {
                if (predicate(j))
                    groupCores.Add(coreUsages[j]);
            }
            return groupCores;
        }

        private static async Task PerCoreUsageUpdateLoop(Dispatcher dispatcher)
        {
            try
            {
                await PerCoreUsageUpdateLoopInner(dispatcher);
            }
            catch (Exception ex)
            {
                WindowLogger.Write($"Error occurred in CoreUsage loop: {ex}");
            }
        }

        private static async Task PerCoreUsageUpdateLoopInner(Dispatcher dispatcher)
        {
            // Create the Utility% counters for each logical processor
            PerformanceCounter[] utilityCounters = new PerformanceCounter[coreUsages.Count];

            for (int i = 0; i < utilityCounters.Length; ++i)
            {
                utilityCounters[i] = new("Processor Information", "% Processor Utility", $"0,{i}");
            }

            // Create the Parking Status counters for each logical processor
            // We are making the assumption here that every logical processor will have a Parking Status counter.
            // If none or only some of the processors have a Parking Status, the parkingCounters will be set to null and they will not be checked
            PerformanceCounter[]? parkingCounters = new PerformanceCounter[coreUsages.Count];
            try
            {
                for (int i = 0; i < parkingCounters.Length; ++i)
                {
                    parkingCounters[i] = new("Processor Information", "Parking Status", $"0,{i}");
                }
            }
            catch (Exception)
            {
                // Parking counters may not exist on this system; leave null
                parkingCounters = null;
            }

            float[] utilityValues = new float[coreUsages.Count];
            bool[] parkedValues = new bool[coreUsages.Count];
            while (true)
            {
                // Pause the core usage sampling while minimized to the system tray or taskbar, to reduce CPU usage in the background
                bool windowIsVisible = await dispatcher.InvokeAsync(() =>
                    App.Current.MainWindow.Visibility == Visibility.Visible
                    && App.Current.MainWindow.WindowState != WindowState.Minimized);

                if (windowIsVisible)
                {
                    for (int i = 0; i < coreUsages.Count; ++i)
                    {
                        // Get the Utility% of each logical processor, and clamp it between 0.0-1.0
                        utilityValues[i] = Math.Clamp(utilityCounters[i].NextValue() / 100f, 0f, 1f);

                        // Get the Parking Status of each logical processor. 1.0 is parked, 0.0 is not parked.
                        if (parkingCounters is not null)
                            parkedValues[i] = parkingCounters[i].NextValue() > 0.5f; // treat >0.5 as parked
                        else
                            parkedValues[i] = false;
                    }

                    // Apply the new values on the dispatcher to make sure changes are done in the same UI frame
                    await dispatcher.InvokeAsync(() =>
                    {
                        float total = 0;
                        for (int i = 0; i < coreUsages.Count; ++i)
                        {
                            coreUsages[i].Utility = utilityValues[i];
                            coreUsages[i].IsParked = parkedValues[i];
                            total += utilityValues[i];
                        }

                        // Update the total on the control instance
                        if (_instance is { } control)
                            control.TotalUsageStr = $"{total / coreUsages.Count * 100:F0}%";
                    });
                }

                await Task.Delay(1500);
            }
        }
    }

    /// <summary>
    /// A named group of logical processor heat cells, e.g. the P-Cores / E-Cores of a hybrid CPU
    /// or a single CCD on AMD
    /// </summary>
    public class CoreUsageGroup
    {
        public string Name { get; }
        public IReadOnlyList<CoreUsage> Cores { get; }

        /// <summary>Whether a vertical divider line is drawn after this group (false for the last group)</summary>
        public bool ShowSeparator { get; set; } = true;

        public CoreUsageGroup(string name, IReadOnlyList<CoreUsage> cores)
        {
            Name = name;
            Cores = cores;
        }
    }
}
