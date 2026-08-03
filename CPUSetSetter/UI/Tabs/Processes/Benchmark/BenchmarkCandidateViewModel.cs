using CommunityToolkit.Mvvm.ComponentModel;
using CPUSetSetter.Config.Models;

namespace CPUSetSetter.UI.Tabs.Processes.Benchmark
{
    /// <summary>
    /// A mask candidate that can be included in (or excluded from) the benchmark
    /// </summary>
    public partial class BenchmarkCandidateViewModel : ObservableObject
    {
        public LogicalProcessorMask Mask { get; }

        public string DisplayName => Mask.MaskType == MaskApplyType.NoMask
            ? "No mask (baseline)"
            : Mask.DisplayName;

        [ObservableProperty]
        private bool _isSelected;

        public BenchmarkCandidateViewModel(LogicalProcessorMask mask, bool isSelected = false)
        {
            Mask = mask;
            _isSelected = isSelected;
        }
    }
}
