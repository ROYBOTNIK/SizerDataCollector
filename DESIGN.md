# SizerDataCollector CLI Design

## Goals

Turn the existing SizerDataCollector console harness into an agent‑ and cron‑friendly CLI for:

- Configuring connections to Sizer and TimescaleDB
- Managing which metrics are collected
- Running the collector in non‑interactive mode (one‑shot or long‑running)
- Applying and inspecting database schema/migrations safely
- Running health checks and discovery probes

Key properties:

- **CLI only** – no prompts, no UI interaction
- **Idempotent** – commands may be re‑run safely
- **Additive by default** – destructive DB changes require an explicit flag
- **Parseable output** – simple line‑based text or JSON

The CLI is implemented inside the existing `SizerDataCollector` console project, using the shared logic in `SizerDataCollector.Core`.

---

## Top‑Level Commands

```bash
SizerDataCollector <command> [subcommand] [options]
```

Supported commands:

- `config` – view/update runtime configuration
- `metrics` – list and update enabled metrics
- `db` – DB health checks and migrations
- `collector` – run ingestion (probe / one‑shot / loop)
- `discovery` – run Sizer discovery probe
- `probe`, `single-poll` – legacy harness entrypoints (backwards compatible)

If invoked without arguments or with `help`, the CLI prints a concise usage summary.

---

## Configuration (`config`)

Configuration is stored in `%ProgramData%\Opti-Fresh\SizerDataCollector\collector_config.json` (via `CollectorSettingsProvider`) with defaults from `App.config`. A legacy file next to the executable is still read as a fallback.

### `config show`

Prints the effective runtime settings as key=value pairs:

```text
STATUS=OK
SIZER_HOST=10.155.155.10
SIZER_PORT=8001
OPEN_TIMEOUT_SEC=5
SEND_TIMEOUT_SEC=5
RECEIVE_TIMEOUT_SEC=5
ENABLE_INGESTION=False
POLL_INTERVAL_SECONDS=60
INITIAL_BACKOFF_SECONDS=10
MAX_BACKOFF_SECONDS=300
SHARED_DATA_DIRECTORY=C:\ProgramData\Opti-Fresh\SizerCollector
TIMESCALE_CONNECTION_STRING_CONFIGURED=True
ENABLED_METRICS=lanes_grade_fpm,lanes_size_fpm,machine_total_fpm,machine_cupfill,outlets_details
```

This is predictable and easy for agents to parse.

### `config set`

Updates `%ProgramData%\Opti-Fresh\SizerDataCollector\collector_config.json` non‑interactively:

```bash
SizerDataCollector config set \
  --sizer-host=10.155.155.10 \
  --sizer-port=8001 \
  --timescale-connection-string="Host=...;Port=...;Username=...;Password=...;Database=...;" \
  --enable-ingestion=true \
  --poll-interval-seconds=60 \
  --initial-backoff-seconds=10 \
  --max-backoff-seconds=300 \
  --enabled-metrics=lanes_grade_fpm,lanes_size_fpm,machine_total_fpm,machine_cupfill,outlets_details \
  --shared-data-directory=C:\ProgramData\Opti-Fresh\SizerCollector
```

Recognised keys (all via `--key=value`):

- `sizer-host` (string)
- `sizer-port` (int)
- `open-timeout-sec` (int)
- `send-timeout-sec` (int)
- `receive-timeout-sec` (int)
- `enable-ingestion` (bool: true/false/1/0/yes/no)
- `poll-interval-seconds` (int)
- `initial-backoff-seconds` (int)
- `max-backoff-seconds` (int)
- `shared-data-directory` (string)
- `timescale-connection-string` (string)
- `enabled-metrics` / `metrics` (comma‑separated list)

Behaviour:

- All parsing is **strict**; invalid values cause `ERROR:` output and **no file is written**.
- If no recognised keys are supplied, command fails with a clear error.
- On success, prints:

```text
STATUS=OK
MESSAGE=Runtime settings updated. Restart any running services/agents to apply.
```

