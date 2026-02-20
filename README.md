# SizerDataCollector (CLI)

SizerDataCollector is a .NET Framework 4.8 console/CLI application that connects a Compac Sizer WCF service to a TimescaleDB/Postgres backend. It is designed to be driven by **cron jobs**, **Windows Scheduler**, or **automation agents** – no GUI, no prompts.

This repo also contains a WPF configuration UI and Windows service host as part of the broader `OptiFresh.OeeSuite` solution, but the `SizerDataCollector` console project is the CLI surface intended for autonomous operation.

---

## Features

- Configure runtime settings via `collector_config.json` (with App.config defaults)
- List and update which Sizer metrics are collected
- Run ingestion once (for cron) or in a continuous loop (for services)
- Run discovery probes/scans against Sizer WCF endpoints (JSON or text output)
- Run TimescaleDB health checks
- Apply schema/migration scripts with checksum tracking and dry‑run support
- Safe by default: potentially destructive DB changes are skipped unless explicitly allowed

For a deeper design overview, see **[DESIGN.md](./DESIGN.md)**.

---

## Requirements

- .NET Framework 4.8
- Network access to the Compac Sizer WCF endpoint
- Network access to a TimescaleDB/Postgres instance

> The console project is intended to run side‑by‑side with the service/WPF components on Windows. The codebase is maintained here under WSL for development, but builds/run happen on a Windows host with .NET 4.8 installed.

---

## Configuration

Runtime settings are loaded from:

1. `App.config` (defaults)
2. `%ProgramData%\Opti-Fresh\SizerDataCollector\collector_config.json` (overrides)
3. Legacy fallback: `collector_config.json` next to `SizerDataCollector.exe`

Core settings:

- Sizer WCF endpoint
  - `SizerHost`, `SizerPort`
  - `OpenTimeoutSec`, `SendTimeoutSec`, `ReceiveTimeoutSec`
- TimescaleDB connection
  - `TimescaleDb` connection string in `<connectionStrings>`
- Collector behaviour
  - `EnableIngestion`
  - `EnabledMetrics` (comma‑separated)
  - `PollIntervalSeconds`, `InitialBackoffSeconds`, `MaxBackoffSeconds`
  - `SharedDataDirectory` (for heartbeat JSON etc.)

You normally set initial defaults in `App.config`, then manage site‑specific overrides via the CLI.

---

## CLI Overview

After building `SizerDataCollector.csproj`, you can invoke the CLI directly:

```bash
SizerDataCollector <command> [subcommand] [options]
```

Supported commands:

- `config` – view/update runtime configuration
- `metrics` – list/update enabled metrics
- `db` – health checks and migrations
- `collector` – run ingestion
- `discovery` – Sizer endpoint probe/scan and endpoint apply
- `probe`, `single-poll` – legacy harness entrypoints (backwards compatible)

Run with no arguments or `help` to see a usage summary.

---

## Common Workflows

### 1. Configure connections

```bash
# Show effective runtime config
SizerDataCollector config show

# Set Sizer + Timescale connection and enable ingestion
SizerDataCollector config set \
  --sizer-host=10.155.155.10 \
  --sizer-port=8001 \
  --timescale-connection-string="Host=db-host;Port=5432;Username=postgres;Password=secret;Database=oee_prod;" \
  --enable-ingestion=true \
  --poll-interval-seconds=60 \
  --enabled-metrics=lanes_grade_fpm,lanes_size_fpm,machine_total_fpm,machine_cupfill,outlets_details \
  --log-level=Info \
  --diagnostic-mode=false \
  --log-as-json=false \
  --log-max-file-bytes=10485760 \
  --log-retention-days=14 \
  --log-max-files=100
```

Enable temporary diagnostic logging (agent/operator friendly):

```bash
# Enable debug-level diagnostics for 30 minutes, then auto-expire
SizerDataCollector config set --diagnostic-duration-minutes=30 --log-level=Debug

# Disable diagnostics immediately
SizerDataCollector config set --diagnostic-mode=false --diagnostic-until-utc=
```

### 2. Inspect and update metrics

```bash
# List metrics currently enabled for collection
SizerDataCollector metrics list

# List all metrics the Sizer client can provide
SizerDataCollector metrics list-supported

# Override enabled metrics (idempotent)
SizerDataCollector metrics set --metrics=lanes_grade_fpm,lanes_size_fpm,machine_total_fpm
```

### 3. Check database health

```bash
# JSON health report (recommended for agents)
SizerDataCollector db health --format=json

# Human‑readable status
SizerDataCollector db health --format=text
```

The health report validates:

- Connectivity to TimescaleDB
- Timescale extension installed
- Expected tables, functions, continuous aggregates, and policies present
- Seed tables populated (thresholds, shift calendar)

