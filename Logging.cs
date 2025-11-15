using System;
using System.Configuration;
using System.IO;
using System.Text;

namespace SizerDataCollector
{
    internal static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logDirectory;
        private static readonly string _logFileBaseName = "SizerCollector";

        static Logger()
        {
            // Get log directory from App.config
            string configuredDir = ConfigurationManager.AppSettings["LogDirectory"];

            if (string.IsNullOrWhiteSpace(configuredDir))
            {
                // Default: create a "logs" folder next to the EXE
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                _logDirectory = Path.Combine(exeDir, "logs");
            }
            else
            {
                _logDirectory = configuredDir;
            }

            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch (Exception ex)
            {
                // If we can't create the directory, there's not much we can do.
                // Fallback: write to console.
                Console.WriteLine("Logger: Failed to create log directory: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes a line to the log file with timestamp and optional exception details.
        /// </summary>
        public static void Log(string message, Exception ex = null)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var sb = new StringBuilder();
            sb.Append('[').Append(timestamp).Append("] ").Append(message);

            if (ex != null)
            {
                sb.AppendLine();
                sb.Append("Exception: ").Append(ex.ToString());
            }

            string line = sb.ToString();

            // Write to console as well (handy while you're still running it manually)
            Console.WriteLine(line);

            try
            {
                string logFilePath = GetLogFilePath();
                lock (_lock)
                {
                    File.AppendAllText(logFilePath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Swallow logging errors to avoid crashing the app because of a file problem.
            }
        }

        private static string GetLogFilePath()
        {
            // One log file per day: SizerCollector_YYYYMMDD.log
            string datePart = DateTime.Now.ToString("yyyyMMdd");
            string fileName = $"{_logFileBaseName}_{datePart}.log";
            return Path.Combine(_logDirectory, fileName);
        }
    }
}
