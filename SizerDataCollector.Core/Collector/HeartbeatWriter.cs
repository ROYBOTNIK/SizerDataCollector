using System;
using System.IO;
using Newtonsoft.Json;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Collector
{
	public sealed class HeartbeatWriter
	{
		private readonly string _filePath;

		public HeartbeatWriter(string filePath)
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				throw new ArgumentException("Heartbeat file path must not be empty.", nameof(filePath));
			}

			_filePath = filePath;
		}

		public void Write(HeartbeatPayload payload)
		{
			try
			{
				var directory = Path.GetDirectoryName(_filePath);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
				File.WriteAllText(_filePath, json);
			}
			catch (Exception ex)
			{
				Logger.Log($"HeartbeatWriter: Failed to write heartbeat to '{_filePath}'.", ex);
			}
		}
	}
}

