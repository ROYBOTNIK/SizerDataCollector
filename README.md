# SizerDataCollector (CLI)

SizerDataCollector is a .NET Framework 4.8 console/CLI application that connects a Compac Sizer WCF service to a TimescaleDB/Postgres backend. It is designed to be driven by **cron jobs**, **Windows Scheduler**, or **automation agents** – no GUI, no prompts.

This repo also contains a WPF configuration UI and Windows service host as part of the broader `OptiFresh.OeeSuite` solution, but the `SizerDataCollector` console project is the CLI surface intended for autonomous operation.

---

## Features

- Configure runtime settings via `collector_config.json` (with App.config defaults)
- List and update which Sizer metrics are collected
- Run ingestion once (for cron) or in a continuous loop (for services)
- Run discovery probes against the Sizer WCF service (JSON output)
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
2. `collector_config.json` next to `SizerDataCollector.exe` (overrides)

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
- `discovery` – Sizer discovery probe
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
  --enabled-metrics=lanes_grade_fpm,lanes_size_fpm,machine_total_fpm,machine_cupfill,outlets_details
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

### 6. Run Sizer discovery

```bash
SizerDataCollector discovery run > discovery_snapshot.json
```

This runs `DiscoveryRunner` once and prints a JSON `MachineDiscoverySnapshot` containing:

- Serial number and machine name
- Timings per WCF call
- Raw responses for key metrics
- A summarised view of lanes, outlets, grade/size keys, etc.

Useful for commissioning, support, and offline analysis.

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

## Legacy Harness Modes

The original harness behaviour is preserved for compatibility:

- `SizerDataCollector probe`
  - Ensures core Timescale tables (`machines`, `batches`, `metrics`) exist
  - Probes the Sizer service (`GetSerialNo`, `GetMachineName`)
- `SizerDataCollector single-poll`
  - Runs the collector in a continuous loop (legacy naming)

New automation should prefer the explicit `collector` subcommands.

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
