using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Db
{
	public sealed class ThresholdRow
	{
		public string SerialNo { get; set; }
		public int MinRpm { get; set; }
		public int MinTotalFpm { get; set; }
		public DateTimeOffset UpdatedAt { get; set; }
	}

	public sealed class ThresholdsRepository
	{
		public const int DefaultMinRpm = 1700;
		public const int DefaultMinTotalFpm = 150;

		private readonly string _connectionString;

		public ThresholdsRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<ThresholdRow> GetAsync(string serialNo, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			const string sql = @"
SELECT serial_no, min_rpm, min_total_fpm, updated_at
  FROM oee.machine_thresholds
 WHERE serial_no = @serial_no
 LIMIT 1;";

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var command = new NpgsqlCommand(sql, connection))
					{
						command.Parameters.Add("serial_no", NpgsqlDbType.Text).Value = serialNo;

						using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
						{
							if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
							{
								return null;
							}

							return new ThresholdRow
							{
								SerialNo = reader.GetString(0),
								MinRpm = reader.GetInt32(1),
								MinTotalFpm = reader.GetInt32(2),
								UpdatedAt = reader.GetFieldValue<DateTimeOffset>(3)
							};
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log($"ThresholdsRepository: failed to get thresholds for '{serialNo}'.", ex);
				throw;
			}
		}

		public async Task UpsertAsync(string serialNo, int minRpm, int minTotalFpm, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			const string sql = @"
INSERT INTO oee.machine_thresholds (serial_no, min_rpm, min_total_fpm, updated_at)
VALUES (@serial_no, @min_rpm, @min_total_fpm, now())
ON CONFLICT (serial_no) DO UPDATE
SET min_rpm = EXCLUDED.min_rpm,
    min_total_fpm = EXCLUDED.min_total_fpm,
    updated_at = now();";

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var command = new NpgsqlCommand(sql, connection))
					{
						command.Parameters.Add("serial_no", NpgsqlDbType.Text).Value = serialNo;
						command.Parameters.Add("min_rpm", NpgsqlDbType.Integer).Value = minRpm;
						command.Parameters.Add("min_total_fpm", NpgsqlDbType.Integer).Value = minTotalFpm;

						await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log($"ThresholdsRepository: failed to upsert thresholds for '{serialNo}'.", ex);
				throw;
			}
		}
	}
}

