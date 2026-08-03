using CPUSetSetter.Config.Models;


namespace CPUSetSetter.Platforms
{
    /// <summary>
    /// Provides information on the system's CPU. It analyzes the core/die structure of the CPU.
    /// It uses this information to provide a list of names for each logical processor, and a collection of default masks that may be common in use.
    /// </summary>
    public static class CpuInfo
    {
        public static Manufacturer Manufacturer => Default.Manufacturer;

        public static IReadOnlyList<string> LogicalProcessorNames => Default.LogicalProcessorNames;

        public static int LogicalProcessorCount { get; } = Default.LogicalProcessorNames.Count;

        public static IReadOnlyList<(string name, List<bool> boolMask)> DefaultLogicalProcessorMasks => Default.DefaultLogicalProcessorMasks;

        public static IReadOnlyList<LogicalProcessorTopologyInfo> LogicalProcessorTopology => Default.LogicalProcessorTopology;

        public static bool IsSupported => Default.IsSupported;

        public static bool DieDetectionFailed => Default.DieDetectionFailed;


        private static ICpuInfo? _default;

#if WINDOWS
        public static ICpuInfo Default => _default ??= new CpuInfoWindows();
#endif
    }

    public interface ICpuInfo
    {
        Manufacturer Manufacturer { get; }
        IReadOnlyList<string> LogicalProcessorNames { get; }
        IReadOnlyList<LogicalProcessorTopologyInfo> LogicalProcessorTopology { get; }
        IReadOnlyList<(string name, List<bool> boolMask)> DefaultLogicalProcessorMasks { get; }
        bool IsSupported { get; }
        bool DieDetectionFailed { get; }
    }

    /// <summary>
    /// Topology information for a single logical processor: which die/CCX, physical core and SMT thread it belongs to.
    /// </summary>
    public class LogicalProcessorTopologyInfo
    {
        public int LogicalProcessorIndex { get; }

        /// <summary>Index of the die/CCX this logical processor belongs to, or -1 when die detection failed or is not applicable.</summary>
        public int DieIndex { get; }

        /// <summary>Index of the physical core this logical processor belongs to.</summary>
        public int CoreIndex { get; }

        /// <summary>Index of this logical processor within its core (0-based; 1 for the SMT sibling).</summary>
        public int SMTThreadIndex { get; }

        /// <summary>Whether the physical core has more than one thread (Hyper-Threading / SMT).</summary>
        public bool IsSMT { get; }

        /// <summary>
        /// The efficiency class of the physical core this logical processor belongs to (only meaningful on
        /// hybrid CPUs, e.g. Intel P/E cores). A higher number means the core has more performance
        /// </summary>
        public int EfficiencyClass { get; }

        public LogicalProcessorTopologyInfo(int logicalProcessorIndex, int dieIndex, int coreIndex, int smtThreadIndex, bool isSMT, int efficiencyClass)
        {
            LogicalProcessorIndex = logicalProcessorIndex;
            DieIndex = dieIndex;
            CoreIndex = coreIndex;
            SMTThreadIndex = smtThreadIndex;
            IsSMT = isSMT;
            EfficiencyClass = efficiencyClass;
        }
    }

    public enum Manufacturer
    {
        Intel,
        AMD,
        Other
    }

    public class UnsupportedCpu : Exception
    {
        public UnsupportedCpu() { }
        public UnsupportedCpu(string message) : base(message) { }
    }
}
