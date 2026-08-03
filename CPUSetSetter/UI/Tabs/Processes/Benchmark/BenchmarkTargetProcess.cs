namespace CPUSetSetter.UI.Tabs.Processes.Benchmark
{
    /// <summary>
    /// A selectable target process for the benchmark, from the running processes list
    /// </summary>
    public class BenchmarkTargetProcess
    {
        public uint Pid { get; }
        public string Name { get; }
        public string ImagePath { get; }

        public string DisplayName => string.IsNullOrEmpty(ImagePath)
            ? $"{Name} (PID {Pid})"
            : $"{Name} (PID {Pid})";

        public BenchmarkTargetProcess(uint pid, string name, string imagePath)
        {
            Pid = pid;
            Name = name;
            ImagePath = imagePath;
        }
    }
}
