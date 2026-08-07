using CommunityToolkit.Mvvm.ComponentModel;
using CPUSetSetter.Util;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;


namespace CPUSetSetter.UI.Tabs.Processes
{
    /// <summary>
    /// Collects log messages and renders them into a <see cref="FlowDocument"/>, coloring
    /// error and warning lines so failures stand out. The full log history is always
    /// rebuilt from the queue, so no messages are ever lost
    /// </summary>
    public partial class WindowLogger : ObservableObject
    {
        [ObservableProperty]
        private string _text = "";

        /// <summary>
        /// The colored document shown in the Log panel. Updated on the UI thread only
        /// </summary>
        public FlowDocument LogDocument { get; } = new();

        private readonly Queue<string> _logLines = new();
        private readonly Lock _lock = new();
        private bool _isUpdating = false;

        public static WindowLogger Default { get; } = new WindowLogger();

        public static void Write(string message)
        {
            Default.WriteImp(message);
        }

        private void WriteImp(string message)
        {
            using (_lock.EnterScope())
            {
                _logLines.Enqueue(message + "\n");

                // Mirror every message to the on-disk log so history survives restart and tray use
                FileLogger.Write(message);

                // Begin updating the logger in the UI
                // A small delay is used before updating, so multiple logs can be rendered in one go
                if (!_isUpdating)
                {
                    _isUpdating = true;
                    Task.Run(UpdateText);
                }
            }
        }

        private async Task UpdateText()
        {
            await Task.Delay(30);

            using (_lock.EnterScope())
            {
                while (_logLines.Count > 500)
                {
                    _logLines.Dequeue();
                }

                _isUpdating = false;
            }

            // Rebuild the colored document on the UI thread, reading the current queue there so
            // the rendered content is always the latest history
            try
            {
                _ = Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    string[] lines;
                    using (_lock.EnterScope())
                        lines = _logLines.ToArray();

                    Text = string.Join("", lines);

                    Brush warningBrush = ResolveBrush("LogWarningBrush", Color.FromRgb(0xFB, 0xBF, 0x24));
                    Brush errorBrush = ResolveBrush("LogErrorBrush", Color.FromRgb(0xF8, 0x71, 0x71));

                    LogDocument.Blocks.Clear();
                    foreach (string line in lines)
                    {
                        string text = line.TrimEnd('\n', '\r');
                        Run run = new(text);
                        if (Classify(text, warningBrush, errorBrush) is { } brush)
                            run.Foreground = brush;
                        Paragraph paragraph = new(run)
                        {
                            Margin = new Thickness(0),
                        };
                        LogDocument.Blocks.Add(paragraph);
                    }
                }));
            }
            catch (Exception)
            {
                // The app may be shutting down; nothing more to render
            }
        }

        /// <summary>
        /// Pick a color for a log message based on its wording: failures show red, warnings amber,
        /// everything else uses the default text color. Returning null keeps the default foreground
        /// </summary>
        private static Brush? Classify(string line, Brush warning, Brush error)
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith("ERROR:", StringComparison.Ordinal)
                || trimmed.StartsWith("Failed to", StringComparison.Ordinal)
                || trimmed.StartsWith("Unable to", StringComparison.Ordinal)
                || trimmed.StartsWith("Could not", StringComparison.Ordinal)
                || trimmed.Contains("Uncaught exception", StringComparison.Ordinal))
                return error;

            if (trimmed.StartsWith("WARNING:", StringComparison.Ordinal))
                return warning;

            return null;
        }

        private static Brush ResolveBrush(string key, Color fallback)
        {
            object? value = Application.Current?.TryFindResource(key);
            return value as Brush ?? new SolidColorBrush(fallback);
        }
    }
}
