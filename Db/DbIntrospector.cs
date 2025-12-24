using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using System.Diagnostics;

namespace SizerDataCollector.Core.Db
{
	public sealed class DbIntrospector
	{
		private static readonly string[] RequiredTables =
		{
			"public.machines",
			"public.batches",
			"public.metrics",
			"public.machine_settings",
			"oee.band_definitions",
			"oee.band_statistics",
			"oee.machine_thresholds",
			"oee.shift_calendar",
			"oee.grade_lane_anomalies"
		};

		private static readonly string[] RequiredFunctions =
		{
			"oee.availability_state",
			"oee.availability_ratio",
			"oee.calc_quality_ratio_qv1",
			"oee.calc_perf_ratio",
			"oee.grade_qty",
			"oee.grade_to_cat",
			"oee.outlet_recycle_fpm",
			"oee.get_target_throughput",
			"oee.classify_oee_value"
		};

		private static readonly ContinuousAggregateTarget[] RequiredCaggs =
		{
			new ContinuousAggregateTarget("oee", "cagg_availability_daily"),
			new ContinuousAggregateTarget("oee", "cagg_availability_daily_batch"),
			new ContinuousAggregateTarget("oee", "cagg_availability_minute"),
			new ContinuousAggregateTarget("oee", "cagg_availability_minute_batch"),
			new ContinuousAggregateTarget("oee", "cagg_grade_daily_batch"),
			new ContinuousAggregateTarget("oee", "cagg_grade_minute_batch"),
			new ContinuousAggregateTarget("oee", "cagg_throughput_daily_batch"),
			new ContinuousAggregateTarget("oee", "cagg_throughput_minute_batch"),
			new ContinuousAggregateTarget("public", "cagg_lane_grade_minute"),
			new ContinuousAggregateTarget("public", "cagg_lane_size_minute"),
			new ContinuousAggregateTarget("public", "cagg_throughput_daily"),
			new ContinuousAggregateTarget("public", "cagg_throughput_minute")
		};

		private readonly string _connectionString;

		public DbIntrospector(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
		}

		public async Task<DbHealthReport> RunAsync(CancellationToken cancellationToken)
		{
			var report = new DbHealthReport
			{
				CheckedAt = DateTimeOffset.UtcNow
			};

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					report.CanConnect = true;
					report.DatabaseName = connection.Database;

			await CheckTimescaleAsync(connection, report, cancellationToken).ConfigureAwait(false);
					await CheckSchemaVersionAsync(connection, report, cancellationToken).ConfigureAwait(false);
					await CheckTablesAsync(connection, report, cancellationToken).ConfigureAwait(false);
					await CheckFunctionsAsync(connection, report, cancellationToken).ConfigureAwait(false);
					await CheckContinuousAggregatesAsync(connection, report, cancellationToken).ConfigureAwait(false);
					await CheckRefreshPoliciesAsync(connection, report, cancellationToken).ConfigureAwait(false);
					await CheckSeedCountsAsync(connection, report, cancellationToken).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				report.Error = ex.Message;
				report.Exception = ex;
			}

			return report;
		}

		private static async Task CheckTimescaleAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			const string sql = "SELECT extversion FROM pg_extension WHERE extname = 'timescaledb';";
			using (var command = new NpgsqlCommand(sql, connection))
			{
				var version = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
				report.TimescaleInstalled = !string.IsNullOrWhiteSpace(version);
				report.TimescaleVersion = version;
			}
		}

		private static async Task CheckSchemaVersionAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			const string sql = @"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name='schema_version';";
			using (var command = new NpgsqlCommand(sql, connection))
			{
				var exists = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
				if (!exists)
				{
					report.AppliedMigrationsCount = 0;
					return;
				}
			}

			using (var command = new NpgsqlCommand("SELECT COUNT(*) FROM public.schema_version;", connection))
			{
				var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
				report.AppliedMigrationsCount = Convert.ToInt32(count);
			}
		}

		private static async Task CheckTablesAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			const string sql = @"SELECT table_schema || '.' || table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE';";
			var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			using (var command = new NpgsqlCommand(sql, connection))
			using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
			{
				while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
				{
					existing.Add(reader.GetString(0));
				}
			}

			foreach (var table in RequiredTables)
			{
				if (!existing.Contains(table))
				{
					report.MissingTables.Add(table);
				}
			}
		}

		private static async Task CheckFunctionsAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			const string sql = @"SELECT n.nspname || '.' || p.proname FROM pg_proc p JOIN pg_namespace n ON p.pronamespace = n.oid;";
			var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			using (var command = new NpgsqlCommand(sql, connection))
			using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
			{
				while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
				{
					existing.Add(reader.GetString(0));
				}
			}

			foreach (var func in RequiredFunctions)
			{
				if (!existing.Contains(func))
				{
					report.MissingFunctions.Add(func);
				}
			}
		}

		private static async Task CheckContinuousAggregatesAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			if (!report.TimescaleInstalled)
			{
				report.MissingContinuousAggregates.AddRange(RequiredCaggs.Select(c => c.QualifiedName));
				return;
			}

			const string sql = @"SELECT view_schema, view_name, materialization_hypertable_schema, materialization_hypertable_name
