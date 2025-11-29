using System;
using System.Configuration;
using System.IO;
using System.Text;

namespace SizerDataCollector
{
	public static class Logger
	{
		private static readonly object _lock = new object();
		private static readonly string _logDirectory;
		private static readonly string _logFileBaseName = "SizerCollector";

		static Logger()
		{
			string configuredDir = ConfigurationManager.AppSettings["LogDirectory"];

			if (string.IsNullOrWhiteSpace(configuredDir))
			{
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
				Console.WriteLine("Logger: Failed to create log directory: " + ex.Message);
			}
		}

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
				// Ignore logging errors.
			}
		}

		private static string GetLogFilePath()
		{
			string datePart = DateTime.Now.ToString("yyyyMMdd");
			string fileName = $"{_logFileBaseName}_{datePart}.log";
			return Path.Combine(_logDirectory, fileName);
		}
	}
}