### 4. Apply schema/migrations

Migrations are managed by `DbBootstrapper` using embedded SQL scripts under `SizerDataCollector.Core/db/Migrations` and tracked in `public.schema_version`.

Dry‑run (plan only):

```bash
SizerDataCollector db migrate --dry-run
```

Apply (additive by default):

```bash
# Safe mode – scripts with DROP TABLE/SCHEMA/TRUNCATE/ALTER ... DROP COLUMN are skipped
SizerDataCollector db migrate

# Explicitly allow potentially destructive scripts (use with care)
SizerDataCollector db migrate --allow-destructive
```

Per‑migration output is line‑based and easy to parse:

```text
MIGRATION version=V001__base_schema script=V001__base_schema.sql status=Applied message=Applied
MIGRATION version=V010__serial_aware_settings_functions script=V010__serial_aware_settings_functions.sql status=Skipped message=Already applied with matching checksum.
RESULT success=true applied=10 skipped=9 checksum_mismatch=0 failed=0
```

### 5. Run the collector

#### One‑shot (cron‑style)

```bash
# Run a single ingestion cycle
SizerDataCollector collector run-once

# Ignore commissioning gates and force ingestion
SizerDataCollector collector run-once --force
```

This runs one `CollectorEngine.RunSinglePollAsync` cycle, writes a heartbeat JSON file, logs to disk, and exits with a non‑zero code on error.

#### Continuous loop (service/systemd)

```bash
# Start continuous ingestion loop (respect commissioning state)
SizerDataCollector collector run-loop

# Force ingestion even if commissioning is incomplete
SizerDataCollector collector run-loop --force
```

Systemd example:

```ini
[Service]
ExecStart=/opt/OptiFresh/SizerDataCollector.exe collector run-loop
Restart=always
```

### 5b. Check runtime status (heartbeat + config)

```bash
# JSON status (recommended for agents)
SizerDataCollector status --format=json

# Text status
SizerDataCollector status --format=text

# Optional explicit heartbeat path
SizerDataCollector status --heartbeat-file="C:\ProgramData\Opti-Fresh\SizerCollector\heartbeat.json"
```

`status` reports current runtime config (including logging mode) plus the latest heartbeat payload and file metadata.
For service-hosted workloads, heartbeat also carries `SERVICE_STATE`/`SERVICE_STATE_REASON` so automation can detect `running`, `degraded`, `blocked`, or `stopping` conditions.

### 5c. Run production preflight checks

```bash
# Full preflight (recommended before first start/deploy)
SizerDataCollector preflight --format=json

# Skip network/DB checks when validating offline packaging
SizerDataCollector preflight --check-sizer=false --check-db=false --format=text

# Tune Sizer probe timeout for slower networks
SizerDataCollector preflight --timeout-ms=3000 --format=json
```

`preflight` validates:

- runtime settings load
- required config sanity (host/port)
- write access to shared data + runtime config directories
- optional Sizer connectivity probe
- optional DB health check

Exit codes:

- `0`: all required checks passed
- `2`: one or more required checks failed
- `1`: invalid command arguments

### 6. Run Sizer discovery and endpoint scan

```bash
# Run a deep discovery snapshot for the currently configured endpoint
SizerDataCollector discovery run --format=json > discovery_snapshot.json

# Probe one specific endpoint quickly (agent-friendly single-target check)
SizerDataCollector discovery probe --host=10.155.155.10 --port=8001 --timeout-ms=1500 --format=json

# Scan a subnet for candidate endpoints
SizerDataCollector discovery scan --subnet=10.155.155.0/24 --port=8001 --timeout-ms=1200 --concurrency=32 --max-found=5 --format=json

# Apply a discovered endpoint to runtime settings
SizerDataCollector discovery apply --host=10.155.155.10 --port=8001
```

`discovery run` prints a `MachineDiscoverySnapshot` for a single configured endpoint.  
`discovery probe` and `discovery scan` return deterministic endpoint results that are easy for AI agents to parse and reason about.

`discovery scan` target selection is explicit (exactly one required):

- `--subnet=<CIDR>` (example: `10.155.155.0/24`)
- `--range=<start-end>` (example: `10.155.155.10-10.155.155.80`)
- `--hosts=<h1,h2,...>` (example: `10.155.155.10,10.155.155.11`)

Safety and determinism defaults for agents:

- bounded timeout (`--timeout-ms`, default `1500`)
- bounded parallelism (`--concurrency`, default `32`, max `128`)
- bounded discovery results (`--max-found`, default `5`)
- host-count limit to prevent accidental huge scans (`4096` unless `--allow-large-scan=true`)
- no implicit config writes (`discovery apply` is explicit)

