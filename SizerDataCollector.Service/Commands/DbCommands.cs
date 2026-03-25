using System;
using System.Collections.Generic;
using System.Threading;
using Npgsql;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Service.Commands
{
	internal static class DbCommands
	{
		public static int Run(string[] args)
		{
			var subCommand = args.Length > 0
				? (args[0] ?? string.Empty).Trim().ToLowerInvariant()
				: string.Empty;

			switch (subCommand)
			{
				case "status":
					return Status();
				case "init":
					return Init();
				case "migrate":
					Console.WriteLine("NOTE: 'db migrate' is deprecated. Use 'db init' instead.");
					return Init();
				case "apply-functions":
					return ApplyFile("functions.sql");
				case "apply-caggs":
					return ApplyFile("continuous_aggregates.sql");
				case "apply-views":
					return ApplyFile("views.sql");
				case "apply-all":
					return ApplyAll();
				case "list-functions":
					return ListFunctions();
				case "list-views":
					return ListViews();
				case "list-caggs":
					return ListCaggs();
				default:
					Console.WriteLine("Usage: SizerDataCollector.Service.exe db <command>");
					Console.WriteLine("Commands: status, init, apply-functions, apply-caggs, apply-views, apply-all,");
					Console.WriteLine("          list-functions, list-views, list-caggs");
					return 1;
			}
		}

		private static int Status()
		{
			var config = LoadConfig();
			if (config == null) return 1;

			Console.WriteLine("Checking database health...");
			var introspector = new DbIntrospector(config.TimescaleConnectionString);
			var report = introspector.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine();
			Console.WriteLine($"  Database:       {report.DatabaseName ?? "(unknown)"}");
			Console.WriteLine($"  Can connect:    {report.CanConnect}");
			Console.WriteLine($"  TimescaleDB:    {(report.TimescaleInstalled ? report.TimescaleVersion : "NOT INSTALLED")}");
			Console.WriteLine($"  Migrations:     {report.AppliedMigrationsCount} applied");
			Console.WriteLine();

			if (report.MissingTables.Count > 0)
			{
				Console.WriteLine($"  Missing tables ({report.MissingTables.Count}):");
				foreach (var t in report.MissingTables) Console.WriteLine($"    - {t}");
			}
			else
			{
				Console.WriteLine("  Tables:         OK");
			}

			if (report.MissingFunctions.Count > 0)
			{
				Console.WriteLine($"  Missing functions ({report.MissingFunctions.Count}):");
				foreach (var f in report.MissingFunctions) Console.WriteLine($"    - {f}");
			}
			else
			{
				Console.WriteLine("  Functions:      OK");
			}

			if (report.MissingContinuousAggregates.Count > 0)
			{
				Console.WriteLine($"  Missing CAGGs ({report.MissingContinuousAggregates.Count}):");
				foreach (var c in report.MissingContinuousAggregates) Console.WriteLine($"    - {c}");
			}
			else
			{
				Console.WriteLine($"  CAGGs:          OK ({report.ContinuousAggregateCount} total)");
			}

			if (report.MissingPolicies.Count > 0)
			{
				Console.WriteLine($"  Missing policies ({report.MissingPolicies.Count}):");
				foreach (var p in report.MissingPolicies) Console.WriteLine($"    - {p}");
			}
			else
			{
				Console.WriteLine($"  Refresh policies: OK ({report.FoundPolicies}/{report.ExpectedPolicies})");
			}

			Console.WriteLine();
			Console.WriteLine($"  Thresholds rows:  {report.MachineThresholdsCount}");
			Console.WriteLine($"  Band definitions: {report.BandDefinitionsCount}");
			Console.WriteLine($"  Shift calendar:   {report.ShiftCalendarCount}");
			Console.WriteLine($"  Discovery snaps:  {report.DiscoverySnapshotCount}");

			if (report.LatestDiscoveryAt.HasValue)
			{
				Console.WriteLine($"  Latest discovery: {report.LatestDiscoveryAt.Value:u}");
			}

			Console.WriteLine();

			if (!string.IsNullOrWhiteSpace(report.Error))
			{
				Console.WriteLine($"  ERROR: {report.Error}");
			}

			Console.WriteLine(report.Healthy ? "  Overall: HEALTHY" : "  Overall: UNHEALTHY");
			return report.Healthy ? 0 : 1;
		}

		private static int Init()
		{
			Console.WriteLine("Initializing database (schema -> functions -> CAGGs -> views)...");
			Console.WriteLine();

			var r1 = ApplyFile("schema.sql");
			if (r1 != 0) return r1;

			Console.WriteLine();
			return ApplyAll();
		}

		private static int ApplyFile(string fileName)
		{
			var config = LoadConfig();
			if (config == null) return 1;

			var settings = new CollectorSettingsProvider().Load();
			var runner = new SqlDefinitionRunner(config.TimescaleConnectionString, settings.SharedDataDirectory);

			var resolved = runner.ResolvePath(fileName);
			if (resolved == null)
			{
				Console.WriteLine($"SQL file '{fileName}' not found.");
				Console.WriteLine("Searched locations:");
				if (!string.IsNullOrWhiteSpace(settings.SharedDataDirectory))
				{
					Console.WriteLine($"  1. {settings.SharedDataDirectory}\\sql\\definitions\\{fileName}");
				}
				Console.WriteLine($"  2. {AppDomain.CurrentDomain.BaseDirectory}sql\\definitions\\{fileName}");
				return 1;
			}

			Console.WriteLine($"Applying {fileName} from: {resolved}");
			var result = runner.ApplyAsync(fileName, CancellationToken.None).GetAwaiter().GetResult();

			if (result.Succeeded)
			{
				Console.WriteLine($"{fileName} applied successfully.");
				return 0;
			}

			Console.WriteLine($"Failed to apply {fileName}: {result.ErrorMessage}");
			return 1;
		}

		private static int ApplyAll()
		{
			Console.WriteLine("Applying all definitions (functions -> CAGGs -> views)...");
			Console.WriteLine();

			var r1 = ApplyFile("functions.sql");
			if (r1 != 0) return r1;

			Console.WriteLine();
			var r2 = ApplyFile("continuous_aggregates.sql");
			if (r2 != 0) return r2;

			Console.WriteLine();
			var r3 = ApplyFile("views.sql");
			if (r3 != 0) return r3;

			Console.WriteLine();
			Console.WriteLine("All definitions applied successfully.");
			return 0;
		}

		private static int ListFunctions()
		{
			var config = LoadConfig();
			if (config == null) return 1;

			const string sql = @"
SELECT n.nspname AS schema, p.proname AS name,
       pg_catalog.pg_get_function_arguments(p.oid) AS args,
       pg_catalog.pg_get_function_result(p.oid) AS returns
FROM pg_proc p
JOIN pg_namespace n ON p.pronamespace = n.oid
WHERE n.nspname IN ('oee', 'public')
  AND p.prokind = 'f'
  AND p.proname NOT IN ('pg_stat_statements_reset', 'pg_stat_statements')
  AND NOT EXISTS (SELECT 1 FROM pg_extension e
                  JOIN pg_depend d ON d.refobjid = e.oid
                  WHERE d.objid = p.oid AND d.deptype = 'e')
ORDER BY n.nspname, p.proname;";

			return RunQuery(config, sql, "Functions", reader =>
			{
				var schema = reader.GetString(0);
				var name = reader.GetString(1);
				var args = reader.GetString(2);
				var returns = reader.GetString(3);
				Console.WriteLine($"  {schema}.{name}({args}) -> {returns}");
			});
		}

		private static int ListViews()
		{
			var config = LoadConfig();
			if (config == null) return 1;

			const string sql = @"
SELECT table_schema, table_name
FROM information_schema.views
WHERE table_schema IN ('oee', 'public')
  AND table_name NOT LIKE 'pg_%'
  AND table_name NOT LIKE 'information_%'
ORDER BY table_schema, table_name;";

			return RunQuery(config, sql, "Views", reader =>
			{
				Console.WriteLine($"  {reader.GetString(0)}.{reader.GetString(1)}");
			});
		}

		private static int ListCaggs()
		{
			var config = LoadConfig();
			if (config == null) return 1;

			const string sql = @"
SELECT view_schema, view_name,
       materialization_hypertable_schema || '.' || materialization_hypertable_name AS mat_table
FROM timescaledb_information.continuous_aggregates
ORDER BY view_schema, view_name;";

			return RunQuery(config, sql, "Continuous Aggregates", reader =>
			{
				Console.WriteLine($"  {reader.GetString(0)}.{reader.GetString(1)}  (mat: {reader.GetString(2)})");
			});
		}

		private static int RunQuery(CollectorConfig config, string sql, string label, Action<NpgsqlDataReader> printRow)
		{
			try
			{
				var count = 0;
				using (var conn = new NpgsqlConnection(config.TimescaleConnectionString))
				{
					conn.Open();
					using (var cmd = new NpgsqlCommand(sql, conn))
					using (var reader = cmd.ExecuteReader())
					{
						while (reader.Read())
						{
							count++;
							printRow(reader);
						}
					}
				}

				Console.WriteLine();
				Console.WriteLine($"{count} {label.ToLowerInvariant()} found.");
				return 0;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to list {label.ToLowerInvariant()}: {ex.Message}");
				return 1;
			}
		}

		private static CollectorConfig LoadConfig()
		{
			try
			{
				var provider = new CollectorSettingsProvider();
				var settings = provider.Load();
				var config = new CollectorConfig(settings);

				if (string.IsNullOrWhiteSpace(config.TimescaleConnectionString))
				{
					Console.WriteLine("No database connection string configured. Run 'set-db' first.");
					return null;
				}

				return config;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to load configuration: {ex.Message}");
				return null;
			}
		}
	}
}
