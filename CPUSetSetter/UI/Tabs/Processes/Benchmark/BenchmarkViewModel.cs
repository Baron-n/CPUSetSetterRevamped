using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPUSetSetter.Config.Models;
using CPUSetSetter.Platforms;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CPUSetSetter.UI.Tabs.Processes.Benchmark
{
    public partial class BenchmarkViewModel : ObservableObject
    {
        public ObservableCollection<BenchmarkTargetProcess> Targets { get; } = [];
        public ObservableCollection<BenchmarkCandidateViewModel> Candidates { get; } = [];
        public ObservableCollection<BenchmarkResultEntry> Results { get; } = [];

        [ObservableProperty]
        private BenchmarkTargetProcess? _selectedTarget;

        [ObservableProperty]
        private string _targetFilter = "";

        [ObservableProperty]
        private string _customTargetPath = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        private string _durationText = "10";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCommand))]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        [NotifyPropertyChangedFor(nameof(CanStart))]
        private bool _isRunning;

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private string _countdownText = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
        [NotifyPropertyChangedFor(nameof(CanStart))]
        private double _progress;

        private CancellationTokenSource? _cts;

        public ICollectionView ResultsView { get; }
        public ICollectionView TargetsView { get; }

        public bool CanStart => !IsRunning && SelectedTarget is not null && Candidates.Any(candidate => candidate.IsSelected);
        public bool CanCancel => IsRunning;
        public bool CanExport => Results.Count > 0;

        public BenchmarkViewModel()
        {
            // Snapshot the currently running processes as selectable targets
            RefreshTargets();

            // Filter the target list by name or path as the user types, so long lists are easy to search
            TargetsView = CollectionViewSource.GetDefaultView(Targets);
            TargetsView.Filter = item => ((BenchmarkTargetProcess)item).Name.Contains(TargetFilter, StringComparison.OrdinalIgnoreCase)
                || ((BenchmarkTargetProcess)item).ImagePath.Contains(TargetFilter, StringComparison.OrdinalIgnoreCase);

            // Build the candidate list from the user's masks (NoMask is at index 0)
            List<string> defaultMaskNames = CpuInfo.DefaultLogicalProcessorMasks.Select(defaultMask => defaultMask.name).ToList();
            foreach (LogicalProcessorMask mask in AppConfig.Instance.LogicalProcessorMasks)
            {
                bool isDefault = defaultMaskNames.Contains(mask.Name);
                Candidates.Add(new(mask, mask.MaskType == MaskApplyType.NoMask || isDefault));
            }

            foreach (BenchmarkCandidateViewModel candidate in Candidates)
            {
                candidate.PropertyChanged += OnCandidatePropertyChanged;
            }

            ResultsView = (ListCollectionView)CollectionViewSource.GetDefaultView(Results);
            ResultsView.SortDescriptions.Add(new(nameof(BenchmarkResultEntry.AverageCpuPercent), ListSortDirection.Ascending));
        }

        /// <summary>
        /// Re-snapshot the running processes as selectable targets, keeping the current selection if it still exists.
        /// Call when the Benchmark tab is shown, so the list is never stale
        /// </summary>
        public void RefreshTargets()
        {
            uint? selectedPid = SelectedTarget?.Pid;
            Targets.Clear();
            foreach (ProcessListEntryViewModel process in ProcessesTabViewModel.RunningProcesses)
            {
                Targets.Add(new(process.Pid, process.Name, process.ImagePath));
            }
            SelectedTarget = selectedPid is null ? null : Targets.FirstOrDefault(target => target.Pid == selectedPid.Value);
        }

        private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BenchmarkCandidateViewModel.IsSelected))
                StartCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedTargetChanged(BenchmarkTargetProcess? value)
        {
            StartCommand.NotifyCanExecuteChanged();
        }

        partial void OnTargetFilterChanged(string value)
        {
            TargetsView.Refresh();
        }

        [RelayCommand]
        private void BrowseTarget()
        {
            OpenFileDialog dialog = new() { Filter = "Executable files (*.exe)|*.exe" };
            if (dialog.ShowDialog() != true)
                return;

            CustomTargetPath = dialog.FileName;

            // If the picked executable is currently running, select it as the target right away
            BenchmarkTargetProcess? match = Targets.FirstOrDefault(target => string.Equals(target.ImagePath, dialog.FileName, StringComparison.OrdinalIgnoreCase));
            SelectedTarget = match;
            if (match is not null)
                StatusText = $"Target: {match.DisplayName}";
            else
                StatusText = $"'{dialog.FileName}' is not running. Start it first, then press Start.";
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        private async Task Start()
        {
            if (SelectedTarget is null)
                return;

            if (!int.TryParse(DurationText, out int duration) || duration is < 3 or > 120)
            {
                StatusText = "Duration must be a whole number between 3 and 120 seconds.";
                return;
            }

            List<BenchmarkCandidateViewModel> selected = Candidates.Where(candidate => candidate.IsSelected).ToList();
            if (selected.Count == 0)
                return;

            IsRunning = true;
            Results.Clear();
            Progress = 0;
            CountdownText = "";
            _cts = new();

            // Find the matching process entry, so its AutoReapply can be paused during the run to avoid interference
            ProcessListEntryViewModel? processEntry = ProcessesTabViewModel.RunningProcesses.FirstOrDefault(process => process.Pid == SelectedTarget.Pid);
            bool autoReapplyWasOn = processEntry?.AutoReapply ?? false;
            if (processEntry is not null)
                processEntry.AutoReapply = false;

            try
            {
                await RunBenchmarkAsync(SelectedTarget, selected, duration, _cts.Token);
                if (!_cts.IsCancellationRequested)
                    OfferBestMask(processEntry);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Benchmark cancelled. The original mask was restored.";
            }
            finally
            {
                if (processEntry is not null)
                    processEntry.AutoReapply = autoReapplyWasOn;
                _cts.Dispose();
                _cts = null;
                IsRunning = false;
                CountdownText = "";
                Progress = 0;
                CancelCommand.NotifyCanExecuteChanged();
                ExportCsvCommand.NotifyCanExecuteChanged();
            }
        }

        private async Task RunBenchmarkAsync(BenchmarkTargetProcess target, List<BenchmarkCandidateViewModel> selected, int duration, CancellationToken ct)
        {
            // Create a handler to capture the process' original mask, so it can be restored at the end
            using IProcessHandler? originalHandler = ProcessEvents.CreateProcessHandler(target.Name, target.Pid);
            if (originalHandler is null)
            {
                StatusText = $"Could not open process '{target.Name}'. It may have exited or access is denied.";
                return;
            }

            LogicalProcessorMask originalMask = originalHandler.GetCurrentMask() ?? LogicalProcessorMask.NoMask;
            bool ownsOriginalMask = !ReferenceEquals(originalMask, LogicalProcessorMask.NoMask);

            try
            {
                for (int i = 0; i < selected.Count; ++i)
                {
                    ct.ThrowIfCancellationRequested();
                    BenchmarkCandidateViewModel candidate = selected[i];
                    StatusText = $"Testing '{candidate.DisplayName}' ({i + 1}/{selected.Count})...";
                    CountdownText = "";

                    // A fresh handler per candidate gives each window a clean CPU sampling buffer
                    using IProcessHandler? handler = ProcessEvents.CreateProcessHandler(target.Name, target.Pid);
                    if (handler is null)
                    {
                        Results.Add(new(candidate.DisplayName, null, "", BenchmarkStatus.FailedToApply, duration));
                        RefreshResultBars();
                        continue;
                    }

                    if (!handler.ApplyMask(candidate.Mask))
                    {
                        Results.Add(new(candidate.DisplayName, null, "", BenchmarkStatus.FailedToApply, duration));
                        RefreshResultBars();
                        continue;
                    }

                    // Warm up the per-core sampler (its first call returns zeros)
                    handler.GetPerCoreCpuUsage();

                    var avgSamples = new List<double>();
                    var perCoreSums = new double[CpuInfo.LogicalProcessorCount];
                    int perCoreCount = 0;
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    bool exited = false;

                    while (stopwatch.Elapsed.TotalSeconds < duration)
                    {
                        ct.ThrowIfCancellationRequested();

                        double avgCpu = handler.GetAverageCpuUsage();
                        if (avgCpu < 0)
                        {
                            exited = true;
                            break;
                        }
                        avgSamples.Add(avgCpu);

                        double[]? perCore = handler.GetPerCoreCpuUsage();
                        if (perCore is not null)
                        {
                            for (int core = 0; core < perCore.Length; ++core)
                                perCoreSums[core] += perCore[core];
                            ++perCoreCount;
                        }

                        Progress = (i + stopwatch.Elapsed.TotalSeconds / duration) / selected.Count;
                        CountdownText = $"{(int)Math.Ceiling(duration - stopwatch.Elapsed.TotalSeconds)}s left";
                        await Task.Delay(500, ct);
                    }

                    if (exited)
                    {
                        Results.Add(new(candidate.DisplayName, null, "", BenchmarkStatus.ProcessExited, duration));
                        RefreshResultBars();
                        continue;
                    }

                    double averageCpuPercent = avgSamples.Count > 0 ? avgSamples.Average() * 100 : 0;
                    string busiestCores = FormatBusiestCores(perCoreSums, perCoreCount);
                    Results.Add(new(candidate.DisplayName, averageCpuPercent, busiestCores, BenchmarkStatus.Completed, duration));
                    RefreshResultBars();
                }
            }
            finally
            {
                // Restore the original mask on every exit path (including cancellation)
                using IProcessHandler? restoreHandler = ProcessEvents.CreateProcessHandler(target.Name, target.Pid);
                if (restoreHandler is not null)
                {
                    bool restored = restoreHandler.ApplyMask(originalMask);
                    if (!restored)
                        WindowLogger.Write($"WARNING: Could not restore the original mask of '{target.Name}' after the benchmark");
                }
                if (ownsOriginalMask)
                    originalMask.Dispose();
            }
        }

        /// <summary>
        /// After the benchmark (and its mask restore), ask the user whether to apply the best-performing mask to the
        /// target process. Lower average CPU usage is considered better. Applied via the process entry so it behaves
        /// exactly like picking the mask in the process list (rule is created or updated too)
        /// </summary>
        private void OfferBestMask(ProcessListEntryViewModel? processEntry)
        {
            if (processEntry is null)
            {
                StatusText = "Benchmark complete. The original mask was restored.";
                return;
            }

            BenchmarkResultEntry? best = Results
                .Where(result => result.Status == BenchmarkStatus.Completed && result.AverageCpuPercent is not null)
                .OrderBy(result => result.AverageCpuPercent)
                .FirstOrDefault();
            BenchmarkCandidateViewModel? bestCandidate = best is null
                ? null
                : Candidates.FirstOrDefault(candidate => candidate.DisplayName == best.MaskDisplayName);

            if (best is null || bestCandidate is null)
            {
                StatusText = "Benchmark complete. The original mask was restored.";
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                $"The best mask was '{bestCandidate.DisplayName}' ({best.AverageCpuPercent!.Value:F1}% average CPU).\n\nApply it to '{processEntry.Name}' now?",
                "Benchmark complete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes)
            {
                StatusText = $"Benchmark complete. Best mask '{bestCandidate.DisplayName}' was not applied.";
                return;
            }

            processEntry.SetMask(bestCandidate.Mask, true);
            StatusText = $"Applied best mask '{bestCandidate.DisplayName}' to '{processEntry.Name}'.";
        }

        private static string FormatBusiestCores(double[] perCoreSums, int perCoreCount)
        {
            if (perCoreCount == 0)
                return "";

            double[] means = perCoreSums.Select(sum => sum / perCoreCount).ToArray();
            IEnumerable<string> busiest = CpuInfo.LogicalProcessorNames
                .Select((name, index) => (name, index))
                .OrderByDescending(core => means[core.index])
                .Take(3)
                .Where(core => means[core.index] > 0.01)
                .Select(core => $"{core.name} {means[core.index] * 100:F0}%");
            return string.Join("  ·  ", busiest);
        }

        /// <summary>
        /// Compute each completed result's score bar: length proportional to the average CPU usage relative to the
        /// worst (highest) completed result, and color interpolated green (best) to red (worst) across the results
        /// </summary>
        private void RefreshResultBars()
        {
            double[] averages = Results
                .Where(result => result.AverageCpuPercent is not null)
                .Select(result => result.AverageCpuPercent!.Value)
                .ToArray();

            double maxAvg = averages.Length > 0 ? averages.Max() : 0;
            double minAvg = averages.Length > 0 ? averages.Min() : 0;
            double range = maxAvg - minAvg;

            foreach (BenchmarkResultEntry result in Results)
            {
                if (result.AverageCpuPercent is not double avg || averages.Length == 0)
                {
                    result.BarRatio = 0;
                    result.BarBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
                    result.BarBrush.Freeze();
                    continue;
                }

                result.BarRatio = maxAvg > 0 ? avg / maxAvg : 0;
                // Normalize over the observed range so the best result is pure green and the worst pure red
                double t = range > 0 ? (avg - minAvg) / range : 0;
                byte r = (byte)(0x3C + (0xE5 - 0x3C) * t);
                byte g = (byte)(0xB0 - (0xB0 - 0x3B) * t);
                byte b = (byte)(0x40 + (0x3B - 0x40) * t);
                result.BarBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
                result.BarBrush.Freeze();
            }
        }

        [RelayCommand(CanExecute = nameof(CanCancel))]
        private void Cancel()
        {
            _cts?.Cancel();
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        private void ExportCsv()
        {
            if (Results.Count == 0)
                return;

            SaveFileDialog dialog = new()
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"benchmark_{SelectedTarget?.Name ?? "process"}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };
            if (dialog.ShowDialog() != true)
                return;

            string targetName = SelectedTarget?.Name ?? "";
            var builder = new StringBuilder();
            builder.AppendLine("Mask,AverageCpuPercent,BusiestCores,Status,DurationSeconds,TargetProcess");
            foreach (BenchmarkResultEntry result in Results.OrderBy(result => result.AverageCpuPercent ?? double.MaxValue))
            {
                string avg = result.AverageCpuPercent is null
                    ? ""
                    : result.AverageCpuPercent.Value.ToString("F2", CultureInfo.InvariantCulture);
                builder.AppendLine($"{CsvEscape(result.MaskDisplayName)},{avg},{CsvEscape(result.BusiestCores)},{result.StatusStr},{result.DurationSeconds},{CsvEscape(targetName)}");
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            StatusText = $"Results exported to {dialog.FileName}";
        }

        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