---

## Metrics (`metrics`)

Metrics are stored in `CollectorRuntimeSettings.EnabledMetrics` and ultimately passed into `CollectorConfig.EnabledMetrics`.

The underlying catalog of supported metrics comes from `SizerClient.SupportedMetricKeys`, which exposes the keys of the WCF `MetricResolvers` map. This avoids hard‑coding metric names in multiple places.

### `metrics list` / `metrics list-enabled`

```bash
SizerDataCollector metrics list
```

Output:

```text
STATUS=OK
ENABLED_METRIC=lanes_grade_fpm
ENABLED_METRIC=lanes_size_fpm
ENABLED_METRIC=machine_total_fpm
ENABLED_METRIC=machine_cupfill
ENABLED_METRIC=outlets_details
```

### `metrics list-supported`

```bash
SizerDataCollector metrics list-supported
```

Output:

```text
STATUS=OK
SUPPORTED_METRIC=lanes_grade_fpm
SUPPORTED_METRIC=lanes_size_fpm
SUPPORTED_METRIC=machine_total_fpm
SUPPORTED_METRIC=machine_missed_fpm
SUPPORTED_METRIC=machine_recycle_fpm
SUPPORTED_METRIC=machine_cupfill
SUPPORTED_METRIC=machine_tph
SUPPORTED_METRIC=outlets_details
SUPPORTED_METRIC=machine_reject_fpm
SUPPORTED_METRIC=machine_dropped_fpm
SUPPORTED_METRIC=machine_packed_fpm
SUPPORTED_METRIC=machine_rods_pm
```

### `metrics set`

```bash
SizerDataCollector metrics set --metrics=lanes_grade_fpm,lanes_size_fpm,machine_total_fpm
```

Updates the `EnabledMetrics` list and reports:

```text
STATUS=OK
MESSAGE=Enabled metrics updated.
```

Errors (e.g. missing `--metrics`) are reported as `ERROR:` on stderr with exit code 1.

> Note: Metrics can also be updated via `config set --enabled-metrics=...`; `metrics set` is a focused convenience wrapper.

---

## Database (`db`)

All DB operations use the Timescale/Postgres connection string from `CollectorRuntimeSettings.TimescaleConnectionString`. If it is empty, commands fail with an actionable error and exit code 2.

### `db health`

Runs `DbIntrospector.RunAsync` and prints either JSON (default) or key=value.

```bash
SizerDataCollector db health [--format=json|text]
```

**JSON output (default):**

```bash
SizerDataCollector db health --format=json
```

Returns the `DbHealthReport` as pretty‑printed JSON, suitable for agents to parse.

**Text output:**

```bash
SizerDataCollector db health --format=text
```

Example:

```text
STATUS=OK
CAN_CONNECT=True
TIMESCALE_INSTALLED=True
APPLIED_MIGRATIONS=19
MISSING_TABLES=
MISSING_FUNCTIONS=
MISSING_CAGGS=
MISSING_POLICIES=
SEED_PRESENT=True
```

Exit codes:

- `0` – healthy (no missing objects, seed present, no exception)
- `2` – unhealthy or health check error

### `db migrate`

Applies schema/migration scripts provided by `DbBootstrapper`.

```bash
SizerDataCollector db migrate [--dry-run] [--allow-destructive]
```

Implementation notes:

- Uses `DbBootstrapper` with a new overload:
  - `BootstrapAsync(bool allowDestructive, bool dryRun, CancellationToken)`
- `allowDestructive=false` prevents execution of scripts that contain **potentially destructive** statements:
  - `DROP TABLE`, `DROP SCHEMA`
  - `TRUNCATE TABLE`
  - `ALTER TABLE ... DROP COLUMN`
- Dropping views / materialized views / functions is **not** treated as destructive in this context (they are derived from core tables and hold no primary data).

#### Dry‑run

