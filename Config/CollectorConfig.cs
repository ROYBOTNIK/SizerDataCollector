using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using Logger = SizerDataCollector.Logger;

namespace SizerDataCollector.Config
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
		private const int MinPollIntervalSeconds = 5;
		private const int MinInitialBackoffSeconds = 1;
		private const int DefaultPollIntervalSeconds = 60;
		private const int DefaultInitialBackoffSeconds = 10;
		private const int DefaultMaxBackoffSeconds = 300;

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

		public CollectorConfig()
		{
			SizerHost = GetString("SizerHost", "10.155.155.10");
			SizerPort = GetInt("SizerPort", 8001);
			OpenTimeoutSec = GetInt("OpenTimeoutSec", 5);
			SendTimeoutSec = GetInt("SendTimeoutSec", 5);
			ReceiveTimeoutSec = GetInt("ReceiveTimeoutSec", 5);

			TimescaleConnectionString = GetConnectionString("TimescaleDb");
			EnabledMetrics = GetEnabledMetrics();
			EnableIngestion = GetBool("EnableIngestion", false);

			var pollInterval = GetIntWithMinimum("PollIntervalSeconds", MinPollIntervalSeconds, DefaultPollIntervalSeconds);
			var initialBackoff = GetIntWithMinimum("InitialBackoffSeconds", MinInitialBackoffSeconds, DefaultInitialBackoffSeconds);
			var maxBackoff = GetIntWithMinimum("MaxBackoffSeconds", MinInitialBackoffSeconds, DefaultMaxBackoffSeconds);

			if (maxBackoff < initialBackoff)
			{
				Logger.Log($"CollectorConfig: MaxBackoffSeconds value {maxBackoff} must be greater than or equal to InitialBackoffSeconds value {initialBackoff}. Falling back to defaults ({DefaultInitialBackoffSeconds}/{DefaultMaxBackoffSeconds}).");
				initialBackoff = DefaultInitialBackoffSeconds;
				maxBackoff = DefaultMaxBackoffSeconds;
			}

			PollIntervalSeconds = pollInterval;
			InitialBackoffSeconds = initialBackoff;
			MaxBackoffSeconds = maxBackoff;
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

		private static IReadOnlyList<string> GetEnabledMetrics()
		{
			var raw = ConfigurationManager.AppSettings["EnabledMetrics"];
			if (string.IsNullOrWhiteSpace(raw))
			{
				return DefaultEnabledMetrics;
			}

			var tokens = raw
				.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(token => token.Trim())
				.Where(token => token.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();

			return tokens.Length == 0 ? DefaultEnabledMetrics : Array.AsReadOnly(tokens);
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

			if (value < minimum)
			{
				Logger.Log($"CollectorConfig: {key} value {value} is below minimum {minimum}. Using default {defaultValue}.");
				return defaultValue;
			}

			return value;
		}
	}
}

