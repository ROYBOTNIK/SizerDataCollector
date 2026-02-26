## SizerDataCollector

### Purpose

SizerDataCollector is part of the **OPTI-FRESH OEE Suite**. It provides:

- **`SizerDataCollector.Service`**: a Windows service (`SizerDataCollectorService`) plus a rich CLI for:
  - Installing, uninstalling, starting, stopping, and restarting the service.
  - Configuring connectivity to the Sizer API and TimescaleDB/Postgres.
  - Managing the TimescaleDB schema using **single authoritative SQL definition files** (no numbered migrations).
  - Commissioning machines and configuring OEE parameters (quality, performance, bands) per serial number.
- **`SizerDataCollector`** (console): a smaller console app used as an early connectivity probe. It is no longer the primary entry point for operating the system.

### Projects in this solution

- **`SizerDataCollector.Service`**: Windows service executable and CLI entry point.
- **`SizerDataCollector.Core`**: shared domain logic (config, DB access, OEE repositories, Sizer API client).
- **`SizerDataCollector`**: legacy console probe.
- **`Installer_SizerDataCollector`**: installer project for the Windows service.
- **`SizerDataCollector.GUI.WPF`**: legacy WPF UI (decoupled; not required for normal operation).

### Requirements

- **Runtime**
  - **.NET Framework 4.8**
  - Windows account with rights to install and control services.
- **Connectivity**
  - Network access to the **Sizer API** endpoint.
  - Access to a **TimescaleDB/Postgres** instance (e.g. `sizer_metrics_staging`).

### Configuration overview

- **Service configuration (`SizerDataCollector.Service/app.config`)**
  - `connectionStrings`:
    - `TimescaleDb`: Postgres connection string used by the service and CLI, for example:  
      `Host=127.0.0.1;Port=5432;Username=postgres;Password=root;Database=sizer_metrics_staging;`
  - `appSettings`:
    - `LogDirectory`: base directory for log files. If empty, logs default to `<exe>\logs`.

- **Runtime settings (`collector_config.json`)**
  - Managed via `CollectorSettingsProvider` from the CLI.
  - Key properties:
    - `SizerHost`, `SizerPort`, `OpenTimeoutSec`, `SendTimeoutSec`, `ReceiveTimeoutSec`
    - `TimescaleConnectionString`
    - `EnableIngestion`
    - `SharedDataDirectory`

- **Environment overrides**
  - `FORCE_ENABLE_INGESTION=1` can be set to force ingestion even if configuration is disabled.

