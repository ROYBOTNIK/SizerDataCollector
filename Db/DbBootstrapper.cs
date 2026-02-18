using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Db
{
	public sealed class DbBootstrapper
	{
		public const string BasePath = @"C:\ProgramData\Opti-Fresh\db";
		public const string MigrationPath = @"C:\ProgramData\Opti-Fresh\db\sql\migrations";

		private const string SchemaVersionTableSql = @"
CREATE TABLE IF NOT EXISTS public.schema_version
(
	version     text PRIMARY KEY,
	applied_at  timestamptz NOT NULL DEFAULT now(),
	checksum    text NOT NULL,
	script_name text NOT NULL
);";

		private readonly string _connectionString;
		private readonly Assembly _assembly;

		public DbBootstrapper(string connectionString)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
			_assembly = typeof(DbBootstrapper).Assembly;
		}

		/// <summary>
		/// Applies database migrations using the embedded SQL scripts.
		/// This overload preserves the original behaviour (no dry-run, destructive operations allowed)
		/// for existing callers such as the WPF commissioning UI.
		/// </summary>
		public Task<BootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
		{
			return BootstrapAsync(allowDestructive: true, dryRun: false, cancellationToken: cancellationToken);
		}

		/// <summary>
		/// Applies database migrations with optional dry-run and destructive-operation gating.
		/// When <paramref name="allowDestructive"/> is false, scripts that contain
		/// potentially destructive statements (DROP TABLE/SCHEMA, TRUNCATE, ALTER TABLE ... DROP COLUMN)
		/// will be skipped and reported as such in the result.
		/// </summary>
		public async Task<BootstrapResult> BootstrapAsync(bool allowDestructive, bool dryRun, CancellationToken cancellationToken)
		{
			var result = new BootstrapResult();

			try
			{
				var scripts = await PrepareAndLoadScriptsAsync(cancellationToken).ConfigureAwait(false);

				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
					await EnsureSchemaVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);
					var applied = await LoadAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);

					LogMigrationDiscovery(scripts);
					LogAppliedSummary(applied);

					foreach (var script in scripts)
					{
						try
						{
							var checksum = ComputeChecksum(script.Content);

							if (applied.TryGetValue(script.Version, out var existing))
							{
								if (string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
								{
									Logger.Log($"SKIPPING {script.ScriptName} (version={script.Version}) reason=already applied");
									result.Migrations.Add(MigrationResult.Skipped(script.Version, script.ScriptName, "Already applied with matching checksum."));
									continue;
								}

								var mismatch = MigrationResult.ChecksumMismatch(script.Version, script.ScriptName, existing.Checksum, checksum);
								result.Migrations.Add(mismatch);
								result.Exception = mismatch.Exception;
								result.ErrorMessage = mismatch.Message;
								Logger.Log($"SKIPPING {script.ScriptName} (version={script.Version}) reason=checksum mismatch existing={existing.Checksum} new={checksum}");
								continue;
							}

							var isDestructive = IsPotentiallyDestructive(script.Content);
							if (isDestructive && !allowDestructive)
							{
								const string reason = "Potentially destructive migration skipped (requires --allow-destructive).";
								Logger.Log($"SKIPPING {script.ScriptName} (version={script.Version}) reason={reason}");
								result.Migrations.Add(MigrationResult.Skipped(script.Version, script.ScriptName, reason));
								continue;
							}

							if (dryRun)
							{
								const string reason = "Dry run: script not executed.";
								Logger.Log($"DRY-RUN {script.ScriptName} (version={script.Version})");
								result.Migrations.Add(MigrationResult.Skipped(script.Version, script.ScriptName, reason));
								continue;
							}

							Logger.Log($"APPLYING {script.ScriptName} (version={script.Version})");
							var migrationResult = await ApplyMigrationAsync(connection, script, checksum, cancellationToken).ConfigureAwait(false);
							result.Migrations.Add(migrationResult);

							if (migrationResult.Status == MigrationStatus.Failed)
							{
								result.Exception = migrationResult.Exception;
								result.ErrorMessage = migrationResult.Message;
								return result;
							}
						}
						catch (Exception ex)
						{
							Logger.Log($"Migration {script.ScriptName} (version={script.Version}) failed.", ex);
							result.Exception = ex;
							result.ErrorMessage = ex.Message;
							return result;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("DbBootstrapper: bootstrap failed during migration discovery or application.", ex);
				result.Exception = ex;
				result.ErrorMessage = ex.Message;
			}

			return result;
		}

		public Task EnsureSqlFolderAsync(CancellationToken cancellationToken)
		{
			EnsureMigrationDirectoryExists();
			CopyEmbeddedMigrations();
			cancellationToken.ThrowIfCancellationRequested();
			return Task.CompletedTask;
		}

		/// <summary>
		/// Plans migrations without executing them, returning a per-script view of what is
		/// already applied, what would be applied, and which scripts are potentially destructive.
		/// </summary>
		public async Task<IReadOnlyList<MigrationPlanItem>> PlanAsync(CancellationToken cancellationToken)
		{
			var scripts = await PrepareAndLoadScriptsAsync(cancellationToken).ConfigureAwait(false);
			var plan = new List<MigrationPlanItem>(scripts.Count);

			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				await EnsureSchemaVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);
				var applied = await LoadAppliedVersionsAsync(connection, cancellationToken).ConfigureAwait(false);

				LogMigrationDiscovery(scripts);
				LogAppliedSummary(applied);

				foreach (var script in scripts)
				{
					cancellationToken.ThrowIfCancellationRequested();
					applied.TryGetValue(script.Version, out var existing);
					var checksum = ComputeChecksum(script.Content);
					var isDestructive = IsPotentiallyDestructive(script.Content);

					var item = new MigrationPlanItem
					{
						Version = script.Version,
						ScriptName = script.ScriptName,
						Checksum = checksum,
						ExistingChecksum = existing?.Checksum,
						ExistingScriptName = existing?.ScriptName,
						AppliedAt = existing?.AppliedAt,
						IsPotentiallyDestructive = isDestructive
					};

					if (existing == null)
					{
						item.Status = MigrationPlanStatus.PendingApply;
					}
					else if (string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
					{
						item.Status = MigrationPlanStatus.AlreadyApplied;
					}
					else
					{
						item.Status = MigrationPlanStatus.ChecksumMismatch;
					}

					plan.Add(item);
				}
			}

			return plan;
		}

		private Task<IReadOnlyList<MigrationScript>> PrepareAndLoadScriptsAsync(CancellationToken cancellationToken)
		{
			EnsureMigrationDirectoryExists();
			CopyEmbeddedMigrations();

			var files = Directory
				.GetFiles(MigrationPath, "*.sql", SearchOption.TopDirectoryOnly)
				.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
				.ToList();

			var scripts = new List<MigrationScript>(files.Count);
			foreach (var file in files)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var content = File.ReadAllText(file, Encoding.UTF8);
				var fileName = Path.GetFileName(file);
				var version = Path.GetFileNameWithoutExtension(fileName);

				scripts.Add(new MigrationScript
				{
					ScriptName = fileName,
					Version = version,
					Content = content
				});
			}

			return Task.FromResult<IReadOnlyList<MigrationScript>>(scripts);
		}

		private static void LogAppliedSummary(Dictionary<string, SchemaVersionEntry> applied)
		{
			if (applied == null)
			{
				Logger.Log("DbBootstrapper: schema_version check returned null.");
				return;
			}

			var latest = applied.Values
				.OrderByDescending(v => v.AppliedAt)
				.FirstOrDefault();

			var latestText = latest == null ? "(none)" : $"{latest.Version} at {latest.AppliedAt:u}";
			Logger.Log($"DbBootstrapper: schema_version applied_count={applied.Count}, latest={latestText}");
		}

		private void LogMigrationDiscovery(IReadOnlyList<MigrationScript> scripts)
		{
			Logger.Log($"DbBootstrapper: migration folder={MigrationPath}");

			if (scripts == null || scripts.Count == 0)
			{
				Logger.Log("DbBootstrapper: No migration scripts found.");
				return;
			}

			var ordered = scripts.Select(s => s.ScriptName).ToArray();
			Logger.Log("DbBootstrapper: discovered migrations (apply order): " + string.Join(", ", ordered));
		}

		private void EnsureMigrationDirectoryExists()
		{
			var sqlRoot = Path.GetDirectoryName(MigrationPath);
			if (!string.IsNullOrWhiteSpace(sqlRoot) && !Directory.Exists(sqlRoot))
			{
				Directory.CreateDirectory(sqlRoot);
			}

			if (!Directory.Exists(MigrationPath))
			{
				Directory.CreateDirectory(MigrationPath);
			}
		}

		private void CopyEmbeddedMigrations()
		{
			var resourceNames = _assembly
				.GetManifestResourceNames()
				.Where(r => r.IndexOf(".Migrations.", StringComparison.OrdinalIgnoreCase) >= 0
				            && r.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
				.ToList();

			foreach (var resourceName in resourceNames)
			{
				var fileName = ExtractFileName(resourceName);
				var destinationPath = Path.Combine(MigrationPath, fileName);

				using (var stream = _assembly.GetManifestResourceStream(resourceName))
				{
					if (stream == null)
					{
						throw new InvalidOperationException($"Unable to load embedded migration resource '{resourceName}'.");
					}

					using (var fileStream = File.Create(destinationPath))
					{
						stream.CopyTo(fileStream);
					}
				}
			}
		}

		private static string ExtractFileName(string resourceName)
		{
			const string marker = ".Migrations.";
			var markerIndex = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (markerIndex >= 0)
			{
				return resourceName.Substring(markerIndex + marker.Length);
			}

			// Fallback: last two segments joined with '.'
			var parts = resourceName.Split('.');
			if (parts.Length >= 2)
			{
				return parts[parts.Length - 2] + "." + parts[parts.Length - 1];
			}

			return resourceName;
		}

		private static async Task EnsureSchemaVersionTableAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
		{
			using (var command = new NpgsqlCommand(SchemaVersionTableSql, connection))
			{
				command.CommandTimeout = 0;
				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		private static async Task<Dictionary<string, SchemaVersionEntry>> LoadAppliedVersionsAsync(
			NpgsqlConnection connection,
			CancellationToken cancellationToken)
		{
			const string sql = "SELECT version, checksum, script_name, applied_at FROM public.schema_version;";
			var result = new Dictionary<string, SchemaVersionEntry>(StringComparer.OrdinalIgnoreCase);

			using (var command = new NpgsqlCommand(sql, connection))
			using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
			{
				while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
				{
					var entry = new SchemaVersionEntry
					{
						Version = reader.GetString(0),
						Checksum = reader.GetString(1),
						ScriptName = reader.GetString(2),
						AppliedAt = reader.GetFieldValue<DateTime>(3)
					};

					result[entry.Version] = entry;
				}
			}

			return result;
		}

		private static async Task<MigrationResult> ApplyMigrationAsync(
			NpgsqlConnection connection,
			MigrationScript script,
			string checksum,
			CancellationToken cancellationToken)
		{
			using (var transaction = connection.BeginTransaction())
			{
				try
				{
					using (var command = new NpgsqlCommand(script.Content, connection, transaction))
					{
						command.CommandTimeout = 0;
						await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}

					const string insertSql = @"
INSERT INTO public.schema_version (version, checksum, script_name)
VALUES (@version, @checksum, @script_name);";

					using (var insertCommand = new NpgsqlCommand(insertSql, connection, transaction))
					{
						insertCommand.Parameters.AddWithValue("version", script.Version);
						insertCommand.Parameters.AddWithValue("checksum", checksum);
						insertCommand.Parameters.AddWithValue("script_name", script.ScriptName);

						await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}

					transaction.Commit();
					Logger.Log($"APPLIED {script.ScriptName} (version={script.Version})");
					return MigrationResult.Applied(script.Version, script.ScriptName);
				}
				catch (Npgsql.PostgresException pgex) when (pgex.SqlState == "42P07" || pgex.SqlState == "42710")
				{
					// Object already exists (e.g., duplicate continuous aggregate or view); treat as skipped.
					try { transaction.Rollback(); } catch { }
					Logger.Log($"Migration {script.ScriptName} skipped due to existing object: {pgex.MessageText}");
					return MigrationResult.Skipped(script.Version, script.ScriptName, $"Already exists: {pgex.MessageText}");
				}
				catch (Exception ex)
				{
					try { transaction.Rollback(); } catch { }
					if (ex is Npgsql.PostgresException pgex)
					{
						Logger.Log(
							$"Migration {script.ScriptName} failed. SqlState={pgex.SqlState} Message={pgex.MessageText} Position={pgex.Position} Where={pgex.Where} InternalQuery={pgex.InternalQuery}");
					}
					else
					{
						Logger.Log($"Migration {script.ScriptName} failed.", ex);
					}
					return MigrationResult.Failed(script.Version, script.ScriptName, ex);
				}
			}
		}

		private static bool IsPotentiallyDestructive(string sql)
		{
			if (string.IsNullOrWhiteSpace(sql))
			{
				return false;
			}

			var text = sql.ToLowerInvariant();

			// Intentionally limit this to operations that can drop user data or core tables.
			// Dropping views/materialized views/functions is allowed by default as these are
			// derived objects over the core metrics tables.
			if (text.Contains("drop table ") || text.Contains("drop schema "))
			{
				return true;
			}

			if (text.Contains("truncate table "))
			{
				return true;
			}

			var alterIndex = text.IndexOf("alter table ", StringComparison.Ordinal);
			if (alterIndex >= 0)
			{
				var tail = text.Substring(alterIndex);
				if (tail.Contains(" drop column"))
				{
					return true;
				}
			}

			return false;
		}

		private static string ComputeChecksum(string content)
		{
			using (var sha = SHA256.Create())
			{
				var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);
				var hash = sha.ComputeHash(bytes);
				return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
			}
		}
	}

	public sealed class BootstrapResult
	{
		public List<MigrationResult> Migrations { get; } = new List<MigrationResult>();
		public string ErrorMessage { get; set; }
		public Exception Exception { get; set; }

		public bool Success
		{
			get
			{
				if (Exception != null)
				{
					return false;
				}

				return Migrations.All(m =>
					m.Status != MigrationStatus.Failed &&
					m.Status != MigrationStatus.ChecksumMismatch);
			}
		}
	}

	public sealed class MigrationResult
	{
		public string Version { get; private set; }
		public string ScriptName { get; private set; }
		public MigrationStatus Status { get; private set; }
		public string Message { get; private set; }
		public Exception Exception { get; private set; }

		private MigrationResult() { }

		public static MigrationResult Applied(string version, string scriptName)
		{
			return new MigrationResult
			{
				Version = version,
				ScriptName = scriptName,
				Status = MigrationStatus.Applied,
				Message = "Applied"
			};
		}

		public static MigrationResult Skipped(string version, string scriptName, string reason)
		{
			return new MigrationResult
			{
				Version = version,
				ScriptName = scriptName,
				Status = MigrationStatus.Skipped,
				Message = reason
			};
		}

		public static MigrationResult ChecksumMismatch(string version, string scriptName, string existingChecksum, string newChecksum)
		{
			var message = $"Checksum mismatch for {scriptName}. Existing: {existingChecksum}; Current: {newChecksum}.";
			return new MigrationResult
			{
				Version = version,
				ScriptName = scriptName,
				Status = MigrationStatus.ChecksumMismatch,
				Message = message,
				Exception = new InvalidOperationException(message)
			};
		}

		public static MigrationResult Failed(string version, string scriptName, Exception exception)
		{
			return new MigrationResult
			{
				Version = version,
				ScriptName = scriptName,
				Status = MigrationStatus.Failed,
				Message = exception?.Message,
				Exception = exception
			};
		}
	}

	public enum MigrationStatus
	{
		Applied,
		Skipped,
		ChecksumMismatch,
		Failed
	}

	/// <summary>
	/// Describes the planned state of a migration script before execution.
	/// Used by CLI tooling for dry-run reporting.
	/// </summary>
	public enum MigrationPlanStatus
	{
		/// <summary>Script has not been recorded in schema_version and would be applied.</summary>
		PendingApply,
		/// <summary>Script checksum matches an entry in schema_version.</summary>
		AlreadyApplied,
		/// <summary>Script version exists in schema_version but checksum differs.</summary>
		ChecksumMismatch
	}

	/// <summary>
	/// Lightweight view of a migration script and its relationship to the target database.
	/// </summary>
	public sealed class MigrationPlanItem
	{
		public string Version { get; set; }
		public string ScriptName { get; set; }
		public string Checksum { get; set; }
		public string ExistingChecksum { get; set; }
		public string ExistingScriptName { get; set; }
		public DateTime? AppliedAt { get; set; }
		public bool IsPotentiallyDestructive { get; set; }
		public MigrationPlanStatus Status { get; set; }
	}

	internal sealed class MigrationScript
	{
		public string Version { get; set; }
		public string ScriptName { get; set; }
		public string Content { get; set; }
	}

	internal sealed class SchemaVersionEntry
	{
		public string Version { get; set; }
		public string Checksum { get; set; }
		public string ScriptName { get; set; }
		public DateTime AppliedAt { get; set; }
	}
}

