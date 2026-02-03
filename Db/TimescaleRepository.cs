using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Db
{
	public interface ITimescaleRepository
	{
		Task EnsureSchemaAsync(CancellationToken cancellationToken);

		Task UpsertMachineAsync(string serialNo, string name, CancellationToken cancellationToken);

		Task<long> GetOrCreateBatchAsync(
			string serialNo,
			int batchId,
			string growerCode,
			DateTimeOffset startTs,
			string comments,
			CancellationToken cancellationToken);

		Task InsertMetricsAsync(
			IEnumerable<MetricRow> metrics,
			CancellationToken cancellationToken);
	}

	public sealed class TimescaleRepository : ITimescaleRepository
	{
		private const int MetricsBatchSize = 100;

		private const string CreateBatchSequenceSql = "CREATE SEQUENCE IF NOT EXISTS batches_id_seq;";

		private const string AlterBatchSequenceSql =
			"ALTER SEQUENCE batches_id_seq OWNED BY batches.id;";

		private const string CreateMachinesTableSql = @"
CREATE TABLE IF NOT EXISTS machines (
	serial_no   text PRIMARY KEY,
	name        text,
	inserted_at timestamptz DEFAULT now()
);";

		private const string CreateBatchesTableSql = @"
CREATE TABLE IF NOT EXISTS batches (
	id          bigint PRIMARY KEY DEFAULT nextval('batches_id_seq'),
	batch_id    integer,
	serial_no   text REFERENCES machines (serial_no),
	grower_code text,
	start_ts    timestamptz,
	end_ts      timestamptz,
	comments    text
);";

		private const string CreateMetricsTableSql = @"
CREATE TABLE IF NOT EXISTS metrics (
	ts              timestamptz NOT NULL,
	serial_no       text        NOT NULL REFERENCES machines (serial_no),
	metric          text        NOT NULL,
	value_json      jsonb       NOT NULL,
	batch_record_id bigint      REFERENCES batches (id),
	batch_id        integer,
	PRIMARY KEY (ts, serial_no, metric)
);";

		private const string CreateMetricsHypertableSql =
			"SELECT create_hypertable('metrics', 'ts', if_not_exists => TRUE);";

		private const string CreateMetricsBatchIndexSql = @"
CREATE INDEX IF NOT EXISTS metrics_batch_record_id_idx
	ON metrics (batch_record_id);";

		private const string CreateBatchesBatchStartIndexSql = @"
CREATE INDEX IF NOT EXISTS batches_batch_id_start_ts_idx
	ON batches (batch_id, start_ts);";

		private readonly string _connectionString;

		public TimescaleRepository(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
		{
			Logger.Log("Ensuring TimescaleDB schema...");

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var transaction = connection.BeginTransaction())
					{
						try
						{
							await ExecuteNonQueryAsync(connection, transaction, CreateBatchSequenceSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, CreateMachinesTableSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, CreateBatchesTableSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, AlterBatchSequenceSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, CreateMetricsTableSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, CreateMetricsHypertableSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, CreateMetricsBatchIndexSql, cancellationToken).ConfigureAwait(false);
							await ExecuteNonQueryAsync(connection, transaction, CreateBatchesBatchStartIndexSql, cancellationToken).ConfigureAwait(false);

							transaction.Commit();
						}
						catch
						{
							try { transaction.Rollback(); } catch { }
							throw;
						}
					}
				}

				Logger.Log("TimescaleDB schema ensured.");
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to ensure TimescaleDB schema.", ex);
				throw;
			}
		}

		public async Task UpsertMachineAsync(string serialNo, string name, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			Logger.Log($"Upserting machine '{serialNo}'.");

			const string sql = @"
INSERT INTO machines (serial_no, name, inserted_at)
VALUES (@serial_no, @name, now())
ON CONFLICT (serial_no) DO NOTHING;";

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var command = new NpgsqlCommand(sql, connection))
					{
						command.Parameters.AddWithValue("serial_no", serialNo);
						command.Parameters.AddWithValue("name", (object)name ?? DBNull.Value);

						await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log($"Failed to upsert machine '{serialNo}'.", ex);
				throw;
			}
		}

		public async Task<long> GetOrCreateBatchAsync(
			string serialNo,
			int batchId,
			string growerCode,
			DateTimeOffset startTs,
			string comments,
			CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			Logger.Log($"Retrieving or creating batch for serial '{serialNo}', batch_id '{batchId}'.");

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var transaction = connection.BeginTransaction())
					{
						try
						{
							const string selectSql = @"
SELECT id, comments
  FROM batches
 WHERE batch_id = @batch_id
   AND start_ts = @start_ts
   AND serial_no = @serial_no
 LIMIT 1;";

							long? existingId = null;
							string existingComments = null;
							using (var selectCommand = new NpgsqlCommand(selectSql, connection))
							{
								selectCommand.Transaction = transaction;
								selectCommand.Parameters.AddWithValue("batch_id", batchId);
								selectCommand.Parameters.AddWithValue("start_ts", NpgsqlDbType.TimestampTz, startTs.UtcDateTime);
								selectCommand.Parameters.AddWithValue("serial_no", serialNo);

								using (var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
								{
									if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
									{
										existingId = reader.GetInt64(0);
										existingComments = reader.IsDBNull(1) ? null : reader.GetString(1);
									}
								}
							}

							if (existingId.HasValue)
							{
								if (!string.IsNullOrWhiteSpace(comments) &&
									!string.Equals(existingComments, comments, StringComparison.Ordinal))
								{
									const string updateSql = @"
UPDATE batches
   SET comments = @comments
 WHERE id = @id;";

									using (var updateCommand = new NpgsqlCommand(updateSql, connection))
									{
										updateCommand.Transaction = transaction;
										updateCommand.Parameters.AddWithValue("comments", comments);
										updateCommand.Parameters.AddWithValue("id", existingId.Value);
										await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
									}
								}

								transaction.Commit();
								return existingId.Value;
							}

							const string insertSql = @"
INSERT INTO batches (
	batch_id,
	serial_no,
	grower_code,
	start_ts,
	end_ts,
	comments)
VALUES (
	@batch_id,
	@serial_no,
	@grower_code,
	@start_ts,
	NULL,
	@comments)
RETURNING id;";

							using (var insertCommand = new NpgsqlCommand(insertSql, connection))
							{
								insertCommand.Transaction = transaction;
								insertCommand.Parameters.AddWithValue("batch_id", batchId);
								insertCommand.Parameters.AddWithValue("serial_no", serialNo);
								insertCommand.Parameters.AddWithValue("grower_code", (object)growerCode ?? DBNull.Value);
								insertCommand.Parameters.AddWithValue("start_ts", NpgsqlDbType.TimestampTz, startTs.UtcDateTime);
								insertCommand.Parameters.AddWithValue("comments", (object)comments ?? DBNull.Value);

								var result = await insertCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
								var newId = Convert.ToInt64(result);

								transaction.Commit();
								return newId;
							}
						}
						catch
						{
							try { transaction.Rollback(); } catch { }
							throw;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log($"Failed to get or create batch for serial '{serialNo}', batch_id '{batchId}'.", ex);
				throw;
			}
		}

		public async Task InsertMetricsAsync(IEnumerable<MetricRow> metrics, CancellationToken cancellationToken)
		{
			if (metrics == null)
			{
				throw new ArgumentNullException(nameof(metrics));
			}

			var metricList = metrics as IList<MetricRow> ?? metrics.ToList();
			if (metricList.Count == 0)
			{
				Logger.Log("InsertMetricsAsync called with zero metrics. No work to perform.");
				return;
			}

			var distinctMetricCount = metricList
				.Select(m => m?.MetricName)
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Count();

			Logger.Log($"Inserting {metricList.Count} metric rows across {distinctMetricCount} metric types.");

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					using (var transaction = connection.BeginTransaction())
					{
						try
						{
							var totalInserted = 0;
							for (var i = 0; i < metricList.Count; i += MetricsBatchSize)
							{
								var batchCount = Math.Min(MetricsBatchSize, metricList.Count - i);

								var commandText = BuildMetricInsertCommandText(batchCount, i);
								using (var command = new NpgsqlCommand(commandText, connection))
								{
									command.Transaction = transaction;

									for (var j = 0; j < batchCount; j++)
									{
										var metric = metricList[i + j];
										var suffix = (i + j).ToString();

										AddMetricParameter(command, "ts_" + suffix, NpgsqlDbType.TimestampTz, metric.Timestamp.UtcDateTime);

										if (metric.SerialNo == null)
										{
											throw new ArgumentException("Metric SerialNo cannot be null.", nameof(metrics));
										}

										if (metric.MetricName == null)
										{
											throw new ArgumentException("MetricName cannot be null.", nameof(metrics));
										}

										command.Parameters.AddWithValue("serial_" + suffix, metric.SerialNo);
										command.Parameters.AddWithValue("metric_" + suffix, metric.MetricName);

										var payload = string.IsNullOrWhiteSpace(metric.JsonPayload) ? "{}" : metric.JsonPayload;
										var payloadParameter = command.Parameters.Add("payload_" + suffix, NpgsqlDbType.Jsonb);
										payloadParameter.Value = payload;

										command.Parameters.AddWithValue("batch_record_id_" + suffix, metric.BatchRecordId);
									}

									totalInserted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
								}
							}

							transaction.Commit();
							Logger.Log($"Metrics insert complete. Rows affected (excluding duplicates): {totalInserted} out of {metricList.Count} attempted.");
						}
						catch
						{
							try { transaction.Rollback(); } catch { }
							throw;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("Failed to insert metric rows.", ex);
				throw;
			}
		}

		private static string BuildMetricInsertCommandText(int batchCount, int offset)
		{
			var builder = new StringBuilder();
			builder.Append("INSERT INTO metrics (ts, serial_no, metric, value_json, batch_record_id) VALUES ");

			for (var i = 0; i < batchCount; i++)
			{
				var suffix = offset + i;

				if (i > 0)
				{
					builder.Append(',');
				}

				builder.Append("(@ts_")
					.Append(suffix)
					.Append(", @serial_")
					.Append(suffix)
					.Append(", @metric_")
					.Append(suffix)
					.Append(", @payload_")
					.Append(suffix)
					.Append(", @batch_record_id_")
					.Append(suffix)
					.Append(')');
			}

			builder.Append(" ON CONFLICT (ts, serial_no, metric) DO NOTHING;");
			return builder.ToString();
		}

		private static void AddMetricParameter(NpgsqlCommand command, string name, NpgsqlDbType dbType, object value)
		{
			var parameter = command.Parameters.Add(name, dbType);
			parameter.Value = value;
		}

		private static async Task ExecuteNonQueryAsync(
			NpgsqlConnection connection,
			NpgsqlTransaction transaction,
			string sql,
			CancellationToken cancellationToken)
		{
			using (var command = new NpgsqlCommand(sql, connection, transaction))
			{
				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
		}
	}
}

