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
		private const string RuntimeConfigFolderName = "SizerDataCollector";

		public CollectorRuntimeSettings Load()
		{
			var preferredPath = GetRuntimeSettingsPath();
			var legacyPath = GetLegacyRuntimeSettingsPath();

			if (TryLoadFromPath(preferredPath, out var preferredSettings))
			{
				return preferredSettings;
			}

			if (!string.Equals(preferredPath, legacyPath, StringComparison.OrdinalIgnoreCase) &&
				TryLoadFromPath(legacyPath, out var legacySettings))
			{
				Logger.Log(
					$"CollectorSettingsProvider: Loaded runtime settings from legacy location '{legacyPath}'. " +
					$"Run 'config set' to persist to '{preferredPath}'.");
				return legacySettings;
			}

			Logger.Log(
				$"CollectorSettingsProvider: Runtime settings file not found at '{preferredPath}' " +
				$"or legacy path '{legacyPath}'. Falling back to App.config.");

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
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			File.WriteAllText(path, json);
			Logger.Log($"CollectorSettingsProvider: Saved runtime settings to '{path}'.");
		}

		private static string GetRuntimeSettingsPath()
		{
			var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
			return Path.Combine(commonAppData, "Opti-Fresh", RuntimeConfigFolderName, RuntimeConfigFileName);
		}

		private static string GetLegacyRuntimeSettingsPath()
		{
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RuntimeConfigFileName);
		}

		private static bool TryLoadFromPath(string path, out CollectorRuntimeSettings settings)
		{
			settings = null;
			Logger.Log($"CollectorSettingsProvider: Attempting to load runtime settings from '{path}'.");
			if (!File.Exists(path))
			{
				return false;
			}

			try
			{
				var defaults = BuildFromCollectorConfig();
				var fallbackMetrics = defaults.EnabledMetrics?.ToList() ?? new List<string>();
				defaults.EnabledMetrics = null;

				var json = File.ReadAllText(path);
				JsonConvert.PopulateObject(json, defaults);

				defaults.EnabledMetrics = NormalizeMetrics(defaults.EnabledMetrics ?? fallbackMetrics);
				defaults.SharedDataDirectory = NormalizeSharedDirectory(defaults.SharedDataDirectory);

				settings = defaults;
				return true;
			}
			catch (Exception ex)
			{
				Logger.Log($"CollectorSettingsProvider: Failed to load runtime settings from '{path}'.", ex);
				return false;
			}
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
				SharedDataDirectory = GetDefaultSharedDataDirectory(),
				LogLevel = config.LogLevel,
				DiagnosticMode = config.DiagnosticMode,
				DiagnosticUntilUtc = config.DiagnosticUntilUtc,
				LogAsJson = config.LogAsJson,
				LogMaxFileBytes = config.LogMaxFileBytes,
				LogRetentionDays = config.LogRetentionDays,
				LogMaxFiles = config.LogMaxFiles
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

