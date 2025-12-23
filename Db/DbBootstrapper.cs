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

		public async Task<BootstrapResult> BootstrapAsync(CancellationToken cancellationToken)
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

					foreach (var script in scripts)
					{
						var checksum = ComputeChecksum(script.Content);

						if (applied.TryGetValue(script.Version, out var existing))
						{
							if (string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
							{
								result.Migrations.Add(MigrationResult.Skipped(script.Version, script.ScriptName, "Already applied with matching checksum."));
								continue;
							}

							var mismatch = MigrationResult.ChecksumMismatch(script.Version, script.ScriptName, existing.Checksum, checksum);
							result.Migrations.Add(mismatch);
							result.Exception = mismatch.Exception;
							result.ErrorMessage = mismatch.Message;
							return result;
						}

						var migrationResult = await ApplyMigrationAsync(connection, script, checksum, cancellationToken).ConfigureAwait(false);
						result.Migrations.Add(migrationResult);

						if (migrationResult.Status == MigrationStatus.Failed)
						{
							result.Exception = migrationResult.Exception;
							result.ErrorMessage = migrationResult.Message;
							return result;
						}
					}
				}
			}
			catch (Exception ex)
			{
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
					Logger.Log($"Migration {script.ScriptName} failed.", ex);
					return MigrationResult.Failed(script.Version, script.ScriptName, ex);
				}
			}
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