Dry‑run uses `DbBootstrapper.PlanAsync` which returns a list of `MigrationPlanItem` objects describing the relationship between each script and the target DB (`PendingApply`, `AlreadyApplied`, `ChecksumMismatch`, and whether the script is potentially destructive).

Example:

```bash
SizerDataCollector db migrate --dry-run
```

Output:

```text
STATUS=OK
PLAN version=V001__base_schema script=V001__base_schema.sql status=AlreadyApplied destructive=False applied_at=2024-10-01 12:34:56Z
PLAN version=V019_fix_minute_quality_view_qv1_old script=V019_fix_minute_quality_view_qv1_old.sql status=PendingApply destructive=False applied_at=
```

No changes are made to the database in dry‑run mode.

#### Apply

```bash
SizerDataCollector db migrate
```

Applies migrations using `BootstrapAsync(allowDestructive:false, dryRun:false)` by default. Scripts flagged as potentially destructive are skipped unless `--allow-destructive` is specified.

Per‑migration output:

```text
MIGRATION version=V001__base_schema script=V001__base_schema.sql status=Applied message=Applied
MIGRATION version=V010__serial_aware_settings_functions script=V010__serial_aware_settings_functions.sql status=Skipped message=Already applied with matching checksum.
...
RESULT success=true applied=10 skipped=9 checksum_mismatch=0 failed=0
```

Exit codes:

- `0` – all migrations succeeded or were skipped safely
- `2` – any migration failed or checksum mismatch detected

> `ChecksumMismatch` is treated as a hard error: the CLI will not attempt to re‑apply a script whose content has changed without human review.

---

## Collector (`collector`)

Collector commands use existing `CollectorEngine`, `CollectorRunner`, and `TimescaleRepository` building blocks, with commissioning guardrails from `IsCommissioningEnabled`.

```bash
SizerDataCollector collector [probe|run-once|run-loop] [--force]
```

All collector commands:

- Load runtime settings via `CollectorSettingsProvider`
- Construct `CollectorConfig` and log effective settings
- Respect commissioning rules (disable ingestion if commissioning is incomplete) unless `--force` is supplied
- Write a JSON heartbeat to `<SharedDataDirectory>/heartbeat.json`

### `collector probe`

Equivalent to the legacy harness `probe` mode:

- Ensures Timescale schema via `DatabaseTester.TestAndInitialize`
- Calls `SizerClientTester.TestSizerConnection`

### `collector run-once`

Runs a **single ingestion cycle**:

- Builds `CollectorEngine` and `TimescaleRepository`
- Optional commissioning gate (unless `--force`)
- Calls `CollectorEngine.RunSinglePollAsync` once
- Writes a heartbeat snapshot and exits

Suitable for cron jobs like:

```bash
SizerDataCollector collector run-once --force
```

### `collector run-loop`

Runs the full long‑running ingestion loop:

- Uses `CollectorRunner.RunAsync` to perform repeated polls with interval and backoff settings
- Commissioning gate is applied once at startup (unless `--force`)
- Process runs until terminated by the host (e.g. systemd/service manager)

Example for systemd:

```bash
ExecStart=/path/to/SizerDataCollector collector run-loop
```

### Legacy modes

The original `Program.Main` behaviour is preserved via the `HarnessMode` detection:

- `SizerDataCollector probe` → legacy harness
- `SizerDataCollector single-poll` → legacy continuous collector loop

These are thin wrappers over the new implementations.

---

## Discovery (`discovery`)

Discovery now supports endpoint probing, bounded network scanning, and explicit endpoint apply.

### `discovery run`

```bash
SizerDataCollector discovery run [--format=json|text]
```

Runs a deep probe for the currently configured endpoint via `DiscoveryRunner` and prints a stable JSON/text result.

### `discovery probe`

```bash
SizerDataCollector discovery probe --host=<ip-or-hostname> [--port=8001] [--timeout-ms=1500] [--format=json|text]
```

Fast single-endpoint probe returning:

- `reachable` / `candidateFound`
- `serialNo` / `machineName` (if available)
- confidence and latency

