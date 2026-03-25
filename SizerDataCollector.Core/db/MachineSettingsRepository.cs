using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Db
{
	public sealed class MachineSettingsRepository
	{
		private readonly string _connectionString;

		public MachineSettingsRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<IReadOnlyList<MachineRow>> GetMachinesAsync(CancellationToken cancellationToken)
		{
			const string sql = @"SELECT serial_no, name FROM public.machines ORDER BY serial_no;";
			var results = new List<MachineRow>();

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
				{
					while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
					{
						results.Add(new MachineRow
						{
							SerialNo = reader.IsDBNull(0) ? null : reader.GetString(0),
							Name = reader.IsDBNull(1) ? null : reader.GetString(1)
						});
					}
				}
			}

			return results;
		}

		public async Task<MachineSettingsRow> GetSettingsAsync(string serialNo, CancellationToken cancellationToken)
		{
			const string sql = @"
SELECT serial_no, target_machine_speed, lane_count, target_percentage, recycle_outlet
  FROM public.machine_settings
 WHERE serial_no = @serial_no
 LIMIT 1;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							return null;
						}

						return new MachineSettingsRow
						{
							SerialNo = reader.IsDBNull(0) ? null : reader.GetString(0),
							TargetMachineSpeed = reader.IsDBNull(1) ? (double?)null : reader.GetDouble(1),
							LaneCount = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
							TargetPercentage = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
							RecycleOutlet = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4)
						};
					}
				}
			}
		}

		public async Task UpsertSettingsAsync(string serialNo, double targetMachineSpeed, int laneCount, double targetPercentage, int recycleOutlet, CancellationToken cancellationToken)
		{
			const string sql = @"
INSERT INTO public.machine_settings (serial_no, target_machine_speed, lane_count, target_percentage, recycle_outlet)
VALUES (@serial_no, @target_machine_speed, @lane_count, @target_percentage, @recycle_outlet)
ON CONFLICT (serial_no) DO UPDATE
SET target_machine_speed = EXCLUDED.target_machine_speed,
    lane_count           = EXCLUDED.lane_count,
    target_percentage    = EXCLUDED.target_percentage,
    recycle_outlet       = EXCLUDED.recycle_outlet;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					command.Parameters.AddWithValue("target_machine_speed", targetMachineSpeed);
					command.Parameters.AddWithValue("lane_count", laneCount);
					command.Parameters.AddWithValue("target_percentage", targetPercentage);
					command.Parameters.AddWithValue("recycle_outlet", recycleOutlet);

					await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		public async Task<double?> GetTargetThroughputAsync(string serialNo, CancellationToken cancellationToken)
		{
			const string sql = @"SELECT oee.get_target_throughput(@serial_no);";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo ?? (object)DBNull.Value);
					var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
					if (result == null || result is DBNull) return null;
					return Convert.ToDouble(result);
				}
			}
		}

		public async Task<IReadOnlyList<GradeOverrideRow>> GetGradeOverridesAsync(string serialNo, CancellationToken cancellationToken)
		{
			const string sql = @"SELECT grade_key, desired_cat, is_active, created_by FROM oee.grade_map WHERE serial_no = @serial_no ORDER BY grade_key;";
			var results = new List<GradeOverrideRow>();

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
					{
						while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
						{
							results.Add(new GradeOverrideRow
							{
								GradeKey = reader.IsDBNull(0) ? null : reader.GetString(0),
								DesiredCat = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
								IsActive = !reader.IsDBNull(2) && reader.GetBoolean(2),
								CreatedBy = reader.IsDBNull(3) ? null : reader.GetString(3)
							});
						}
					}
				}
			}

			return results;
		}

		public async Task<int?> ResolveCategoryAsync(string serialNo, string gradeKey, CancellationToken cancellationToken)
		{
			const string sql = @"SELECT oee.grade_to_cat(@serial_no, @grade_key);";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", (object)serialNo ?? DBNull.Value);
					command.Parameters.AddWithValue("grade_key", (object)gradeKey ?? DBNull.Value);
					var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
					if (result == null || result is DBNull) return null;
					return Convert.ToInt32(result);
				}
			}
		}

		public async Task UpsertGradeOverrideAsync(string serialNo, string gradeKey, int desiredCat, bool isActive, string createdBy, CancellationToken cancellationToken)
		{
			const string sql = @"
INSERT INTO oee.grade_map (serial_no, grade_key, desired_cat, is_active, created_by)
VALUES (@serial_no, @grade_key, @desired_cat, @is_active, @created_by)
ON CONFLICT (serial_no, grade_key) DO UPDATE
SET desired_cat = EXCLUDED.desired_cat,
    is_active   = EXCLUDED.is_active,
    created_by  = EXCLUDED.created_by;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					command.Parameters.AddWithValue("grade_key", gradeKey);
					command.Parameters.AddWithValue("desired_cat", desiredCat);
					command.Parameters.AddWithValue("is_active", isActive);
					command.Parameters.AddWithValue("created_by", (object)createdBy ?? DBNull.Value);

					await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}

		public async Task RemoveGradeOverrideAsync(string serialNo, string gradeKey, CancellationToken cancellationToken)
		{
			const string sql = @"DELETE FROM oee.grade_map WHERE serial_no = @serial_no AND grade_key = @grade_key;";

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(sql, connection))
				{
					command.Parameters.AddWithValue("serial_no", serialNo);
					command.Parameters.AddWithValue("grade_key", gradeKey);
					await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
				}
			}
		}
	}

	public sealed class MachineRow
	{
		public string SerialNo { get; set; }
		public string Name { get; set; }
	}

	public sealed class MachineSettingsRow
	{
		public string SerialNo { get; set; }
		public double? TargetMachineSpeed { get; set; }
		public int? LaneCount { get; set; }
		public double? TargetPercentage { get; set; }
		public int? RecycleOutlet { get; set; }
	}

	public sealed class GradeOverrideRow
	{
		public string GradeKey { get; set; }
		public int? DesiredCat { get; set; }
		public bool IsActive { get; set; }
		public string CreatedBy { get; set; }
	}
}

