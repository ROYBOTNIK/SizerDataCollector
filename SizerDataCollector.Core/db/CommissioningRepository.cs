using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Db
{
	public sealed class CommissioningRow
	{
		public string SerialNo { get; set; } = string.Empty;
		public DateTimeOffset? DbBootstrappedAt { get; set; }
		public DateTimeOffset? SizerConnectedAt { get; set; }
		public DateTimeOffset? MachineDiscoveredAt { get; set; }
		public DateTimeOffset? GradeMappingCompletedAt { get; set; }
		public DateTimeOffset? ThresholdsSetAt { get; set; }
		public DateTimeOffset? IngestionEnabledAt { get; set; }
		public string Notes { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
	}

	public sealed class CommissioningRepository
	{
		private readonly string _connectionString;

		public CommissioningRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task MarkDiscoveredAsync(string serialNo, DateTimeOffset discoveredAt, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			const string sql = @"
INSERT INTO oee.commissioning_status (serial_no, machine_discovered_at, updated_at)
VALUES (@serial_no, @discovered_at, now())
ON CONFLICT (serial_no) DO UPDATE
SET machine_discovered_at = CASE
	WHEN oee.commissioning_status.machine_discovered_at IS NULL THEN EXCLUDED.machine_discovered_at
	WHEN EXCLUDED.machine_discovered_at > oee.commissioning_status.machine_discovered_at THEN EXCLUDED.machine_discovered_at
	ELSE oee.commissioning_status.machine_discovered_at
END,
    updated_at = CASE
	    WHEN oee.commissioning_status.machine_discovered_at IS NULL
	         OR EXCLUDED.machine_discovered_at > oee.commissioning_status.machine_discovered_at
	    THEN now()
	    ELSE oee.commissioning_status.updated_at
    END;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					var tsParam = command.Parameters.Add("discovered_at", NpgsqlDbType.TimestampTz);
					tsParam.Value = discoveredAt.UtcDateTime;

					await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		public async Task EnsureRowAsync(string serialNo)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			const string sql = @"
INSERT INTO oee.commissioning_status (serial_no)
VALUES (@serial_no)
ON CONFLICT (serial_no) DO NOTHING;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync().ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					await command.ExecuteNonQueryAsync().ConfigureAwait(false);
				}
			}
		}

		public async Task<CommissioningRow> GetAsync(string serialNo)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			const string sql = @"
SELECT serial_no,
       db_bootstrapped_at,
       sizer_connected_at,
       machine_discovered_at,
       grade_mapping_completed_at,
       thresholds_set_at,
       ingestion_enabled_at,
       notes,
       updated_at
  FROM oee.commissioning_status
 WHERE serial_no = @serial_no
 LIMIT 1;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync().ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
					{
						if (!await reader.ReadAsync().ConfigureAwait(false))
						{
							return null;
						}

						return new CommissioningRow
						{
							SerialNo = reader.GetString(0),
							DbBootstrappedAt = reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(1),
							SizerConnectedAt = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2),
							MachineDiscoveredAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3),
							GradeMappingCompletedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4),
							ThresholdsSetAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5),
							IngestionEnabledAt = reader.IsDBNull(6) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(6),
							Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
							UpdatedAt = reader.GetFieldValue<DateTimeOffset>(8)
						};
					}
				}
			}
		}

		public async Task SetTimestampAsync(string serialNo, string step, DateTimeOffset? ts)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			var column = MapStepToColumn(step);
			await EnsureRowAsync(serialNo).ConfigureAwait(false);

			var sql = $@"
UPDATE oee.commissioning_status
   SET {column} = @ts,
       updated_at = now()
 WHERE serial_no = @serial_no;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync().ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					var tsParam = command.Parameters.Add("ts", NpgsqlDbType.TimestampTz);
					tsParam.Value = ts.HasValue ? (object)ts.Value : DBNull.Value;

					await command.ExecuteNonQueryAsync().ConfigureAwait(false);
				}
			}
		}

		public async Task UpdateNotesAsync(string serialNo, string notes)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			await EnsureRowAsync(serialNo).ConfigureAwait(false);

			const string sql = @"
UPDATE oee.commissioning_status
   SET notes = @notes,
       updated_at = now()
 WHERE serial_no = @serial_no;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync().ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					command.Parameters.AddWithValue("notes", (object)notes ?? DBNull.Value);

					await command.ExecuteNonQueryAsync().ConfigureAwait(false);
				}
			}
		}

		private static string MapStepToColumn(string step)
		{
			switch (step?.Trim().ToLowerInvariant())
			{
				case "db_bootstrapped_at":
					return "db_bootstrapped_at";
				case "sizer_connected_at":
					return "sizer_connected_at";
				case "machine_discovered_at":
					return "machine_discovered_at";
				case "grade_mapping_completed_at":
					return "grade_mapping_completed_at";
				case "thresholds_set_at":
					return "thresholds_set_at";
				case "ingestion_enabled_at":
					return "ingestion_enabled_at";
				default:
					Logger.Log($"CommissioningRepository: Unsupported step '{step}'.");
					throw new ArgumentException("Unsupported step value.", nameof(step));
			}
		}

		public async Task ResetAsync(string serialNo, string notes)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			await EnsureRowAsync(serialNo).ConfigureAwait(false);

			const string sql = @"
UPDATE oee.commissioning_status
   SET sizer_connected_at = NULL,
       machine_discovered_at = NULL,
       grade_mapping_completed_at = NULL,
       thresholds_set_at = NULL,
       ingestion_enabled_at = NULL,
       notes = @notes,
       updated_at = now()
 WHERE serial_no = @serial_no;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync().ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					command.Parameters.AddWithValue("notes", (object)notes ?? DBNull.Value);
					await command.ExecuteNonQueryAsync().ConfigureAwait(false);
				}
			}
		}
	}
}