Exit codes:

- `0` candidate found
- `2` endpoint probed but no candidate

### `discovery scan`

```bash
SizerDataCollector discovery scan --subnet=<CIDR>|--range=<start-end>|--hosts=<h1,h2,...> [--port=8001] [--timeout-ms=1500] [--concurrency=32] [--max-found=5] [--format=json|text]
```

Agent-safe defaults:

- bounded scan size (4096 hosts, or 65536 with `--allow-large-scan=true`)
- bounded concurrency (max 128)
- bounded per-endpoint timeout
- explicit target source (exactly one of subnet/range/hosts required)
- no config writes unless `discovery apply` is called

### `discovery apply`

```bash
SizerDataCollector discovery apply --host=<ip-or-hostname> [--port=8001]
```

Writes selected endpoint to runtime settings (`SizerHost`, `SizerPort`) through `CollectorSettingsProvider`.

---

## Output & Error Handling

General rules:

- **Success**: `STATUS=OK` or JSON with a `Healthy` flag, plus per‑item lines (`MIGRATION`, `PLAN`, `ENABLED_METRIC`, etc.)
- **Configuration / usage errors**: `ERROR: ...` to stderr, exit code `1`
- **Operational errors (DB unavailable, health check failure, migration failure)**: detailed log via `Logger.Log`, concise message to stderr, exit code `2`

This makes it straightforward for agents and schedulers to:

- Check exit codes for high‑level status
- Parse key=value lines or JSON for detailed state

---

## Safety Characteristics

- **Idempotent DB operations**:
  - Bootstrap/migration scripts are versioned and tracked in `public.schema_version` with checksums.
  - Re‑running `db migrate` safely skips already‑applied scripts.
- **Checksum protection**:
  - If a script’s content changes after being applied, further runs will **not** re‑apply it; they will report a checksum mismatch instead.
- **Destructive‑operation gating**:
  - Scripts containing `DROP TABLE`, `DROP SCHEMA`, `TRUNCATE TABLE`, or `ALTER TABLE ... DROP COLUMN` are considered potentially destructive and are skipped unless `--allow-destructive` is set.
  - Existing migrations only drop views/materialized views/functions; no base tables are dropped.
- **Commissioning guardrails**:
  - Collector commands honour commissioning state (via `IsCommissioningEnabled`) by default, preventing ingestion until the system has been properly bootstrapped and commissioned.
- **No interactive prompts**:
  - All inputs are via flags; the CLI never asks for confirmation.

---

## Commissioning (`commissioning`)

The commissioning CLI exposes key steps of the commissioning workflow that were previously only available via the WPF UI.

```bash
SizerDataCollector commissioning <subcommand> [options]
```

### `commissioning status`

```bash
SizerDataCollector commissioning status --serial=ABC123
```

Uses:

- `CommissioningService.BuildStatusAsync` (wraps `CommissioningRepository`, `DbIntrospector`, `SizerClient`)
- `ThresholdsRepository.GetAsync`
- `MachineSettingsRepository.GetSettingsAsync`

Reports commissioning state for the given serial as key=value lines:

```text
STATUS=OK
SERIAL=ABC123
DB_BOOTSTRAPPED=True
SIZER_CONNECTED=True
THRESHOLDS_SET=True
MACHINE_DISCOVERED=True
GRADE_MAPPING_COMPLETED=True
CAN_ENABLE_INGESTION=True
INGESTION_ENABLED=True
DB_BOOTSTRAPPED_AT=2025-01-10 12:00:00Z
SIZER_CONNECTED_AT=2025-01-10 12:05:00Z
MACHINE_DISCOVERED_AT=2025-01-10 12:10:00Z
GRADE_MAPPING_COMPLETED_AT=2025-01-10 12:20:00Z
THRESHOLDS_SET_AT=2025-01-10 12:15:00Z
INGESTION_ENABLED_AT=2025-01-10 12:30:00Z
THRESHOLDS_MIN_RPM=1700
THRESHOLDS_MIN_TOTAL_FPM=150
THRESHOLDS_UPDATED_AT=2025-01-10 12:15:00Z
TARGET_MACHINE_SPEED=2200
LANE_COUNT=10
TARGET_PERCENTAGE=80
RECYCLE_OUTLET=99
BLOCKING_REASON=...
```

