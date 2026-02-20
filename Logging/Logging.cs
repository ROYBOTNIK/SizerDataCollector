using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using SizerDataCollector.Core.Config;

namespace SizerDataCollector.Core.Logging
{
	public enum LogLevel
	{
		Trace = 0,
		Debug = 1,
		Info = 2,
		Warn = 3,
		Error = 4
	}

	public static class Logger
	{
		private static readonly object _lock = new object();
		private static readonly string _logDirectory;
		private static readonly string _logFileBaseName = "SizerCollector";
		private static LogLevel _minimumLevel = LogLevel.Info;
		private static bool _logAsJson;
		private static bool _diagnosticMode;
		private static DateTimeOffset? _diagnosticUntilUtc;
		private static long _logMaxFileBytes = 10L * 1024L * 1024L;
		private static int _logRetentionDays = 14;
		private static int _logMaxFiles = 100;
		private static DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;

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

			_minimumLevel = ParseLogLevel(ConfigurationManager.AppSettings["LogLevel"], LogLevel.Info);
			_logAsJson = ParseBool(ConfigurationManager.AppSettings["LogAsJson"], false);
			_diagnosticMode = ParseBool(ConfigurationManager.AppSettings["DiagnosticMode"], false);
			_diagnosticUntilUtc = ParseDateTimeOffset(ConfigurationManager.AppSettings["DiagnosticUntilUtc"]);
			_logMaxFileBytes = ParseLong(ConfigurationManager.AppSettings["LogMaxFileBytes"], _logMaxFileBytes, 1024L);
			_logRetentionDays = ParseInt(ConfigurationManager.AppSettings["LogRetentionDays"], _logRetentionDays, 1);
			_logMaxFiles = ParseInt(ConfigurationManager.AppSettings["LogMaxFiles"], _logMaxFiles, 1);

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

		public static void Configure(CollectorRuntimeSettings settings)
		{
			if (settings == null)
			{
				return;
			}

			lock (_lock)
			{
				_minimumLevel = ParseLogLevel(settings.LogLevel, _minimumLevel);
				_logAsJson = settings.LogAsJson;
				_diagnosticMode = settings.DiagnosticMode;
				_diagnosticUntilUtc = settings.DiagnosticUntilUtc;
				_logMaxFileBytes = settings.LogMaxFileBytes >= 1024 ? settings.LogMaxFileBytes : _logMaxFileBytes;
				_logRetentionDays = settings.LogRetentionDays >= 1 ? settings.LogRetentionDays : _logRetentionDays;
				_logMaxFiles = settings.LogMaxFiles >= 1 ? settings.LogMaxFiles : _logMaxFiles;
			}
		}

		public static void Log(string message, Exception ex = null, LogLevel level = LogLevel.Info)
		{
			if (!ShouldLog(level))
			{
				return;
			}

			string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
			string line;
			if (_logAsJson)
			{
				line = JsonConvert.SerializeObject(new
				{
					ts = timestamp,
					level = level.ToString().ToUpperInvariant(),
					message = message,
					exception = ex?.ToString()
				});
			}
			else
			{
				var sb = new StringBuilder();
				sb.Append('[').Append(timestamp).Append("] [").Append(level.ToString().ToUpperInvariant()).Append("] ").Append(message);

				if (ex != null)
				{
					sb.AppendLine();
					sb.Append("Exception: ").Append(ex.ToString());
				}

				line = sb.ToString();
			}

			Console.WriteLine(line);

			try
			{
				string logFilePath = GetWritableLogFilePath();
				lock (_lock)
				{
					File.AppendAllText(logFilePath, line + Environment.NewLine);
					RunRetentionIfDue();
				}
			}
			catch
			{
				// Ignore logging errors.
			}
		}

		private static string GetWritableLogFilePath()
		{
			var datePart = DateTime.Now.ToString("yyyyMMdd");
			var index = 0;
			while (true)
			{
				var suffix = index == 0 ? string.Empty : "_" + index;
				var fileName = $"{_logFileBaseName}_{datePart}{suffix}.log";
				var path = Path.Combine(_logDirectory, fileName);
				if (!File.Exists(path))
				{
					return path;
				}

				var info = new FileInfo(path);
				if (info.Length < _logMaxFileBytes)
				{
					return path;
				}

				index++;
			}
		}

		private static bool ShouldLog(LogLevel level)
		{
			var threshold = _minimumLevel;
			if (_diagnosticMode)
			{
				if (!_diagnosticUntilUtc.HasValue || DateTimeOffset.UtcNow <= _diagnosticUntilUtc.Value)
				{
					threshold = LogLevel.Debug;
				}
			}

			return level >= threshold;
		}

		private static LogLevel ParseLogLevel(string value, LogLevel fallback)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return fallback;
			}

			LogLevel parsed;
			if (Enum.TryParse(value.Trim(), true, out parsed))
			{
				return parsed;
			}

			return fallback;
		}

		private static bool ParseBool(string value, bool fallback)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return fallback;
			}

			bool parsed;
			if (bool.TryParse(value, out parsed))
			{
				return parsed;
			}

			return fallback;
		}

		private static DateTimeOffset? ParseDateTimeOffset(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			DateTimeOffset parsed;
			if (DateTimeOffset.TryParse(value, out parsed))
			{
				return parsed;
			}

			return null;
		}

		private static long ParseLong(string value, long fallback, long minimum)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return fallback;
			}

			long parsed;
			if (long.TryParse(value, out parsed) && parsed >= minimum)
			{
				return parsed;
			}

			return fallback;
		}

		private static int ParseInt(string value, int fallback, int minimum)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return fallback;
			}

			int parsed;
			if (int.TryParse(value, out parsed) && parsed >= minimum)
			{
				return parsed;
			}

			return fallback;
		}

		private static void RunRetentionIfDue()
		{
			var now = DateTimeOffset.UtcNow;
			if ((now - _lastCleanupUtc).TotalMinutes < 5)
			{
				return;
			}

			_lastCleanupUtc = now;
			try
			{
				var files = new DirectoryInfo(_logDirectory)
					.GetFiles($"{_logFileBaseName}_*.log")
					.OrderByDescending(f => f.LastWriteTimeUtc)
					.ToList();

				var cutoff = now.UtcDateTime.AddDays(-_logRetentionDays);
				foreach (var stale in files.Where(f => f.LastWriteTimeUtc < cutoff).ToList())
				{
					stale.Delete();
				}

				files = new DirectoryInfo(_logDirectory)
					.GetFiles($"{_logFileBaseName}_*.log")
					.OrderByDescending(f => f.LastWriteTimeUtc)
					.ToList();

				foreach (var overflow in files.Skip(_logMaxFiles))
				{
					overflow.Delete();
				}
			}
			catch
			{
				// Keep logging resilient even if retention fails.
			}
		}
	}
}

