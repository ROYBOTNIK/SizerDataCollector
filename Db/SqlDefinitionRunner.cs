using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Db
{
	public sealed class SqlDefinitionRunner
	{
		private const string DefinitionsSubPath = @"sql\definitions";

		private readonly string _connectionString;
		private readonly string _sharedDataDirectory;

		public SqlDefinitionRunner(string connectionString, string sharedDataDirectory)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
			_sharedDataDirectory = sharedDataDirectory;
		}

		public async Task<ApplyResult> ApplyAsync(string fileName, CancellationToken cancellationToken)
		{
			var resolvedPath = ResolvePath(fileName);
			if (resolvedPath == null)
			{
				return ApplyResult.Failure(fileName, $"SQL file '{fileName}' not found in shared data dir or exe dir.");
			}

			Logger.Log($"SqlDefinitionRunner: applying '{resolvedPath}'");

			string sql;
			try
			{
				sql = File.ReadAllText(resolvedPath, Encoding.UTF8);
			}
			catch (Exception ex)
			{
				return ApplyResult.Failure(fileName, $"Failed to read '{resolvedPath}': {ex.Message}");
			}

			if (string.IsNullOrWhiteSpace(sql))
			{
				return ApplyResult.Failure(fileName, $"SQL file '{resolvedPath}' is empty.");
			}

			try
			{
				using (var connection = new NpgsqlConnection(_connectionString))
				{
					await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

					using (var command = new NpgsqlCommand(sql, connection))
					{
						command.CommandTimeout = 0;
						await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
					}
				}

				Logger.Log($"SqlDefinitionRunner: '{fileName}' applied successfully from '{resolvedPath}'");
				return ApplyResult.Success(fileName, resolvedPath);
			}
			catch (PostgresException pgex)
			{
				var detail = $"SqlState={pgex.SqlState} Message={pgex.MessageText} Position={pgex.Position}";
				Logger.Log($"SqlDefinitionRunner: '{fileName}' failed. {detail}");
				return ApplyResult.Failure(fileName, detail);
			}
			catch (Exception ex)
			{
				Logger.Log($"SqlDefinitionRunner: '{fileName}' failed.", ex);
				return ApplyResult.Failure(fileName, ex.Message);
			}
		}

		public string ResolvePath(string fileName)
		{
			if (!string.IsNullOrWhiteSpace(_sharedDataDirectory))
			{
				var sharedPath = Path.Combine(_sharedDataDirectory, DefinitionsSubPath, fileName);
				if (File.Exists(sharedPath))
				{
					return sharedPath;
				}
			}

			var exeDir = AppDomain.CurrentDomain.BaseDirectory;
			var exePath = Path.Combine(exeDir, DefinitionsSubPath, fileName);
			if (File.Exists(exePath))
			{
				return exePath;
			}

			return null;
		}
	}

	public sealed class ApplyResult
	{
		public string FileName { get; private set; }
		public string ResolvedPath { get; private set; }
		public bool Succeeded { get; private set; }
		public string ErrorMessage { get; private set; }

		public static ApplyResult Success(string fileName, string resolvedPath)
		{
			return new ApplyResult { FileName = fileName, ResolvedPath = resolvedPath, Succeeded = true };
		}

		public static ApplyResult Failure(string fileName, string errorMessage)
		{
			return new ApplyResult { FileName = fileName, Succeeded = false, ErrorMessage = errorMessage };
		}
	}
}