FROM timescaledb_information.continuous_aggregates;";

			var existingViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var matMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			using (var command = new NpgsqlCommand(sql, connection))
			using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
			{
				while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
				{
					var viewSchema = reader.GetString(0);
					var viewName = reader.GetString(1);
					var matSchema = reader.GetString(2);
					var matName = reader.GetString(3);

					var qualified = $"{viewSchema}.{viewName}";
					existingViews.Add(qualified);
					matMap[qualified] = $"{matSchema}.{matName}";
				}
			}

			foreach (var cagg in RequiredCaggs)
			{
				if (!existingViews.Contains(cagg.QualifiedName))
				{
					report.MissingContinuousAggregates.Add(cagg.QualifiedName);
				}
				else if (matMap.TryGetValue(cagg.QualifiedName, out var matName))
				{
					report.MaterializationHypertables[cagg.QualifiedName] = matName;
				}
			}

			report.ContinuousAggregateCount = existingViews.Count;
		}

		private static async Task CheckRefreshPoliciesAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			if (!report.TimescaleInstalled)
			{
				report.MissingPolicies.AddRange(RequiredCaggs.Select(c => c.QualifiedName));
				report.ExpectedPolicies = RequiredCaggs.Length;
				report.FoundPolicies = 0;
				return;
			}

			const string sql = @"
SELECT
  ca.view_schema,
  ca.view_name,
  j.job_id,
  j.schedule_interval,
  j.config
FROM timescaledb_information.continuous_aggregates ca
JOIN timescaledb_information.jobs j
  ON j.proc_name = 'policy_refresh_continuous_aggregate'
 AND (j.config->>'mat_hypertable_id')::int =
     (SELECT h.id
      FROM _timescaledb_catalog.hypertable h
      WHERE h.schema_name = ca.materialization_hypertable_schema
        AND h.table_name  = ca.materialization_hypertable_name)
ORDER BY ca.view_schema, ca.view_name;";

			var policyMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			try
			{
				using (var command = new NpgsqlCommand(sql, connection))
				using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
				{
					while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
					{
						var viewSchema = reader.GetString(0);
						var viewName = reader.GetString(1);
						var key = $"{viewSchema}.{viewName}";
						policyMap.Add(key);
					}
				}
			}
			catch (Exception ex)
			{
				report.PolicyCheckError = $"Policy detection failed: {ex.Message}";
				return;
			}

			report.ExpectedPolicies = RequiredCaggs.Length;
			report.FoundPolicies = policyMap.Count;

			foreach (var cagg in RequiredCaggs)
			{
				if (!policyMap.Contains(cagg.QualifiedName))
				{
					report.MissingPolicies.Add(cagg.QualifiedName);
				}
			}

			Trace.WriteLine($"DbHealth: CAGGs detected={report.ContinuousAggregateCount}, policies detected={report.FoundPolicies}, missing policies={report.MissingPolicies.Count}");
			if (report.MissingPolicies.Count > 0)
			{
				Trace.TraceWarning("DbHealth: Missing policy targets -> " + string.Join(", ", report.MissingPolicies));
			}
		}

		private static async Task CheckSeedCountsAsync(NpgsqlConnection connection, DbHealthReport report, CancellationToken cancellationToken)
		{
			report.BandDefinitionsCount = await CountAsync(connection, "oee.band_definitions", cancellationToken).ConfigureAwait(false);
			report.MachineThresholdsCount = await CountAsync(connection, "oee.machine_thresholds", cancellationToken).ConfigureAwait(false);
			report.ShiftCalendarCount = await CountAsync(connection, "oee.shift_calendar", cancellationToken).ConfigureAwait(false);
		}

		private static async Task<long> CountAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
		{
			var sql = $"SELECT COUNT(*) FROM {tableName};";
			using (var command = new NpgsqlCommand(sql, connection))
			{
				var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
				return Convert.ToInt64(result);
			}
		}

		private sealed class ContinuousAggregateTarget
		{
			public ContinuousAggregateTarget(string schema, string name)
			{
				Schema = schema;
				Name = name;
				QualifiedName = $"{schema}.{name}";
			}

			public string Schema { get; }
			public string Name { get; }
			public string QualifiedName { get; }
		}
	}

	public sealed class DbHealthReport
	{
		public DateTimeOffset CheckedAt { get; set; }
		public bool CanConnect { get; set; }
		public string DatabaseName { get; set; }

		public bool TimescaleInstalled { get; set; }
		public string TimescaleVersion { get; set; }

		public List<string> MissingTables { get; } = new List<string>();
		public List<string> MissingFunctions { get; } = new List<string>();
		public List<string> MissingContinuousAggregates { get; } = new List<string>();
		public List<string> MissingPolicies { get; } = new List<string>();

		public string PolicyCheckError { get; set; }
		public int ExpectedPolicies { get; set; }
		public int FoundPolicies { get; set; }

		public int AppliedMigrationsCount { get; set; }
		public int ContinuousAggregateCount { get; set; }

		public long BandDefinitionsCount { get; set; }
		public long MachineThresholdsCount { get; set; }
		public long ShiftCalendarCount { get; set; }

		public Dictionary<string, string> MaterializationHypertables { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public string Error { get; set; }
		public Exception Exception { get; set; }

		public bool HasAllTables => MissingTables.Count == 0;
		public bool HasAllFunctions => MissingFunctions.Count == 0;
		public bool HasAllContinuousAggregates => MissingContinuousAggregates.Count == 0;
		public bool HasAllPolicies => MissingPolicies.Count == 0;
		public bool SeedPresent => BandDefinitionsCount > 0 && MachineThresholdsCount > 0 && ShiftCalendarCount > 0;
		public bool Healthy =>
			CanConnect &&
			TimescaleInstalled &&
			HasAllTables &&
			HasAllFunctions &&
			HasAllContinuousAggregates &&
			HasAllPolicies &&
			SeedPresent &&
			Exception == null;
	}
}

