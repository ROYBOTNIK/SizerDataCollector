using System;
using Npgsql;
using SizerDataCollector.Config;

namespace SizerDataCollector
{
	internal static class DatabaseTester
	{
		/// <summary>
		/// Tests the connection to Timescale/Postgres and ensures required tables exist
		/// with the same structure as the existing schema:
		///   machines, batches (with id bigserial), metrics (with batch_record_id FK).
		/// </summary>
		public static void TestAndInitialize(CollectorConfig cfg)
		{
			if (string.IsNullOrWhiteSpace(cfg.TimescaleConnectionString))
			{
				Logger.Log("TimescaleDb connection string is empty. Skipping DB test.");
				return;
			}

			Logger.Log("Testing TimescaleDb connection...");

			try
			{
				using (var conn = new NpgsqlConnection(cfg.TimescaleConnectionString))
				{
					conn.Open();
					Logger.Log("Successfully connected to TimescaleDb/Postgres.");

					EnsureSchema(conn);
				}
			}
			catch (Exception ex)
			{
				Logger.Log("FAILED to connect to TimescaleDb/Postgres.", ex);
			}
		}

		/// <summary>
		/// Ensures that the required tables exist, matching the current DB layout:
		///
		/// machines:
		///   serial_no text PK
		///   name text
		///   inserted_at timestamptz
		///
		/// batches:
		///   id bigserial PK
		///   batch_id int
		///   serial_no text FK -> machines(serial_no)
		///   grower_code text
		///   start_ts timestamptz
		///   end_ts timestamptz
		///   comments text
		///
		/// metrics:
		///   ts timestamptz
		///   serial_no text FK -> machines(serial_no)
		///   metric text
		///   batch_id int
		///   batch_record_id bigint FK -> batches(id)
		///   value_json jsonb
		///
		///   PRIMARY KEY (ts, serial_no, metric)
		///
		/// Uses CREATE TABLE IF NOT EXISTS, so it will not overwrite existing tables.
		/// </summary>
		private static void EnsureSchema(NpgsqlConnection conn)
		{
			Logger.Log("Ensuring core tables exist (machines, batches, metrics) with existing layout...");

			string createMachinesSql = @"
				CREATE TABLE IF NOT EXISTS machines (
					serial_no   text PRIMARY KEY,
					name        text,
					inserted_at timestamptz DEFAULT now()
				);
			";

			string createBatchesSql = @"
				CREATE TABLE IF NOT EXISTS batches (
					id          bigserial PRIMARY KEY,
					batch_id    int,
					serial_no   text REFERENCES machines (serial_no),
					grower_code text,
					start_ts    timestamptz,
					end_ts      timestamptz,
					comments    text
				);
			";

			string createMetricsSql = @"
				CREATE TABLE IF NOT EXISTS metrics (
					ts              timestamptz NOT NULL,
					serial_no       text        NOT NULL REFERENCES machines (serial_no),
					batch_id        int,
					batch_record_id bigint      REFERENCES batches (id),
					metric          text        NOT NULL,
					value_json      jsonb       NOT NULL,
					PRIMARY KEY (ts, serial_no, metric)
				);
			";

			// Optional, but useful for joins on batches:
			string indexMetricsBatchRecordIdSql = @"
				CREATE INDEX IF NOT EXISTS metrics_batch_record_id_idx
					ON metrics (batch_record_id);
			";

			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandText = createMachinesSql;
				cmd.ExecuteNonQuery();
				Logger.Log("Ensured table: machines");

				cmd.CommandText = createBatchesSql;
				cmd.ExecuteNonQuery();
				Logger.Log("Ensured table: batches");

				cmd.CommandText = createMetricsSql;
				cmd.ExecuteNonQuery();
				Logger.Log("Ensured table: metrics");

				cmd.CommandText = indexMetricsBatchRecordIdSql;
				cmd.ExecuteNonQuery();
				Logger.Log("Ensured index: metrics_batch_record_id_idx on metrics(batch_record_id).");
			}

			Logger.Log("Schema initialization aligned with existing DB structure complete.");
		}
		public static void UpsertMachine(CollectorConfig cfg, string serialNo, string name)
		{
			if (string.IsNullOrWhiteSpace(cfg.TimescaleConnectionString))
			{
				Logger.Log("TimescaleDb connection string is empty. Cannot upsert machine.");
				return;
			}

			if (string.IsNullOrWhiteSpace(serialNo))
			{
				Logger.Log("UpsertMachine called with empty serialNo. Skipping.");
				return;
			}

			try
			{
				using (var conn = new NpgsqlConnection(cfg.TimescaleConnectionString))
				{
					conn.Open();

					const string sql = @"
				INSERT INTO machines (serial_no, name)
				VALUES (@serial_no, @name)
				ON CONFLICT (serial_no) DO UPDATE
					SET name = EXCLUDED.name;
			";

					using (var cmd = new NpgsqlCommand(sql, conn))
					{
						cmd.Parameters.AddWithValue("serial_no", serialNo);
						cmd.Parameters.AddWithValue("name", (object)name ?? DBNull.Value);

						int rows = cmd.ExecuteNonQuery();
						Logger.Log($"UpsertMachine: serial_no={serialNo}, name='{name}', rows affected={rows}.");
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log($"Failed to upsert machine (serial_no={serialNo}).", ex);
			}
		}

	}
}

