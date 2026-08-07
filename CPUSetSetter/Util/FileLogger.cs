using System;
using System.IO;
using System.Text;


namespace CPUSetSetter.Util
{
    /// <summary>
    /// Writes goings-on and crash information to a text file on disk, so issues can be diagnosed
    /// after the fact even when the in-app Log panel is not visible (e.g. when running in the tray).
    /// Mirrors the messages shown in the Log panel and also records unhandled exceptions.
    /// Writing is best-effort and must never crash the app.
    /// </summary>
    public static class FileLogger
    {
        private static readonly Lock @lock = new();

        /// <summary>
        /// Path of the log file on disk. Stored under %APPDATA% so it works even when the app runs elevated
        /// </summary>
        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CPUSetSetter",
            "log.txt");

        /// <summary>
        /// Ensure the log directory exists. Called once at startup
        /// </summary>
        public static void Initialize()
        {
            try
            {
                string? dir = Path.GetDirectoryName(LogPath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                // Logging is best-effort
            }
        }

        /// <summary>
        /// Append an ordinary log message to the file, prefixed with a timestamp
        /// </summary>
        public static void Write(string message)
        {
            AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}");
        }

        /// <summary>
        /// Record an unhandled exception and its stack trace to the file
        /// </summary>
        public static void WriteException(string kind, Exception exception)
        {
            AppendLine($"==================================================================");
            AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  Has been {kind}");
            AppendLine($"  Type:    {exception.GetType().FullName}");
            AppendLine($"  Message: {exception.Message}");
            AppendLine($"  Stack:");
            AppendLine(exception.StackTrace ?? "  (none)");
            AppendLine($"==================================================================");
        }

        private static void AppendLine(string line)
        {
            try
            {
                lock (@lock)
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // Logging must never crash the app
            }
        }
    }
}