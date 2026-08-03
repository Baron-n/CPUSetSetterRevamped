using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace CPUSetSetter.UI.Tabs.Processes.Benchmark
{
    public enum BenchmarkStatus
    {
        Completed,
        ProcessExited,
        FailedToApply
    }

    /// <summary>
    /// A row in the benchmark results table
    /// </summary>
    public partial class BenchmarkResultEntry : ObservableObject
    {
        public string MaskDisplayName { get; }

        /// <summary>Average CPU usage percentage, null when the run did not complete</summary>
        public double? AverageCpuPercent { get; }

        public string BusiestCores { get; }

        public BenchmarkStatus Status { get; }

        public int DurationSeconds { get; }

        /// <summary>Score bar length relative to the worst (highest) completed result, 0..1</summary>
        [ObservableProperty]
        private double _barRatio;

        /// <summary>Score bar color: green for the best (lowest usage), red for the worst</summary>
        [ObservableProperty]
        private Brush _barBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));

        public string AverageCpuPercentStr => AverageCpuPercent is null ? "-" : $"{AverageCpuPercent:F1}%";

        public string StatusStr => Status switch
        {
            BenchmarkStatus.Completed => "Completed",
            BenchmarkStatus.ProcessExited => "Process exited",
            _ => "Failed to apply",
        };

        public BenchmarkResultEntry(string maskDisplayName, double? averageCpuPercent, string busiestCores, BenchmarkStatus status, int durationSeconds)
        {
            MaskDisplayName = maskDisplayName;
            AverageCpuPercent = averageCpuPercent;
            BusiestCores = busiestCores;
            Status = status;
            DurationSeconds = durationSeconds;
        }
    }
}
