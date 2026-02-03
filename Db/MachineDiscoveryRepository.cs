using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer.Discovery;

namespace SizerDataCollector.Core.Db
{
	public interface IMachineDiscoveryRepository
	{
		Task<InsertSnapshotResult> InsertSnapshotAsync(MachineDiscoverySnapshot snapshot, CancellationToken cancellationToken);

		Task<MachineDiscoverySnapshot> GetLatestSnapshotAsync(string serialNo, CancellationToken cancellationToken);

		Task<IReadOnlyList<DiscoverySnapshotRecord>> GetRecentSnapshotsAsync(string serialNo, int limit, CancellationToken cancellationToken);
	}

	public sealed class MachineDiscoveryRepository : IMachineDiscoveryRepository
	{
		private readonly string _connectionString;

		private const string InsertSql = @"
INSERT INTO oee.machine_discovery_snapshots
(
	serial_no,
	discovered_at,
	source_host,
	source_port,
	client_kind,
	success,
	duration_ms,
	error_text,
	payload_json,
	summary_json
)
VALUES
(
	@serial_no,
	DEFAULT,
	@source_host,
	@source_port,
	@client_kind,
	@success,
	@duration_ms,
	@error_text,
	@payload_json,
	@summary_json
)
RETURNING id, discovered_at;";

		private const string SelectLatestSql = @"
SELECT
	id,
	serial_no,
	discovered_at,
	source_host,
	source_port,
	client_kind,
	success,
	duration_ms,
	error_text,
	payload_json,
	summary_json
FROM oee.machine_discovery_snapshots
WHERE serial_no = @serial_no
ORDER BY discovered_at DESC
LIMIT 1;";

		private const string SelectRecentSql = @"
SELECT
	id,
	serial_no,
	discovered_at,
	success,
	error_text,
	payload_json,
	summary_json
FROM oee.machine_discovery_snapshots
WHERE (@serial_no IS NULL OR serial_no = @serial_no)
ORDER BY discovered_at DESC
LIMIT @limit;";

		public MachineDiscoveryRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<InsertSnapshotResult> InsertSnapshotAsync(MachineDiscoverySnapshot snapshot, CancellationToken cancellationToken)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			if (string.IsNullOrWhiteSpace(snapshot.SerialNo))
			{
				throw new ArgumentException("Snapshot SerialNo is required for persistence.", nameof(snapshot));
			}

