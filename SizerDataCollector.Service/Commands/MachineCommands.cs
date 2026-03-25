using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SizerDataCollector.Core.Commissioning;
using SizerDataCollector.Core.Config;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Sizer;

namespace SizerDataCollector.Service.Commands
{
	internal static class MachineCommands
	{
		public static int Run(string[] args, Dictionary<string, string> options)
		{
			var subCommand = args.Length > 0
				? (args[0] ?? string.Empty).Trim().ToLowerInvariant()
				: string.Empty;

			switch (subCommand)
			{
				case "list":
					return ListMachines();
				case "register":
					return Register(options);
				case "status":
					return ShowStatus(options);
				case "set-thresholds":
					return SetThresholds(options);
				case "set-settings":
					return SetSettings(options);
				case "grade-map":
					return GradeMap(options);
			case "commission":
				return Commission(options);
			case "show-quality-params":
				return ShowQualityParams(options);
			case "set-quality-params":
				return SetQualityParams(options);
			case "show-perf-params":
				return ShowPerfParams(options);
			case "set-perf-params":
				return SetPerfParams(options);
			case "show-bands":
				return ShowBands(options);
			case "set-band":
				return SetBand(options);
			case "remove-band":
				return RemoveBand(options);
			default:
					ShowMachineUsage();
					return 1;
			}
		}

		private static int ListMachines()
		{
			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new MachineSettingsRepository(config.TimescaleConnectionString);
			var machines = repo.GetMachinesAsync(CancellationToken.None).GetAwaiter().GetResult();

			if (machines == null || machines.Count == 0)
			{
				Console.WriteLine("No machines registered.");
				return 0;
			}

			Console.WriteLine($"Registered machines ({machines.Count}):");
			foreach (var m in machines)
			{
				Console.WriteLine($"  {m.SerialNo,-20} {m.Name}");
			}
			return 0;
		}

		private static int Register(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				return 1;
			}