- `INGESTION_ENABLED` is derived from `commissioning_status.ingestion_enabled_at`.
- Any blocking reasons from `CommissioningStatus.BlockingReasons` are printed as `BLOCKING_REASON=<code>:<message>`.

Exit codes:

- `0` – status computed successfully (even if commissioning is incomplete)
- `2` – DB/Sizer errors (exception, connectivity issues)

### `commissioning ensure-row`

```bash
SizerDataCollector commissioning ensure-row --serial=ABC123
```

Uses `CommissioningRepository.EnsureRowAsync(serial)` to create a commissioning row if missing. Idempotent.

Output:

```text
STATUS=OK
SERIAL=ABC123
MESSAGE=Ensured commissioning_status row.
```

### `commissioning mark-discovered`

```bash
SizerDataCollector commissioning mark-discovered --serial=ABC123 --timestamp=now
SizerDataCollector commissioning mark-discovered --serial=ABC123 --timestamp=2025-01-10T12:10:00Z
```

Uses `CommissioningRepository.MarkDiscoveredAsync(serial, discoveredAt)`.

- `--timestamp` accepts `now` or an ISO8601 timestamp.
- On success:

```text
STATUS=OK
SERIAL=ABC123
MACHINE_DISCOVERED_AT=2025-01-10 12:10:00Z
```

### `commissioning enable-ingestion`

```bash
SizerDataCollector commissioning enable-ingestion --serial=ABC123
```

Marks ingestion as enabled by setting `commissioning_status.ingestion_enabled_at`:

- Uses `CommissioningRepository.SetTimestampAsync(serial, "ingestion_enabled_at", nowUtc)`

Output:

```text
STATUS=OK
SERIAL=ABC123
INGESTION_ENABLED=True
INGESTION_ENABLED_AT=2025-01-10 12:30:00Z
```

### `commissioning configure-machine`

```bash
SizerDataCollector commissioning configure-machine \
  --serial=ABC123 \
  --name="Bravo 3" \
  --target-machine-speed=2200 \
  --lane-count=10 \
  --target-percentage=80 \
  --recycle-outlet=99
```

Uses:

- `MachineSettingsRepository.UpsertSettingsAsync(serial, targetSpeed, laneCount, targetPercentage, recycleOutlet)`
- `DatabaseTester.UpsertMachine(config, serial, name)` to upsert into `public.machines` (optional if `--name` is omitted)

Idempotent: upserts both machine settings and machine metadata.

Output:

```text
STATUS=OK
SERIAL=ABC123
MACHINE_NAME=Bravo 3
TARGET_MACHINE_SPEED=2200
LANE_COUNT=10
TARGET_PERCENTAGE=80
RECYCLE_OUTLET=99
```

### `commissioning set-notes`

```bash
SizerDataCollector commissioning set-notes --serial=ABC123 --notes="Commissioned by Alice on 2025-01-10."
```

Uses `CommissioningRepository.UpdateNotesAsync` to persist commissioning notes for the serial.

Output:

```text
STATUS=OK
SERIAL=ABC123
NOTES_SET=True
MESSAGE=Commissioning notes updated.
```

### `commissioning reset`

```bash
SizerDataCollector commissioning reset --serial=ABC123
```

Uses `CommissioningRepository.ResetAsync(serial, notes)` to clear commissioning timestamps/flags while preserving the row. Notes are left NULL by the CLI (WPF may choose to set its own).

Output:

```text
STATUS=OK
SERIAL=ABC123
MESSAGE=Commissioning status reset for serial.
```

---

## Grades (`grades`)

The grades CLI surfaces the `grade_to_cat` mapping and related schema.

