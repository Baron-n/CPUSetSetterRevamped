using CommunityToolkit.Mvvm.ComponentModel;


namespace CPUSetSetter.UI.Tabs.Processes.CoreUsage
{
    /// <summary>
    /// Represents the utility and parking state of a single logical processor.
    /// </summary>
    public partial class CoreUsage : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(UtilityStr))]
        [NotifyPropertyChangedFor(nameof(TooltipStr))]
        private double _utility;

        [ObservableProperty]
        private bool _isParked;

        public string Name { get; }

        public string UtilityStr => $"{Utility * 100:F0}%";

        public string TooltipStr => $"{Name}: {Utility * 100:F0}%";

        public CoreUsage(string cpuName)
        {
            _utility = 0;
            _isParked = false;
            Name = cpuName;
        }
    }
}