Discovery snapshot content includes:

- Serial number and machine name
- Timings per WCF call
- Raw responses for key metrics
- A summarised view of lanes, outlets, grade/size keys, etc.

Useful for commissioning, support, and offline analysis.

### 7. AI-agent workflow (recommended)

```bash
# 1) Find likely endpoints
SizerDataCollector discovery scan --subnet=10.155.155.0/24 --format=json

# 2) Apply the best candidate
SizerDataCollector discovery apply --host=10.155.155.10 --port=8001

# 3) Verify Sizer connectivity
SizerDataCollector collector probe

# 4) Verify DB connectivity/schema health
SizerDataCollector db health --format=json

# 5) Continue with commissioning if needed
SizerDataCollector commissioning status --serial=ABC123
```

Expected behavior for automation:

- `discovery probe` returns exit code `0` when a candidate is found, otherwise `2`
- `discovery scan` returns exit code `0` when scan execution succeeds (parse `summary`/`candidates` to decide next action)
- `discovery apply` returns `STATUS=OK` and writes `SIZER_HOST`/`SIZER_PORT` into runtime settings

---

## Commissioning, Grades, and Targets

### Commissioning a new machine (CLI‑only)

Assuming the DB schema has already been migrated (`SizerDataCollector db migrate`), a typical commissioning flow for a new serial might look like this:

```bash
# 1) Ensure a commissioning_status row exists
SizerDataCollector commissioning ensure-row --serial=ABC123

# 2) Configure machine settings and name
SizerDataCollector commissioning configure-machine \
  --serial=ABC123 \
  --name="Bravo 3" \
  --target-machine-speed=2200 \
  --lane-count=10 \
  --target-percentage=80 \
  --recycle-outlet=99

# 3) (Optional) Record commissioning notes
SizerDataCollector commissioning set-notes --serial=ABC123 --notes="Commissioned by Alice on 2025-01-10."

# 4) Set thresholds directly in the DB (via your preferred tooling)
#    or via an external admin tool, then verify commissioning status:
SizerDataCollector commissioning status --serial=ABC123

# 5) Enable ingestion once prerequisites are satisfied
SizerDataCollector commissioning enable-ingestion --serial=ABC123
```

If commissioning needs to be restarted for a serial (e.g. configuration changed significantly):

```bash
SizerDataCollector commissioning reset --serial=ABC123
```

This clears commissioning timestamps while preserving the row and any notes.

The `commissioning status` command combines information from:

- `commissioning_status` (timestamps & notes)
- `oee.machine_thresholds` (min RPM / FPM)
- `public.machine_settings` (target machine config)
- DB health (`DbIntrospector`) and Sizer connectivity (`SizerClient`)

All outputs are key=value lines that are easy for agents to parse.

### Grades and grade_to_cat

To inspect and work with grade categories and mappings:

```bash
# List category meanings
SizerDataCollector grades list-categories

# Resolve a grade key for a specific machine
SizerDataCollector grades resolve --serial=ABC123 --grade-key=BRAVO_E1

# Dump the grade_to_cat-related schema to SQL
SizerDataCollector grades dump-sql > grade_to_cat_schema.sql
```

- `grades resolve` wraps `oee.grade_to_cat` via `MachineSettingsRepository.ResolveCategoryAsync`.
- `grades dump-sql` concatenates the schema from the grade‑related migrations so operators can re‑apply it via `psql` if needed.

**Grade overrides (per-machine)**

Overrides let you tweak grade→category mapping for a specific machine without changing the global `grade_to_cat` logic:

```bash
# List overrides for a serial
SizerDataCollector grades list-overrides --serial=ABC123

# Set/override mapping for a specific grade key
SizerDataCollector grades set-override --serial=ABC123 --grade-key=BRAVO_E1 --category=2

# Remove an override (fall back to default grade_to_cat behaviour)
SizerDataCollector grades remove-override --serial=ABC123 --grade-key=BRAVO_E1
```

- Overrides are stored in `oee.grade_map` and are applied by the serial-aware `oee.grade_to_cat(p_serial_no, p_grade)` functions added in the migrations.

### Machine targets

Targets can be inspected and updated without touching the WPF UI:

```bash
# Read current targets and derived throughput
SizerDataCollector targets get --serial=ABC123

# Update targets
SizerDataCollector targets set \
  --serial=ABC123 \
  --target-machine-speed=2200 \
  --lane-count=10 \
  --target-percentage=80 \
  --recycle-outlet=99
```

Both commands are thin wrappers over `MachineSettingsRepository` and `oee.get_target_throughput`, and they follow the same key=value output pattern as the rest of the CLI.