```bash
SizerDataCollector grades <subcommand> [options]
```

### `grades list-categories`

Prints the meaning of each category integer:

```bash
SizerDataCollector grades list-categories
```

Output:

```text
STATUS=OK
CATEGORY=0 NAME=Good
CATEGORY=1 NAME=Gate/Peddler
CATEGORY=2 NAME=Bad
CATEGORY=3 NAME=Recycle
```

The mapping is maintained in a small in‑code dictionary, derived from the SQL grade bands (`good`, `peddler`, `bad`, `recycle`) and the WPF UI’s category labels.

### `grades resolve`

Resolves a grade key to a category for a given serial using `oee.grade_to_cat` via `MachineSettingsRepository.ResolveCategoryAsync`:

```bash
SizerDataCollector grades resolve --serial=ABC123 --grade-key=BRAVO_E1
```

Example output:

```text
STATUS=OK
SERIAL=ABC123
GRADE_KEY=BRAVO_E1
CATEGORY=0
CATEGORY_NAME=Good
```

If no mapping is found or the function returns NULL:

```text
STATUS=ERROR
ERROR_MESSAGE=No category mapping found.
```

Exit code: `0` on success, `2` on error.

### `grades dump-sql`

Emits the schema‑only grade_to_cat‑related objects as SQL to stdout so operators can re‑apply them via `psql` if needed.

```bash
SizerDataCollector grades dump-sql > grade_to_cat_schema.sql
```

Implementation details:

- Uses `DbBootstrapper.EnsureSqlFolderAsync` to materialize embedded migrations to `DbBootstrapper.MigrationPath` (no DB connection required).
- Reads and concatenates:
  - `V012__grade_map_overrides.sql`
  - `V015__serial_grade_to_cat.sql`
  - `V016__grade_to_cat_suffix_overrides.sql`
- Writes them as raw SQL with simple `-- BEGIN/END` comments.

Output is pure SQL (no `STATUS=` prefix) so it can be piped directly into `psql` or other tooling.

### Grade overrides

Overrides allow per‑machine tweaks on top of the default `grade_to_cat` behaviour, using the `oee.grade_map` table.

#### `grades list-overrides`

```bash
SizerDataCollector grades list-overrides --serial=ABC123
```

Uses `MachineSettingsRepository.GetGradeOverridesAsync(serial)` to list overrides for a machine.

Output (with overrides):

```text
STATUS=OK
SERIAL=ABC123
OVERRIDE GRADE_KEY=BRAVO_E1 CATEGORY=0
OVERRIDE GRADE_KEY=BRAVO_E2 CATEGORY=2
```

If no overrides are present:

```text
STATUS=OK
SERIAL=ABC123
MESSAGE=No overrides found for serial.
```

#### `grades set-override`

```bash
SizerDataCollector grades set-override --serial=ABC123 --grade-key=BRAVO_E1 --category=2
```

Uses `MachineSettingsRepository.UpsertGradeOverrideAsync(serial, gradeKey, category, isActive: true, createdBy: "cli")` to upsert an override row.

Output:

```text
STATUS=OK
SERIAL=ABC123
GRADE_KEY=BRAVO_E1
CATEGORY=2
CATEGORY_NAME=Bad
MESSAGE=Override saved.
```

#### `grades remove-override`

```bash
SizerDataCollector grades remove-override --serial=ABC123 --grade-key=BRAVO_E1
```

Uses `MachineSettingsRepository.RemoveGradeOverrideAsync(serial, gradeKey)` to delete the override, if present.

Output:

```text
STATUS=OK
SERIAL=ABC123
GRADE_KEY=BRAVO_E1
MESSAGE=Override removed (if it existed).
```

---

## Targets (`targets`)

Targets are derived from `public.machine_settings` and the `oee.get_target_throughput` function, exposed via `MachineSettingsRepository`.

```bash
SizerDataCollector targets <subcommand> [options]
```

### `targets get`

Reads the current machine target configuration and the derived throughput:

