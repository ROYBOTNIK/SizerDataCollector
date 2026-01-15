using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Npgsql;
using NpgsqlTypes;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;
using SizerDataCollector.Core.Sizer;

namespace SizerDataCollector.Core.Commissioning
{
	public sealed class CommissioningService
	{
		private const string ThresholdsExistsSql = "SELECT EXISTS (SELECT 1 FROM oee.machine_thresholds WHERE serial_no = @serial_no);";

		private readonly string _connectionString;
		private readonly CommissioningRepository _repository;
		private readonly DbIntrospector _introspector;
		private readonly Func<ISizerClient> _sizerClientFactory;

		public CommissioningService(
			string connectionString,
			CommissioningRepository repository,
			DbIntrospector introspector,
			Func<ISizerClient> sizerClientFactory)
		{
			_connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
			_repository = repository ?? throw new ArgumentNullException(nameof(repository));
			_introspector = introspector ?? throw new ArgumentNullException(nameof(introspector));
			_sizerClientFactory = sizerClientFactory ?? throw new ArgumentNullException(nameof(sizerClientFactory));
		}

		public async Task<CommissioningStatus> BuildStatusAsync(string serialNo, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(serialNo))
			{
				throw new ArgumentException("Serial number must be provided.", nameof(serialNo));
			}

			var status = new CommissioningStatus
			{
				SerialNo = serialNo
			};

			// Ensure commissioning row exists and read stored timestamps/notes.
			await _repository.EnsureRowAsync(serialNo).ConfigureAwait(false);
			status.StoredRow = await _repository.GetAsync(serialNo).ConfigureAwait(false);
			status.MachineDiscovered = status.StoredRow?.MachineDiscoveredAt != null;
			status.GradeMappingCompleted = status.StoredRow?.GradeMappingCompletedAt != null;

			// Derive DB bootstrap status from DbIntrospector.
			var health = await _introspector.RunAsync(cancellationToken).ConfigureAwait(false);
			status.DbBootstrapped = health != null &&
				health.CanConnect &&
				health.TimescaleInstalled &&
				health.HasAllTables &&
				health.HasAllFunctions &&
				health.HasAllContinuousAggregates &&
				health.HasAllPolicies &&
				health.Exception == null &&
				string.IsNullOrWhiteSpace(health.PolicyCheckError);
			var dbReachable = health != null && health.CanConnect && health.Exception == null;

			AddDbConnectivityReasons(status, health);

			// Probe Sizer for connectivity and serial alignment.
			status.SizerConnected = await ProbeSizerAsync(serialNo, status, cancellationToken).ConfigureAwait(false);

			// Validate thresholds exist for the serial number.
			if (dbReachable && status.DbBootstrapped)
			{
				status.ThresholdsSet = await CheckThresholdsAsync(serialNo, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				status.ThresholdsSet = dbReachable;
			}

			if (status.DbBootstrapped && !status.ThresholdsSet)
			{
				status.BlockingReasons.Add(new CommissioningReason("THRESHOLDS_MISSING", "Machine thresholds not set for this serial number."));
			}

			// Gating rule for ingestion.
			status.CanEnableIngestion = dbReachable && status.SizerConnected && status.ThresholdsSet;
			if (!status.CanEnableIngestion && status.BlockingReasons.Count == 0)
			{
				status.BlockingReasons.Add(new CommissioningReason("INGESTION_DISABLED", "Ingestion cannot be enabled until prerequisites are satisfied."));
			}

			return status;
		}

		private async Task<bool> ProbeSizerAsync(string expectedSerial, CommissioningStatus status, CancellationToken cancellationToken)
		{
			ISizerClient client = null;
			try
			{
				client = _sizerClientFactory();
				if (client == null)
				{
					throw new InvalidOperationException("Sizer client factory returned null.");
				}

				var reportedSerial = await client.GetSerialNoAsync(cancellationToken).ConfigureAwait(false);
				var machineName = await client.GetMachineNameAsync(cancellationToken).ConfigureAwait(false);

				if (string.IsNullOrWhiteSpace(reportedSerial) ||
				    !string.Equals(reportedSerial, expectedSerial, StringComparison.OrdinalIgnoreCase))
				{
					status.BlockingReasons.Add(new CommissioningReason("SIZER_SERIAL_MISMATCH", "Sizer connection succeeded but serial number did not match expected."));
					return false;
				}

				if (string.IsNullOrWhiteSpace(machineName))
				{
					status.BlockingReasons.Add(new CommissioningReason("SIZER_NO_NAME", "Sizer connection succeeded but machine name was empty."));
					return false;
				}

				return true;
			}
			catch (Exception ex)
			{
				Logger.Log("CommissioningService: Sizer probe failed.", ex);
				status.BlockingReasons.Add(new CommissioningReason("SIZER_TIMEOUT", "Sizer connection failed (see logs for details)."));
				return false;
			}
			finally
			{
				client?.Dispose();
			}
		}

		private async Task<bool> CheckThresholdsAsync(string serialNo, CancellationToken cancellationToken)
		{
			using (var connection = new NpgsqlConnection(_connectionString))
			{
				await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
				using (var command = new NpgsqlCommand(ThresholdsExistsSql, connection))
				{
					command.Parameters.Add("serial_no", NpgsqlDbType.Text).Value = serialNo;
					var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
					return result is bool exists && exists;
				}
			}
		}

		private static void AddDbConnectivityReasons(CommissioningStatus status, DbHealthReport health)
		{
			if (health == null)
			{
				status.BlockingReasons.Add(new CommissioningReason("DB_UNREACHABLE", "Database health unknown."));
				return;
			}

			if (!health.CanConnect || health.Exception != null)
			{
				status.BlockingReasons.Add(new CommissioningReason("DB_UNREACHABLE", "Cannot connect to database."));
			}
		}
	}
}
