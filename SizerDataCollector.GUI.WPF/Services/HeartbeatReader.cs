using System;
using System.IO;
using Newtonsoft.Json;
using SizerDataCollector.Core.Collector;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.GUI.WPF.Services
{
	public sealed class HeartbeatReader
	{
		private readonly string _filePath;

		public HeartbeatReader(string filePath)
		{
			_filePath = filePath;
		}

		public HeartbeatPayload ReadOrNull()
		{
			try
			{
				if (!File.Exists(_filePath))
				{
					return null;
				}

				var json = File.ReadAllText(_filePath);
				if (string.IsNullOrWhiteSpace(json))
				{
					return null;
				}

				return JsonConvert.DeserializeObject<HeartbeatPayload>(json);
			}
			catch (Exception ex)
			{
				Logger.Log($"HeartbeatReader: Failed to read heartbeat from '{_filePath}'.", ex);
				return null;
			}
		}
	}
}