```bash
SizerDataCollector targets get --serial=ABC123
```

Uses:

- `MachineSettingsRepository.GetSettingsAsync(serial)`
- `MachineSettingsRepository.GetTargetThroughputAsync(serial)`

Example output:

```text
STATUS=OK
SERIAL=ABC123
TARGET_MACHINE_SPEED=2200
LANE_COUNT=10
TARGET_PERCENTAGE=80
RECYCLE_OUTLET=99
TARGET_THROUGHPUT=17600
```

If no settings row exists for the serial, the command returns:

```text
STATUS=ERROR
ERROR_MESSAGE=Machine settings not found for serial.
```

### `targets set`

Sets or updates the per‑machine targets via `MachineSettingsRepository.UpsertSettingsAsync`:

```bash
SizerDataCollector targets set \
  --serial=ABC123 \
  --target-machine-speed=2200 \
  --lane-count=10 \
  --target-percentage=80 \
  --recycle-outlet=99
```

Idempotent: re‑running the command updates the same row.

Output:

```text
STATUS=OK
SERIAL=ABC123
TARGET_MACHINE_SPEED=2200
LANE_COUNT=10
TARGET_PERCENTAGE=80
RECYCLE_OUTLET=99
TARGET_THROUGHPUT=17600
```

---

## `db apply-sql`

`db apply-sql` provides a CLI-based path for executing ad-hoc SQL scripts with the same connection as the rest of the tooling, while still running through our destructive-operation safety checks.

```bash
SizerDataCollector db apply-sql --file ./script.sql [--allow-destructive] [--dry-run] [--label=site_hotfix_2026-02-18]
```

Behaviour:

- `--file` must point to an existing SQL file.
- The script is read and inspected for potentially destructive statements (same rules as migrations: `DROP TABLE`, `DROP SCHEMA`, `TRUNCATE TABLE`, `ALTER TABLE ... DROP COLUMN`).
- **Dry-run** (`--dry-run`):
  - Does **not** connect to the DB or execute SQL.
  - Prints:

    ```text
    STATUS=OK
    FILE=/path/to/script.sql
    LABEL=...
    DRY_RUN=True
    POTENTIALLY_DESTRUCTIVE=True|False
    MESSAGE=Script validated (dry-run only).
    ```

  - If the script is potentially destructive and `--allow-destructive` is not supplied, the message is:

    ```text
    MESSAGE=Script would be treated as potentially destructive and requires --allow-destructive to execute.
    ```

- **Apply** (no `--dry-run`):
  - Requires a configured Timescale connection string.
  - If the script is potentially destructive and `--allow-destructive` is **not** present, execution is blocked and the command exits with:

    ```text
    STATUS=ERROR
    FILE=/path/to/script.sql
    POTENTIALLY_DESTRUCTIVE=True
    ERROR_MESSAGE=Script appears potentially destructive; rerun with --allow-destructive to execute.
    ```

  - Otherwise, the script is executed inside a single `NpgsqlTransaction`; on error the transaction is rolled back.

  - On success:

    ```text
    STATUS=OK
    FILE=/path/to/script.sql
    LABEL=...
    DRY_RUN=False
    POTENTIALLY_DESTRUCTIVE=True|False
    MESSAGE=Applied SQL script successfully.
    ```

> Note: `db apply-sql` does **not** write to `public.schema_version` and is intended for carefully controlled hotfixes or site-specific adjustments. Prefer `db migrate` for long-term, versioned schema changes.

---

## Future Extensions (Not yet implemented)

The design intentionally leaves room for additional, more specialised operations, for example:

- `db ensure-core-schema` – explicit wrapper around `DatabaseTester.TestAndInitialize`
- `db seed` – idempotent seeding of thresholds, calendars, and band definitions
- Machine/grade bulk‑import/export helpers built on the existing repositories

The current pass focuses on the core operational flows needed by cron jobs and agents: configuration, health, migrations, discovery, commissioning, and ingestion control.