			if (!options.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
			{
				Console.WriteLine("Missing required option: --name <machine_name>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new TimescaleRepository(config.TimescaleConnectionString);
			repo.UpsertMachineAsync(serial.Trim(), name.Trim(), CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Machine '{serial.Trim()}' registered as '{name.Trim()}'.");
			return 0;
		}

		private static int ShowStatus(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new CommissioningRepository(config.TimescaleConnectionString);
			var row = repo.GetAsync(serial.Trim()).GetAwaiter().GetResult();

			if (row == null)
			{
				Console.WriteLine($"No commissioning record for serial '{serial.Trim()}'.");
				return 1;
			}

			Console.WriteLine($"Commissioning status for '{serial.Trim()}':");
			Console.WriteLine($"  DB bootstrapped:      {FormatTimestamp(row.DbBootstrappedAt)}");
			Console.WriteLine($"  Sizer connected:      {FormatTimestamp(row.SizerConnectedAt)}");
			Console.WriteLine($"  Machine discovered:   {FormatTimestamp(row.MachineDiscoveredAt)}");
			Console.WriteLine($"  Grade mapping done:   {FormatTimestamp(row.GradeMappingCompletedAt)}");
			Console.WriteLine($"  Thresholds set:       {FormatTimestamp(row.ThresholdsSetAt)}");
			Console.WriteLine($"  Ingestion enabled:    {FormatTimestamp(row.IngestionEnabledAt)}");

			if (!string.IsNullOrWhiteSpace(row.Notes))
			{
				Console.WriteLine($"  Notes:                {row.Notes}");
			}

			Console.WriteLine($"  Last updated:         {row.UpdatedAt:u}");
			return 0;
		}

		private static int SetThresholds(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				return 1;
			}

			if (!options.TryGetValue("min-rpm", out var rpmRaw) ||
				!int.TryParse(rpmRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minRpm))
			{
				Console.WriteLine("Missing or invalid option: --min-rpm <integer>");
				return 1;
			}

			if (!options.TryGetValue("min-total-fpm", out var fpmRaw) ||
				!int.TryParse(fpmRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minTotalFpm))
			{
				Console.WriteLine("Missing or invalid option: --min-total-fpm <integer>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new ThresholdsRepository(config.TimescaleConnectionString);
			repo.UpsertAsync(serial.Trim(), minRpm, minTotalFpm, CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Thresholds set for '{serial.Trim()}': min_rpm={minRpm}, min_total_fpm={minTotalFpm}");
			return 0;
		}

		private static int SetSettings(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				return 1;
			}

			if (!options.TryGetValue("target-speed", out var speedRaw) ||
				!double.TryParse(speedRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetSpeed))
			{
				Console.WriteLine("Missing or invalid option: --target-speed <number>");
				return 1;
			}

			if (!options.TryGetValue("lane-count", out var lanesRaw) ||
				!int.TryParse(lanesRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var laneCount))
			{
				Console.WriteLine("Missing or invalid option: --lane-count <integer>");
				return 1;
			}

			if (!options.TryGetValue("target-pct", out var pctRaw) ||
				!double.TryParse(pctRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetPct))
			{
				Console.WriteLine("Missing or invalid option: --target-pct <number>");
				return 1;
			}

			if (!options.TryGetValue("recycle-outlet", out var outletRaw) ||
				!int.TryParse(outletRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var recycleOutlet))
			{
				Console.WriteLine("Missing or invalid option: --recycle-outlet <integer>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new MachineSettingsRepository(config.TimescaleConnectionString);
			repo.UpsertSettingsAsync(serial.Trim(), targetSpeed, laneCount, targetPct, recycleOutlet, CancellationToken.None)
				.GetAwaiter().GetResult();

			Console.WriteLine($"Settings saved for '{serial.Trim()}':");
			Console.WriteLine($"  Target speed:    {targetSpeed}");
			Console.WriteLine($"  Lane count:      {laneCount}");
			Console.WriteLine($"  Target %:        {targetPct}");
			Console.WriteLine($"  Recycle outlet:  {recycleOutlet}");
			return 0;
		}

		private static int GradeMap(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new MachineSettingsRepository(config.TimescaleConnectionString);

			if (options.ContainsKey("set"))
			{
				if (!options.TryGetValue("grade", out var gradeKey) || string.IsNullOrWhiteSpace(gradeKey))
				{
					Console.WriteLine("Missing required option: --grade <grade_key>");
					return 1;
				}

				if (!options.TryGetValue("category", out var catRaw) ||
					!int.TryParse(catRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var category) ||
					category < 0 || category > 3)
				{
					Console.WriteLine("Missing or invalid option: --category <0-3> (0=good, 1=peddler, 2=bad, 3=recycle)");
					return 1;
				}

				repo.UpsertGradeOverrideAsync(serial.Trim(), gradeKey.Trim(), category, true, "cli", CancellationToken.None)
					.GetAwaiter().GetResult();

				var catName = CategoryName(category);
				Console.WriteLine($"Grade override set: '{gradeKey.Trim()}' -> {category} ({catName}) for serial '{serial.Trim()}'");
				return 0;
			}

			var overrides = repo.GetGradeOverridesAsync(serial.Trim(), CancellationToken.None).GetAwaiter().GetResult();
			if (overrides == null || overrides.Count == 0)
			{
				Console.WriteLine($"No grade overrides for serial '{serial.Trim()}'.");
				return 0;
			}

			Console.WriteLine($"Grade overrides for '{serial.Trim()}' ({overrides.Count}):");
			Console.WriteLine($"  {"Grade Key",-30} {"Cat",4} {"Name",-10} {"Active",-7} {"By",-10}");
			Console.WriteLine($"  {new string('-', 30)} {new string('-', 4)} {new string('-', 10)} {new string('-', 7)} {new string('-', 10)}");

			foreach (var g in overrides)
			{
				var catName = g.DesiredCat.HasValue ? CategoryName(g.DesiredCat.Value) : "?";
				Console.WriteLine($"  {g.GradeKey,-30} {g.DesiredCat,4} {catName,-10} {g.IsActive,-7} {g.CreatedBy,-10}");
			}

			return 0;
		}

		private static int Commission(Dictionary<string, string> options)
		{
			if (!options.TryGetValue("serial", out var serial) || string.IsNullOrWhiteSpace(serial))
			{
				Console.WriteLine("Missing required option: --serial <serial_no>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			Console.WriteLine($"Running commissioning check for '{serial.Trim()}'...");
			Console.WriteLine();

			var connStr = config.TimescaleConnectionString;
			var repository = new CommissioningRepository(connStr);
			var introspector = new DbIntrospector(connStr);
			Func<ISizerClient> clientFactory = () => new SizerClient(config);
			var service = new CommissioningService(connStr, repository, introspector, clientFactory);
			var status = service.BuildStatusAsync(serial.Trim(), CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"  DB bootstrapped:      {status.DbBootstrapped}");
			Console.WriteLine($"  Sizer connected:      {status.SizerConnected}");
			Console.WriteLine($"  Machine discovered:   {status.MachineDiscovered}");
			Console.WriteLine($"  Thresholds set:       {status.ThresholdsSet}");
			Console.WriteLine($"  Grade mapping done:   {status.GradeMappingCompleted}");
			Console.WriteLine($"  Can enable ingestion: {status.CanEnableIngestion}");

			if (status.BlockingReasons != null && status.BlockingReasons.Count > 0)
			{
				Console.WriteLine();
				Console.WriteLine("  Blocking reasons:");
				foreach (var reason in status.BlockingReasons)
				{
					Console.WriteLine($"    [{reason.Code}] {reason.Message}");
				}
			}

			Console.WriteLine();
			Console.WriteLine(status.CanEnableIngestion
				? "  Result: READY for ingestion"
				: "  Result: NOT READY for ingestion");

			return status.CanEnableIngestion ? 0 : 1;
		}

		private static int ShowQualityParams(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;
			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			var row = repo.GetQualityParamsAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

			if (row == null)
			{
				Console.WriteLine($"No custom quality params for '{serial}'. Using defaults:");
				Console.WriteLine("  tgt_good=0.75  tgt_peddler=0.15  tgt_bad=0.05  tgt_recycle=0.05");
				Console.WriteLine("  w_good=0.40    w_peddler=0.20    w_bad=0.20    w_recycle=0.20");
				Console.WriteLine("  sig_k=4.0");
				return 0;
			}

			Console.WriteLine($"Quality params for '{serial}':");
			Console.WriteLine($"  tgt_good:    {row.TgtGood}");
			Console.WriteLine($"  tgt_peddler: {row.TgtPeddler}");
			Console.WriteLine($"  tgt_bad:     {row.TgtBad}");
			Console.WriteLine($"  tgt_recycle: {row.TgtRecycle}");
			Console.WriteLine($"  w_good:      {row.WGood}");
			Console.WriteLine($"  w_peddler:   {row.WPeddler}");
			Console.WriteLine($"  w_bad:       {row.WBad}");
			Console.WriteLine($"  w_recycle:   {row.WRecycle}");
			Console.WriteLine($"  sig_k:       {row.SigK}");
			Console.WriteLine($"  Updated:     {row.UpdatedAt:u}");
			return 0;
		}

		private static int SetQualityParams(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;
			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			var existing = repo.GetQualityParamsAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

			var tgtGood    = ParseDecimalOpt(options, "tgt-good",    existing?.TgtGood    ?? 0.75m);
			var tgtPeddler = ParseDecimalOpt(options, "tgt-peddler", existing?.TgtPeddler ?? 0.15m);
			var tgtBad     = ParseDecimalOpt(options, "tgt-bad",     existing?.TgtBad     ?? 0.05m);
			var tgtRecycle = ParseDecimalOpt(options, "tgt-recycle", existing?.TgtRecycle ?? 0.05m);
			var wGood      = ParseDecimalOpt(options, "w-good",      existing?.WGood      ?? 0.40m);
			var wPeddler   = ParseDecimalOpt(options, "w-peddler",   existing?.WPeddler   ?? 0.20m);
			var wBad       = ParseDecimalOpt(options, "w-bad",       existing?.WBad       ?? 0.20m);
			var wRecycle   = ParseDecimalOpt(options, "w-recycle",   existing?.WRecycle   ?? 0.20m);
			var sigK       = ParseDecimalOpt(options, "sig-k",       existing?.SigK       ?? 4.0m);

			repo.UpsertQualityParamsAsync(serial, tgtGood, tgtPeddler, tgtBad, tgtRecycle,
				wGood, wPeddler, wBad, wRecycle, sigK, CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Quality params saved for '{serial}'.");
			return 0;
		}

		private static int ShowPerfParams(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;
			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			var row = repo.GetPerfParamsAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

			if (row == null)
			{
				Console.WriteLine($"No custom perf params for '{serial}'. Using defaults:");
				Console.WriteLine("  min_effective_fpm=3  low_ratio_threshold=0.5  cap_asymptote=0.2");
				return 0;
			}

			Console.WriteLine($"Performance params for '{serial}':");
			Console.WriteLine($"  min_effective_fpm:   {row.MinEffectiveFpm}");
			Console.WriteLine($"  low_ratio_threshold: {row.LowRatioThreshold}");
			Console.WriteLine($"  cap_asymptote:       {row.CapAsymptote}");
			Console.WriteLine($"  Updated:             {row.UpdatedAt:u}");
			return 0;
		}

		private static int SetPerfParams(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;
			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			var existing = repo.GetPerfParamsAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

			var minEff   = ParseDecimalOpt(options, "min-effective",  existing?.MinEffectiveFpm   ?? 3m);
			var lowRatio = ParseDecimalOpt(options, "low-ratio",      existing?.LowRatioThreshold ?? 0.5m);
			var capAsym  = ParseDecimalOpt(options, "cap-asymptote",  existing?.CapAsymptote      ?? 0.2m);

			repo.UpsertPerfParamsAsync(serial, minEff, lowRatio, capAsym, CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Performance params saved for '{serial}'.");
			return 0;
		}

		private static int ShowBands(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;
			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			var bands = repo.GetBandsAsync(serial, CancellationToken.None).GetAwaiter().GetResult();

			if (bands == null || bands.Count == 0)
			{
				Console.WriteLine($"No band definitions for '{serial}'. classify_oee_value will use hardcoded defaults.");
				return 0;
			}

			Console.WriteLine($"OEE band definitions for '{serial}' ({bands.Count}):");
			Console.WriteLine($"  {"Band",-18} {"Lower",8} {"Upper",8} {"Active",-7} {"Date",-12} {"By",-6}");
			Console.WriteLine($"  {new string('-', 18)} {new string('-', 8)} {new string('-', 8)} {new string('-', 7)} {new string('-', 12)} {new string('-', 6)}");

			foreach (var b in bands)
			{
				Console.WriteLine($"  {b.BandName,-18} {b.LowerBound,8:F4} {b.UpperBound,8:F4} {b.IsActive,-7} {b.EffectiveDate:yyyy-MM-dd} {b.CreatedBy,-6}");
			}
			return 0;
		}

		private static int SetBand(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;

			if (!options.TryGetValue("band", out var bandName) || string.IsNullOrWhiteSpace(bandName))
			{
				Console.WriteLine("Missing required option: --band <name>");
				return 1;
			}

			if (!options.TryGetValue("lower", out var lowerRaw) ||
				!decimal.TryParse(lowerRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lower))
			{
				Console.WriteLine("Missing or invalid option: --lower <0.0-1.0>");
				return 1;
			}

			if (!options.TryGetValue("upper", out var upperRaw) ||
				!decimal.TryParse(upperRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var upper))
			{
				Console.WriteLine("Missing or invalid option: --upper <0.0-1.0>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			repo.UpsertBandAsync(serial, bandName.Trim(), lower, upper, CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Band '{bandName.Trim()}' set for '{serial}': [{lower:F4}, {upper:F4})");
			return 0;
		}

		private static int RemoveBand(Dictionary<string, string> options)
		{
			if (!RequireSerial(options, out var serial)) return 1;

			if (!options.TryGetValue("band", out var bandName) || string.IsNullOrWhiteSpace(bandName))
			{
				Console.WriteLine("Missing required option: --band <name>");
				return 1;
			}

			var config = LoadConfig();
			if (config == null) return 1;

			var repo = new OeeParamsRepository(config.TimescaleConnectionString);
			repo.DeactivateBandAsync(serial, bandName.Trim(), CancellationToken.None).GetAwaiter().GetResult();

			Console.WriteLine($"Band '{bandName.Trim()}' deactivated for '{serial}'.");
			return 0;
		}

		private static bool RequireSerial(Dictionary<string, string> options, out string serial)
		{
			if (options.TryGetValue("serial", out serial) && !string.IsNullOrWhiteSpace(serial))
			{
				serial = serial.Trim();
				return true;
			}

			Console.WriteLine("Missing required option: --serial <serial_no>");
			serial = null;
			return false;
		}

		private static decimal ParseDecimalOpt(Dictionary<string, string> options, string key, decimal fallback)
		{
			if (options.TryGetValue(key, out var raw) &&
				decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
			{
				return value;
			}
			return fallback;
		}

		private static string CategoryName(int cat)
		{
			switch (cat)
			{
				case 0: return "good";
				case 1: return "peddler";
				case 2: return "bad";
				case 3: return "recycle";
				default: return "unknown";
			}
		}

		private static string FormatTimestamp(DateTimeOffset? ts)
		{
			return ts.HasValue ? ts.Value.ToString("u") : "(not set)";
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

		private static void ShowMachineUsage()
		{
			Console.WriteLine("Usage: SizerDataCollector.Service.exe machine <command> [options]");
			Console.WriteLine("Commands:");
			Console.WriteLine("  list");
			Console.WriteLine("  register            --serial <sn> --name <name>");
			Console.WriteLine("  status              --serial <sn>");
			Console.WriteLine("  set-thresholds      --serial <sn> --min-rpm <val> --min-total-fpm <val>");
			Console.WriteLine("  set-settings        --serial <sn> --target-speed <val> --lane-count <val>");
			Console.WriteLine("                      --target-pct <val> --recycle-outlet <val>");
			Console.WriteLine("  grade-map           --serial <sn>                      (list overrides)");
			Console.WriteLine("  grade-map           --serial <sn> --set --grade <key> --category <0-3>");
			Console.WriteLine("  commission          --serial <sn>                      (full check)");
			Console.WriteLine("  show-quality-params --serial <sn>");
			Console.WriteLine("  set-quality-params  --serial <sn> [--tgt-good <v>] [--tgt-peddler <v>]");
			Console.WriteLine("                      [--tgt-bad <v>] [--tgt-recycle <v>] [--w-good <v>]");
			Console.WriteLine("                      [--w-peddler <v>] [--w-bad <v>] [--w-recycle <v>] [--sig-k <v>]");
			Console.WriteLine("  show-perf-params    --serial <sn>");
			Console.WriteLine("  set-perf-params     --serial <sn> [--min-effective <v>] [--low-ratio <v>]");
			Console.WriteLine("                      [--cap-asymptote <v>]");
			Console.WriteLine("  show-bands          --serial <sn>");
			Console.WriteLine("  set-band            --serial <sn> --band <name> --lower <val> --upper <val>");
			Console.WriteLine("  remove-band         --serial <sn> --band <name>");
		}
	}
}
