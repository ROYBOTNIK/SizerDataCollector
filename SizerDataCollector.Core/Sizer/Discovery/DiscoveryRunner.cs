using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Sizer.Discovery
{
	public sealed class DiscoveryRunner
	{
		private static readonly string[] MetricKeys =
		{
			"lanes_grade_fpm",
			"lanes_size_fpm",
			"machine_total_fpm",
			"machine_cupfill",
			"outlets_details",
			"machine_missed_fpm"
		};

		private readonly Func<CollectorConfig, ISizerClient> _clientFactory;

		public DiscoveryRunner(Func<CollectorConfig, ISizerClient> clientFactory = null)
		{
			_clientFactory = clientFactory ?? (cfg => new SizerClient(cfg));
		}

		public async Task<MachineDiscoverySnapshot> RunAsync(CollectorConfig cfg, CancellationToken ct)
		{
			if (cfg == null) throw new ArgumentNullException(nameof(cfg));

			var started = DateTimeOffset.UtcNow;
			var payloads = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
			var timings = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
			var callResults = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
			var operations = ReflectServiceOperations();
			string serial = null;
			string machineName = null;

			using (var client = _clientFactory(cfg))
			{
				serial = await CaptureAsync("serial_no", () => client.GetSerialNoAsync(ct), payloads, timings, callResults, ct).ConfigureAwait(false);
				machineName = await CaptureAsync("machine_name", () => client.GetMachineNameAsync(ct), payloads, timings, callResults, ct).ConfigureAwait(false);
				await CaptureAsync("current_batch", () => client.GetCurrentBatchAsync(ct), payloads, timings, callResults, ct).ConfigureAwait(false);

				foreach (var metric in MetricKeys)
				{
					await CaptureAsync(metric, () => client.GetMetricJsonAsync(metric, ct), payloads, timings, callResults, ct).ConfigureAwait(false);
				}
			}

			var finished = DateTimeOffset.UtcNow;
			var durationMs = (int)Math.Round((finished - started).TotalMilliseconds);

			// Success rule: serial + machine name must be present; metric failures are tolerated.
			var success = !string.IsNullOrWhiteSpace(serial) && !string.IsNullOrWhiteSpace(machineName);

			var snapshot = new MachineDiscoverySnapshot
			{
				SerialNo = serial,
				MachineName = machineName,
				SourceHost = cfg.SizerHost,
				SourcePort = cfg.SizerPort,
				ClientKind = "wcf",
				StartedAtUtc = started,
				FinishedAtUtc = finished,
				DurationMs = durationMs,
				Success = success,
				ErrorText = success ? null : "Required identifiers missing (serial or machine name).",
				Payloads = payloads
			};

			payloads["meta"] = new JObject
			{
				["endpoint_uri"] = $"http://{cfg.SizerHost}:{cfg.SizerPort}/SizerService/",
				["binding"] = "WSHttpBinding",
				["source_host"] = cfg.SizerHost,
				["source_port"] = cfg.SizerPort
			};

			payloads["timings_ms"] = JObject.FromObject(timings);
			payloads["call_results"] = JObject.FromObject(callResults);
			payloads["service_operations"] = operations;

			snapshot.Summary = MachineDiscoverySummary.FromPayloads(snapshot.Payloads);

			return snapshot;
		}

		private static async Task<T> CaptureAsync<T>(
			string key,
			Func<Task<T>> operation,
			IDictionary<string, JToken> payloads,
			IDictionary<string, long> timings,
			IDictionary<string, JToken> callResults,
			CancellationToken ct)
		{
			var sw = Stopwatch.StartNew();
			try
			{
				var value = await operation().ConfigureAwait(false);
				payloads[key] = ToToken(value);
				timings[key] = sw.ElapsedMilliseconds;
				callResults[key] = new JObject
				{
					["success"] = true,
					["error_type"] = null,
					["error_message"] = null
				};
				return value;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				timings[key] = sw.ElapsedMilliseconds;
				callResults[key] = new JObject
				{
					["success"] = false,
					["error_type"] = ex.GetType().Name,
					["error_message"] = ex.Message
				};

				Logger.Log($"Discovery call '{key}' failed: {ex.Message}", ex);
				payloads[key] = new JObject
				{
					["error"] = ex.Message,
					["raw"] = null
				};
				return default;
			}
		}

		private static JToken ToToken(object value)
		{
			if (value == null)
			{
				return JValue.CreateNull();
			}

			if (value is string s)
			{
				return new JValue(s);
			}

			return JToken.FromObject(value);
		}

		private static JArray ReflectServiceOperations()
		{
			try
			{
				var contractType = typeof(SizerServiceReference.ISizerService);
				var methods = contractType.GetMethods();
				var arr = new JArray();
				foreach (var m in methods)
				{
					var item = new JObject
					{
						["name"] = m.Name,
						["return_type"] = m.ReturnType?.Name
					};

					var parameters = new JArray();
					foreach (var p in m.GetParameters())
					{
						parameters.Add($"{p.ParameterType?.Name} {p.Name}");
					}
					item["parameters"] = parameters;

					arr.Add(item);
				}

				return arr;
			}
			catch (Exception ex)
			{
				return new JArray
				{
					new JObject
					{
						["error"] = ex.Message
					}
				};
			}
		}
	}

	public static class DiscoveryRunnerHarness
	{
		/// <summary>
		/// Simple manual test harness for running discovery once and printing the snapshot.
		/// Not used by the service; intended for integration/console testing.
		/// </summary>
		public static async Task RunOnceAsync(CollectorConfig cfg, CancellationToken ct)
		{
			var runner = new DiscoveryRunner();
			var snapshot = await runner.RunAsync(cfg, ct).ConfigureAwait(false);
			var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
			Console.WriteLine(json);
		}
	}
}

