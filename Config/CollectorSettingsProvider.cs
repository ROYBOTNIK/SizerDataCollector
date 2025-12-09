using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Config
{
	public sealed class CollectorSettingsProvider
	{
		private const string RuntimeConfigFileName = "collector_config.json";

		public CollectorRuntimeSettings Load()
		{
			var path = GetRuntimeSettingsPath();
			Logger.Log($"CollectorSettingsProvider: Attempting to load runtime settings from '{path}'.");

			if (File.Exists(path))
			{
				try
				{
					var defaults = BuildFromCollectorConfig();
					var fallbackMetrics = defaults.EnabledMetrics?.ToList() ?? new List<string>();
					defaults.EnabledMetrics = null;

					var json = File.ReadAllText(path);
					JsonConvert.PopulateObject(json, defaults);

					defaults.EnabledMetrics = NormalizeMetrics(defaults.EnabledMetrics ?? fallbackMetrics);
					defaults.SharedDataDirectory = NormalizeSharedDirectory(defaults.SharedDataDirectory);

					return defaults;
				}
				catch (Exception ex)
				{
					Logger.Log($"CollectorSettingsProvider: Failed to load runtime settings from '{path}'. Falling back to App.config.", ex);
				}
			}
			else
			{
				Logger.Log($"CollectorSettingsProvider: Runtime settings file '{path}' not found. Falling back to App.config.");
			}

			return BuildFromCollectorConfig();
		}

		public void Save(CollectorRuntimeSettings settings)
		{
			if (settings == null)
			{
				throw new ArgumentNullException(nameof(settings));
			}

			settings.EnabledMetrics = NormalizeMetrics(settings.EnabledMetrics);
			settings.SharedDataDirectory = NormalizeSharedDirectory(settings.SharedDataDirectory);

			var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
			var path = GetRuntimeSettingsPath();

			File.WriteAllText(path, json);
			Logger.Log($"CollectorSettingsProvider: Saved runtime settings to '{path}'.");
		}

		private static string GetRuntimeSettingsPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RuntimeConfigFileName);
		}

		private static CollectorRuntimeSettings BuildFromCollectorConfig()
		{
			var config = new CollectorConfig();

			return new CollectorRuntimeSettings
			{
				SizerHost = config.SizerHost,
				SizerPort = config.SizerPort,
				OpenTimeoutSec = config.OpenTimeoutSec,
				SendTimeoutSec = config.SendTimeoutSec,
				ReceiveTimeoutSec = config.ReceiveTimeoutSec,
				TimescaleConnectionString = config.TimescaleConnectionString,
				EnabledMetrics = NormalizeMetrics(config.EnabledMetrics),
				EnableIngestion = config.EnableIngestion,
				PollIntervalSeconds = config.PollIntervalSeconds,
				InitialBackoffSeconds = config.InitialBackoffSeconds,
				MaxBackoffSeconds = config.MaxBackoffSeconds,
				SharedDataDirectory = GetDefaultSharedDataDirectory()
			};
		}

		private static List<string> NormalizeMetrics(IEnumerable<string> metrics)
		{
			if (metrics == null)
			{
				return new List<string>();
			}

			return new HashSet<string>(metrics.Where(m => !string.IsNullOrWhiteSpace(m)), StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static string NormalizeSharedDirectory(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return GetDefaultSharedDataDirectory();
			}

			// Expand any environment variables (e.g. %ProgramData%) so hosts and tools
			// use the same concrete path on disk.
			var expanded = Environment.ExpandEnvironmentVariables(value);
			return string.IsNullOrWhiteSpace(expanded) ? GetDefaultSharedDataDirectory() : expanded;
		}

		private static string GetDefaultSharedDataDirectory()
		{
			var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
			return Path.Combine(commonAppData, "Opti-Fresh", "SizerCollector");
		}
	}
}

