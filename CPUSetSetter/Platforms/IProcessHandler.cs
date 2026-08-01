using CPUSetSetter.Config.Models;


namespace CPUSetSetter.Platforms
{
    public interface IProcessHandler : IDisposable
    {
        /// <summary>
        /// Get the average CPU usage of the process of the recent past (~30 seconds)
        /// </summary>
        /// <returns>Between 0 and 1 on success. -1 on fail</returns>
        double GetAverageCpuUsage();

        /// <summary>
        /// Get the CPU usage of the process attributed to each logical processor, sampled since the previous call
        /// </summary>
        /// <returns>An array of length <see cref="CpuInfo.LogicalProcessorCount"/> with fractions between 0 and 1 per logical processor. Null on fail</returns>
        double[]? GetPerCoreCpuUsage();
        bool ApplyMask(LogicalProcessorMask mask);
        bool ReapplyMask(LogicalProcessorMask mask);

        /// <summary>
        /// Read back the restriction that is actually applied to this process (CPU Set IDs or Affinity), read live from the OS.
        /// Returns null if the process is not accessible
        /// </summary>
        string? GetCurrentRestrictionInfo();
    }
}