			var payloadJson = BuildPayloadEnvelope(snapshot);
			var summaryJson = snapshot.Summary != null ? JObject.FromObject(snapshot.Summary) : null;

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var command = new NpgsqlCommand(InsertSql, connection))
					{
						command.Parameters.AddWithValue("serial_no", snapshot.SerialNo);
						command.Parameters.AddWithValue("source_host", (object)snapshot.SourceHost ?? DBNull.Value);
						command.Parameters.AddWithValue("source_port", (object)snapshot.SourcePort ?? DBNull.Value);
						command.Parameters.AddWithValue("client_kind", (object)snapshot.ClientKind ?? DBNull.Value);
						command.Parameters.AddWithValue("success", snapshot.Success);
						command.Parameters.AddWithValue("duration_ms", (object)snapshot.DurationMs ?? DBNull.Value);
						command.Parameters.AddWithValue("error_text", (object)snapshot.ErrorText ?? DBNull.Value);

						var payloadParam = command.Parameters.Add("payload_json", NpgsqlDbType.Jsonb);
						payloadParam.Value = payloadJson.ToString();

						var summaryParam = command.Parameters.Add("summary_json", NpgsqlDbType.Jsonb);
						summaryParam.Value = (object)summaryJson?.ToString() ?? DBNull.Value;

						using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
						{
							if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
							{
								var id = reader.GetInt64(0);
								var discoveredAt = reader.GetFieldValue<DateTime>(1);
								return new InsertSnapshotResult
								{
									Id = id,
									DiscoveredAt = DateTime.SpecifyKind(discoveredAt, DateTimeKind.Utc)
								};
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to insert discovery snapshot.", ex);
				throw;
			}

			throw new InvalidOperationException("InsertSnapshotAsync did not return a row.");
		}

		public async Task<MachineDiscoverySnapshot> GetLatestSnapshotAsync(string serialNo, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("serialNo is required.", nameof(serialNo));
			}

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var command = new NpgsqlCommand(SelectLatestSql, connection))
					{
						command.Parameters.AddWithValue("serial_no", serialNo);

						using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
						{
							if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
							{
								return null;
							}

							var payloadString = reader.IsDBNull(9) ? null : reader.GetString(9);
							var summaryString = reader.IsDBNull(10) ? null : reader.GetString(10);
							var envelope = ParseEnvelope(payloadString);

							return new MachineDiscoverySnapshot
							{
								SerialNo = envelope?.SerialNo ?? reader.GetString(1),
								MachineName = envelope?.MachineName,
								SourceHost = reader.IsDBNull(3) ? null : reader.GetString(3),
								SourcePort = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4),
								ClientKind = reader.IsDBNull(5) ? null : reader.GetString(5),
								StartedAtUtc = envelope?.StartedAt ?? DateTimeOffset.MinValue,
								FinishedAtUtc = reader.GetFieldValue<DateTime>(2),
								DurationMs = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
								Success = reader.GetBoolean(6),
								ErrorText = reader.IsDBNull(8) ? null : reader.GetString(8),
								Payloads = ParsePayloads(payloadString),
								Summary = ParseSummary(summaryString)
							};
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log($"Failed to load latest discovery snapshot for serial '{serialNo}'.", ex);
				throw;
			}
		}

		public async Task<IReadOnlyList<DiscoverySnapshotRecord>> GetRecentSnapshotsAsync(string serialNo, int limit, CancellationToken cancellationToken)
		{
			if (limit <= 0)
			{
				throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
			}

			var results = new List<DiscoverySnapshotRecord>();

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var command = new NpgsqlCommand(SelectRecentSql, connection))
					{
						if (string.IsNullOrWhiteSpace(serialNo))
						{
							command.Parameters.Add("serial_no", NpgsqlDbType.Text).Value = DBNull.Value;
						}
						else
						{
							command.Parameters.AddWithValue("serial_no", serialNo);
						}

						command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = limit;

						using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
						{
							while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
							{
								var payloadString = reader.IsDBNull(5) ? null : reader.GetString(5);
								var summaryString = reader.IsDBNull(6) ? null : reader.GetString(6);
								var envelope = ParseEnvelope(payloadString);

								results.Add(new DiscoverySnapshotRecord
								{
									Id = reader.GetInt64(0),
									SerialNo = reader.IsDBNull(1) ? envelope?.SerialNo : reader.GetString(1),
									DiscoveredAt = reader.GetFieldValue<DateTime>(2),
									Success = reader.GetBoolean(3),
									ErrorText = reader.IsDBNull(4) ? null : reader.GetString(4),
									MachineName = envelope?.MachineName,
									RawPayloadJson = payloadString,
									RawSummaryJson = summaryString,
									Payloads = ParsePayloads(payloadString),
									Summary = ParseSummary(summaryString)
								});
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to load recent discovery snapshots.", ex);
				throw;
			}

			return results;
		}

		private static JObject BuildPayloadEnvelope(MachineDiscoverySnapshot snapshot)
		{
			var envelope = new JObject
			{
				["serial_no"] = snapshot.SerialNo,
				["machine_name"] = snapshot.MachineName,
				["source_host"] = snapshot.SourceHost,
				["source_port"] = snapshot.SourcePort,
				["client_kind"] = snapshot.ClientKind,
				["started_at_utc"] = snapshot.StartedAtUtc,
				["finished_at_utc"] = snapshot.FinishedAtUtc,
				["duration_ms"] = snapshot.DurationMs,
				["success"] = snapshot.Success,
				["error_text"] = snapshot.ErrorText,
				["payloads"] = snapshot.Payloads != null ? JObject.FromObject(snapshot.Payloads) : new JObject()
			};

			return envelope;
		}

		private static (string SerialNo, string MachineName, DateTimeOffset? StartedAt)? ParseEnvelope(string payloadString)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(payloadString))
				{
					return null;
				}

				var obj = JObject.Parse(payloadString);
				var serial = obj["serial_no"]?.Value<string>();
				var machine = obj["machine_name"]?.Value<string>();
				var started = obj["started_at_utc"]?.Value<DateTime?>();
				return (serial, machine, started.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(started.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null);
			}
			catch
			{
				return null;
			}
		}

		private static IDictionary<string, JToken> ParsePayloads(string payloadString)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(payloadString))
				{
					return new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
				}

				var obj = JObject.Parse(payloadString);
				var payloads = obj["payloads"] as JObject;
				if (payloads == null)
				{
					return new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
				}

				var result = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
				foreach (var prop in payloads.Properties())
				{
					result[prop.Name] = prop.Value;
				}

				return result;
			}
			catch
			{
				return new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
			}
		}

		private static MachineDiscoverySummary ParseSummary(string summaryString)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(summaryString))
				{
					return null;
				}

				return JObject.Parse(summaryString).ToObject<MachineDiscoverySummary>();
			}
			catch
			{
				return null;
			}
		}
	}

	public sealed class InsertSnapshotResult
	{
		public long Id { get; set; }
		public DateTimeOffset DiscoveredAt { get; set; }
	}

	public sealed class DiscoverySnapshotRecord
	{
		public long Id { get; set; }
		public string SerialNo { get; set; }
		public DateTime DiscoveredAt { get; set; }
		public bool Success { get; set; }
		public string ErrorText { get; set; }
		public string MachineName { get; set; }
		public IDictionary<string, JToken> Payloads { get; set; }
		public MachineDiscoverySummary Summary { get; set; }
		public string RawPayloadJson { get; set; }
		public string RawSummaryJson { get; set; }
	}
}

