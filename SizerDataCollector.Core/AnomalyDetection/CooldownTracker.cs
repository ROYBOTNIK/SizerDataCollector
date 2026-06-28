using System;
using System.Collections.Generic;

namespace SizerDataCollector.Core.AnomalyDetection
{
	public sealed class CooldownTracker
	{
		private readonly int _cooldownSeconds;
		private readonly Dictionary<string, DateTimeOffset> _lastAlarm = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

		public CooldownTracker(int cooldownSeconds)
		{
			_cooldownSeconds = cooldownSeconds;
		}

		public bool IsOnCooldown(int lane, string gradeKey, DateTimeOffset now)
		{
			var key = MakeKey(lane, gradeKey);
			if (!_lastAlarm.TryGetValue(key, out var last))
				return false;

			return (now - last).TotalSeconds < _cooldownSeconds;
		}

		public void Record(int lane, string gradeKey, DateTimeOffset now)
		{
			_lastAlarm[MakeKey(lane, gradeKey)] = now;
		}

		public void Reset()
		{
			_lastAlarm.Clear();
		}

		private static string MakeKey(int lane, string gradeKey)
		{
			return lane + "|" + gradeKey;
		}
	}
}
