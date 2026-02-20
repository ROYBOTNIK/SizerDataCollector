using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Config
{
	public sealed class CollectorConfig
	{
		private static readonly IReadOnlyList<string> DefaultEnabledMetrics = Array.AsReadOnly(new[]
		{
			"lanes_grade_fpm",
			"lanes_size_fpm",
			"machine_total_fpm",
			"machine_cupfill",
			"outlets_details"
		});
		private const string DefaultSizerHost = "10.155.155.10";
		private const int DefaultSizerPort = 8001;
		private const int DefaultTimeoutSeconds = 5;
		private const int MinPollIntervalSeconds = 5;
		private const int MinInitialBackoffSeconds = 1;
		private const int DefaultPollIntervalSeconds = 60;
		private const int DefaultInitialBackoffSeconds = 10;
		private const int DefaultMaxBackoffSeconds = 300;
		private const string DefaultLogLevel = "Info";
		private const long DefaultLogMaxFileBytes = 10L * 1024L * 1024L;
		private const int DefaultLogRetentionDays = 14;
		private const int DefaultLogMaxFiles = 100;

		public string SizerHost { get; }
		public int SizerPort { get; }
		public int OpenTimeoutSec { get; }
		public int SendTimeoutSec { get; }
		public int ReceiveTimeoutSec { get; }

		public string TimescaleConnectionString { get; }

		public IReadOnlyList<string> EnabledMetrics { get; }

		public bool EnableIngestion { get; }

		public int PollIntervalSeconds { get; }

		public int InitialBackoffSeconds { get; }

		public int MaxBackoffSeconds { get; }

		public string LogLevel { get; }

		public bool DiagnosticMode { get; }

		public DateTimeOffset? DiagnosticUntilUtc { get; }

		public bool LogAsJson { get; }

		public long LogMaxFileBytes { get; }

		public int LogRetentionDays { get; }

		public int LogMaxFiles { get; }

		public CollectorConfig()
			: this(BuildRuntimeSettingsFromAppConfig())
		{
		}

		public CollectorConfig(CollectorRuntimeSettings runtimeSettings)
		{
			if (runtimeSettings == null)
			{
				throw new ArgumentNullException(nameof(runtimeSettings));
			}

			SizerHost = string.IsNullOrWhiteSpace(runtimeSettings.SizerHost) ? DefaultSizerHost : runtimeSettings.SizerHost;
			SizerPort = runtimeSettings.SizerPort;
			OpenTimeoutSec = runtimeSettings.OpenTimeoutSec;
			SendTimeoutSec = runtimeSettings.SendTimeoutSec;
			ReceiveTimeoutSec = runtimeSettings.ReceiveTimeoutSec;

			TimescaleConnectionString = runtimeSettings.TimescaleConnectionString ?? string.Empty;
			EnabledMetrics = NormalizeEnabledMetrics(runtimeSettings.EnabledMetrics);
			EnableIngestion = runtimeSettings.EnableIngestion;

			var pollInterval = EnsureMinimum("PollIntervalSeconds", runtimeSettings.PollIntervalSeconds, MinPollIntervalSeconds, DefaultPollIntervalSeconds);
			var initialBackoff = EnsureMinimum("InitialBackoffSeconds", runtimeSettings.InitialBackoffSeconds, MinInitialBackoffSeconds, DefaultInitialBackoffSeconds);
			var maxBackoff = EnsureMinimum("MaxBackoffSeconds", runtimeSettings.MaxBackoffSeconds, MinInitialBackoffSeconds, DefaultMaxBackoffSeconds);

			if (maxBackoff < initialBackoff)
			{
				Logger.Log($"CollectorConfig: MaxBackoffSeconds value {maxBackoff} must be greater than or equal to InitialBackoffSeconds value {initialBackoff}. Falling back to defaults ({DefaultInitialBackoffSeconds}/{DefaultMaxBackoffSeconds}).");
				initialBackoff = DefaultInitialBackoffSeconds;
				maxBackoff = DefaultMaxBackoffSeconds;
			}

			PollIntervalSeconds = pollInterval;
			InitialBackoffSeconds = initialBackoff;
			MaxBackoffSeconds = maxBackoff;
			LogLevel = string.IsNullOrWhiteSpace(runtimeSettings.LogLevel) ? DefaultLogLevel : runtimeSettings.LogLevel.Trim();
			DiagnosticMode = runtimeSettings.DiagnosticMode;
			DiagnosticUntilUtc = runtimeSettings.DiagnosticUntilUtc;
			LogAsJson = runtimeSettings.LogAsJson;
			LogMaxFileBytes = EnsureMinimumLong("LogMaxFileBytes", runtimeSettings.LogMaxFileBytes, 1024L, DefaultLogMaxFileBytes);
			LogRetentionDays = EnsureMinimum("LogRetentionDays", runtimeSettings.LogRetentionDays, 1, DefaultLogRetentionDays);
			LogMaxFiles = EnsureMinimum("LogMaxFiles", runtimeSettings.LogMaxFiles, 1, DefaultLogMaxFiles);
		}

		private static CollectorRuntimeSettings BuildRuntimeSettingsFromAppConfig()
		{
			return new CollectorRuntimeSettings
			{
				SizerHost = GetString("SizerHost", DefaultSizerHost),
				SizerPort = GetInt("SizerPort", DefaultSizerPort),
				OpenTimeoutSec = GetInt("OpenTimeoutSec", DefaultTimeoutSeconds),
				SendTimeoutSec = GetInt("SendTimeoutSec", DefaultTimeoutSeconds),
				ReceiveTimeoutSec = GetInt("ReceiveTimeoutSec", DefaultTimeoutSeconds),
				TimescaleConnectionString = GetConnectionString("TimescaleDb"),
				EnabledMetrics = GetEnabledMetrics().ToList(),
				EnableIngestion = GetBool("EnableIngestion", false),
				PollIntervalSeconds = GetIntWithMinimum("PollIntervalSeconds", MinPollIntervalSeconds, DefaultPollIntervalSeconds),
				InitialBackoffSeconds = GetIntWithMinimum("InitialBackoffSeconds", MinInitialBackoffSeconds, DefaultInitialBackoffSeconds),
				MaxBackoffSeconds = GetIntWithMinimum("MaxBackoffSeconds", MinInitialBackoffSeconds, DefaultMaxBackoffSeconds),
				LogLevel = GetString("LogLevel", DefaultLogLevel),
				DiagnosticMode = GetBool("DiagnosticMode", false),
				DiagnosticUntilUtc = GetDateTimeOffset("DiagnosticUntilUtc"),
				LogAsJson = GetBool("LogAsJson", false),
				LogMaxFileBytes = GetLongWithMinimum("LogMaxFileBytes", 1024L, DefaultLogMaxFileBytes),
				LogRetentionDays = GetIntWithMinimum("LogRetentionDays", 1, DefaultLogRetentionDays),
				LogMaxFiles = GetIntWithMinimum("LogMaxFiles", 1, DefaultLogMaxFiles)
			};
		}

		private static string GetString(string key, string defaultValue)
		{
			var value = ConfigurationManager.AppSettings[key];
			return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
		}

		private static int GetInt(string key, int defaultValue)
		{
			var value = ConfigurationManager.AppSettings[key];
			if (int.TryParse(value, out int result))
				return result;

			return defaultValue;
		}

		private static bool GetBool(string key, bool defaultValue)
		{
			var value = ConfigurationManager.AppSettings[key];
			if (string.IsNullOrWhiteSpace(value))
			{
				return defaultValue;
			}

			if (bool.TryParse(value, out bool parsed))
			{
				return parsed;
			}

			if (int.TryParse(value, out int numeric))
			{
				return numeric != 0;
			}

			return defaultValue;
		}

		private static string GetConnectionString(string name)
		{
			var cs = ConfigurationManager.ConnectionStrings[name];
			return cs?.ConnectionString ?? string.Empty;
		}

		private static DateTimeOffset? GetDateTimeOffset(string key)
		{
			var raw = ConfigurationManager.AppSettings[key];
			if (string.IsNullOrWhiteSpace(raw))
			{
				return null;
			}

			DateTimeOffset parsed;
			if (DateTimeOffset.TryParse(raw, out parsed))
			{
				return parsed;
			}

			Logger.Log($"CollectorConfig: {key} value '{raw}' is not a valid DateTimeOffset.");
			return null;
		}

		private static IReadOnlyList<string> GetEnabledMetrics()
		{
			var raw = ConfigurationManager.AppSettings["EnabledMetrics"];
			if (string.IsNullOrWhiteSpace(raw))
			{
				return DefaultEnabledMetrics;
			}

			var tokens = raw
				.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(token => token.Trim());

			return NormalizeEnabledMetrics(tokens);
		}

		private static int GetIntWithMinimum(string key, int minimum, int defaultValue)
		{
			var raw = ConfigurationManager.AppSettings[key];
			if (string.IsNullOrWhiteSpace(raw))
			{
				return defaultValue;
			}

			if (!int.TryParse(raw, out int value))
			{
				Logger.Log($"CollectorConfig: {key} value '{raw}' is not a valid integer. Using default {defaultValue}.");
				return defaultValue;
			}

			return EnsureMinimum(key, value, minimum, defaultValue);
		}

		private static long GetLongWithMinimum(string key, long minimum, long defaultValue)
		{
			var raw = ConfigurationManager.AppSettings[key];
			if (string.IsNullOrWhiteSpace(raw))
			{
				return defaultValue;
			}

			long value;
			if (!long.TryParse(raw, out value))
			{
				Logger.Log($"CollectorConfig: {key} value '{raw}' is not a valid long integer. Using default {defaultValue}.");
				return defaultValue;
			}

			return EnsureMinimumLong(key, value, minimum, defaultValue);
		}

		private static int EnsureMinimum(string key, int value, int minimum, int defaultValue)
		{
			if (value < minimum)
			{
				Logger.Log($"CollectorConfig: {key} value {value} is below minimum {minimum}. Using default {defaultValue}.");
				return defaultValue;
			}

			return value;
		}

		private static long EnsureMinimumLong(string key, long value, long minimum, long defaultValue)
		{
			if (value < minimum)
			{
				Logger.Log($"CollectorConfig: {key} value {value} is below minimum {minimum}. Using default {defaultValue}.");
				return defaultValue;
			}

			return value;
		}

		private static IReadOnlyList<string> NormalizeEnabledMetrics(IEnumerable<string> metrics)
		{
			if (metrics == null)
			{
				return DefaultEnabledMetrics;
			}

			var tokens = metrics
				.Where(token => !string.IsNullOrWhiteSpace(token))
				.Select(token => token.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return tokens.Length == 0 ? DefaultEnabledMetrics : Array.AsReadOnly(tokens);
		}
	}
}