- **SQL definition file resolution**
  - By default, SQL definitions are loaded from  
    `sql\definitions\schema.sql`, `functions.sql`, `continuous_aggregates.sql`, `views.sql`  
    under the service executable directory.
  - If `SharedDataDirectory` is set, matching files in  
    `<SharedDataDirectory>\sql\definitions\` take precedence.

### Quickstart: build and run

1. **Build the solution**
   - Open `OptiFresh.OeeSuite.sln` in Visual Studio and build in `Release` or `Debug`.
2. **Open an elevated command prompt**
   - Navigate to the `SizerDataCollector.Service` output directory (e.g. `bin\Release`).
3. **Configure connections**
   - Set Sizer API and database connection:
     - `SizerDataCollector.Service.exe set-sizer --host 10.155.155.10 --port 8001`
     - `SizerDataCollector.Service.exe set-db --connection "Host=...;Port=5432;Username=...;Password=...;Database=sizer_metrics_staging;"`
   - (Optional) Set shared SQL definition directory:
     - `SizerDataCollector.Service.exe set-shared-dir --path "C:\ProgramData\Opti-Fresh\SizerCollector"`
4. **Initialize the database (from scratch or upgrade)**
   - `SizerDataCollector.Service.exe db init`
   - This applies:
     - `schema.sql` (schemas, tables, sequences, indexes, hypertables)
     - `functions.sql`
     - `continuous_aggregates.sql`
     - `views.sql`
   - All operations are **idempotent and safe to re-run**.
5. **Install and start the Windows service**
   - Install (requires Administrator):
     - `SizerDataCollector.Service.exe service install`
   - Start:
     - `SizerDataCollector.Service.exe service start`
   - The service is registered as `SizerDataCollectorService` with **Automatic (Delayed Start)**.

### CLI usage summary

All commands are run from the `SizerDataCollector.Service` executable directory.

- **General**
  - `SizerDataCollector.Service.exe`  
    Run as a Windows service (normal service host mode; no CLI subcommand).
  - `SizerDataCollector.Service.exe console`  
    Run the collector in the foreground until ENTER is pressed.
  - `SizerDataCollector.Service.exe show-config`  
    Display current runtime configuration from `collector_config.json`.
  - `SizerDataCollector.Service.exe configure`  
    Interactive configuration of Sizer API, DB, ingestion, and shared directory.
  - `SizerDataCollector.Service.exe set-sizer --host <host> --port <port> [--open-timeout <sec>] [--send-timeout <sec>] [--receive-timeout <sec>]`
  - `SizerDataCollector.Service.exe set-db --connection "Host=...;Database=...;Username=...;Password=..."`
  - `SizerDataCollector.Service.exe set-ingestion --enabled true|false`
  - `SizerDataCollector.Service.exe set-shared-dir --path "C:\ProgramData\Opti-Fresh\SizerCollector"`
  - `SizerDataCollector.Service.exe test-connections`  
    Tests Sizer API connectivity and attempts a TimescaleDB schema/bootstrap check.

- **Service management (Administrator)**
  - `SizerDataCollector.Service.exe service status`
  - `SizerDataCollector.Service.exe service install`
  - `SizerDataCollector.Service.exe service uninstall`
  - `SizerDataCollector.Service.exe service start [--timeout <seconds>]`
  - `SizerDataCollector.Service.exe service stop  [--timeout <seconds>]`
  - `SizerDataCollector.Service.exe service restart [--timeout <seconds>]`

- **Database management**
  - `SizerDataCollector.Service.exe db status`  
    High-level health report (tables, functions, CAGGs, policies, seed data).
  - `SizerDataCollector.Service.exe db init`  
    Create or update full schema from the definition files.
  - `SizerDataCollector.Service.exe db apply-functions`
  - `SizerDataCollector.Service.exe db apply-caggs`
  - `SizerDataCollector.Service.exe db apply-views`
  - `SizerDataCollector.Service.exe db apply-all`
  - `SizerDataCollector.Service.exe db list-functions`
  - `SizerDataCollector.Service.exe db list-views`
  - `SizerDataCollector.Service.exe db list-caggs`

- **Machine / OEE configuration**
  - `SizerDataCollector.Service.exe machine list`
  - `SizerDataCollector.Service.exe machine register --serial <sn> --name <name>`
  - `SizerDataCollector.Service.exe machine status --serial <sn>`
  - `SizerDataCollector.Service.exe machine set-thresholds --serial <sn> --min-rpm <val> --min-total-fpm <val>`
  - `SizerDataCollector.Service.exe machine set-settings --serial <sn> --target-speed <val> --lane-count <val> --target-pct <val> --recycle-outlet <val>`
  - `SizerDataCollector.Service.exe machine grade-map --serial <sn>`  
    List current grade overrides.
  - `SizerDataCollector.Service.exe machine grade-map --serial <sn> --set --grade <key> --category <0-3>`
  - `SizerDataCollector.Service.exe machine commission --serial <sn>`  
    Run a commissioning check without gating ingestion.
  - `SizerDataCollector.Service.exe machine show-quality-params --serial <sn>`
  - `SizerDataCollector.Service.exe machine set-quality-params --serial <sn> [--tgt-good <v>] [--tgt-peddler <v>] [--tgt-bad <v>] [--tgt-recycle <v>] [--w-good <v>] [--w-peddler <v>] [--w-bad <v>] [--w-recycle <v>] [--sig-k <v>]`
  - `SizerDataCollector.Service.exe machine show-perf-params --serial <sn>`
  - `SizerDataCollector.Service.exe machine set-perf-params --serial <sn> [--min-effective <v>] [--low-ratio <v>] [--cap-asymptote <v>]`
  - `SizerDataCollector.Service.exe machine show-bands --serial <sn>`
  - `SizerDataCollector.Service.exe machine set-band --serial <sn> --band <name> --lower <val> --upper <val>`
  - `SizerDataCollector.Service.exe machine remove-band --serial <sn> --band <name>`

### Logs and operational behaviour

- **Log locations**
  - Service and CLI logs are written to `LogDirectory` (from `app.config`), or to `<exe>\logs` if empty.
  - Typical filenames: `SizerCollector_YYYYMMDD.log`.

- **Network / Sizer API behaviour**
  - The Windows service no longer fails or idles at startup if the Sizer API is unreachable.
  - Instead, it starts normally and the internal `CollectorRunner` uses robust retry-with-backoff logic to connect when the network/API becomes available.

- **WPF UI status**
  - The WPF project (`SizerDataCollector.GUI.WPF`) remains in the repository for reference but is **not required** for normal installation, configuration, or operation.