---

## Applying ad-hoc SQL (`db apply-sql`)

For carefully controlled site-specific changes, you can run arbitrary SQL scripts against the TimescaleDB instance via:

```bash
SizerDataCollector db apply-sql --file ./script.sql [--allow-destructive] [--dry-run] [--label=site_hotfix_2026-02-18]
```

Key points:

- Scripts are inspected for potentially destructive operations (`DROP TABLE`, `DROP SCHEMA`, `TRUNCATE TABLE`, `ALTER TABLE ... DROP COLUMN`).
- Without `--allow-destructive`, destructive scripts are **blocked** from execution.
- `--dry-run` inspects and reports on the script without connecting to the DB.
- All execution happens inside a single transaction; failures roll back automatically.
- This path does **not** write to `public.schema_version`; use `db migrate` for versioned schema changes.

Example dry-run:

```bash
SizerDataCollector db apply-sql --file ./script.sql --dry-run
```

Example apply (safe mode):

```bash
SizerDataCollector db apply-sql --file ./script.sql
```

Example apply (explicitly allowing destructive operations):

```bash
SizerDataCollector db apply-sql --file ./drop_old_view.sql --allow-destructive
```

---

## Production Deploy Scripts (Windows)

PowerShell scripts are provided under `scripts/` for repeatable install/upgrade/rollback.

### Install or upgrade

```powershell
# Run as Administrator
powershell -ExecutionPolicy Bypass -File .\scripts\install-production.ps1
```

What this does:

- builds Release artifacts (unless `-SkipBuild`)
- backs up existing install directory
- deploys CLI + service binaries under `C:\Program Files\Opti-Fresh\SizerDataCollector`
- creates/updates Windows service (`SizerDataCollectorService`)
- runs `preflight` (unless `-SkipPreflight`)
- starts service (unless `-SkipStart`)

Useful flags:

- `-InstallRoot "C:\Program Files\Opti-Fresh\SizerDataCollector"`
- `-ServiceName "SizerDataCollectorService"`
- `-SkipBuild`
- `-SkipPreflight`
- `-SkipStart`

### Uninstall

```powershell
# Run as Administrator
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-production.ps1
```

Optional:

- `-RemoveInstallFolder` to also delete deployed binaries

### Rollback

```powershell
# Run as Administrator
powershell -ExecutionPolicy Bypass -File .\scripts\rollback-production.ps1 -BackupPath "C:\ProgramData\Opti-Fresh\SizerDataCollector\backups\install_YYYYMMDD_HHMMSS" -StartService
```

The `BackupPath` value is printed at the end of each successful install/upgrade.

---

## Legacy Harness Modes

The original harness behaviour is preserved for compatibility:

- `SizerDataCollector probe`
  - Ensures core Timescale tables (`machines`, `batches`, `metrics`) exist
  - Probes the Sizer service (`GetSerialNo`, `GetMachineName`)
- `SizerDataCollector single-poll`
  - Runs the collector in a continuous loop (legacy naming)

New automation should prefer the explicit `collector` subcommands.

---

## Agent Ops Contract (Exit + Event IDs)

Use these stable codes for automation and alert routing.

CLI exit code contract:

- `0` success / healthy
- `1` usage or validation error (bad flags/arguments)
- `2` operational failure (dependency, connectivity, health, or runtime checks)

Windows service Event Log IDs (`Application`, source `SizerDataCollectorService`):

- `1001` service startup failure
- `1002` service runtime fault entering degraded retry mode

For service automation, pair Event Log IDs with heartbeat `SERVICE_STATE`/`SERVICE_STATE_REASON` from `SizerDataCollector status --format=json`.

---

## Project Layout (CLI‑relevant)

- `Program.cs` – CLI entrypoint and command routing
- `Config/CollectorConfig.cs` – configuration model & defaults
- `Config/CollectorSettingsProvider.cs` – JSON runtime settings loader/saver
- `Collector/CollectorEngine.cs` – single ingestion cycle
- `Collector/CollectorRunner.cs` – continuous ingestion loop with backoff
- `Db/TimescaleRepository.cs` – core metrics/batch persistence
- `Db/DbBootstrapper.cs` – migrations & schema_version tracking
- `Db/DbIntrospector.cs` – health checks
- `Sizer/SizerClient.cs` – WCF client and metric catalog
- `SizerDataCollector.Core/Sizer/Discovery` – Sizer discovery runner

For commissioning, band thresholds, machine settings, and grade overrides, see the additional repositories under `SizerDataCollector.Core/db` and the WPF `SettingsViewModel`. Those workflows are not yet exposed as CLI commands but can be added following the same patterns used here.
