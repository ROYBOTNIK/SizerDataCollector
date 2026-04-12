# Adaptive Thresholds Workflow

This document explains how adaptive thresholds work in this repo, which SQL objects now honor per-machine tuning, and how an external AI agent should safely inspect, update, validate, and roll back changes.

## Goal

The collector does **not** auto-tune quality or performance targets by itself.

Instead, the application now supports a workflow where another tool or AI agent can:

1. Read historical machine performance from the database.
2. Decide whether targets should be raised or lowered.
3. Update the machine-specific quality and performance parameters through the existing CLI.
4. Re-query the serial-aware OEE outputs to confirm the effect.

This keeps decision-making outside the collector while making the scoring functions tunable per machine.

## Parameters that can be tuned

### Quality sigmoid / targets

Stored in `oee.quality_params` per `serial_no`:

- `tgt_good`
- `tgt_peddler`
- `tgt_bad`
- `tgt_recycle`
- `w_good`
- `w_peddler`
- `w_bad`
- `w_recycle`
- `sig_k`

CLI:

```powershell
SizerDataCollector.Service.exe machine show-quality-params --serial <sn>
SizerDataCollector.Service.exe machine set-quality-params --serial <sn> --tgt-good <v> --tgt-peddler <v> --tgt-bad <v> --tgt-recycle <v> --w-good <v> --w-peddler <v> --w-bad <v> --w-recycle <v> --sig-k <v>
```

### Performance curve

Stored in `oee.perf_params` per `serial_no`:

- `min_effective_fpm`
- `low_ratio_threshold`
- `cap_asymptote`

CLI:

```powershell
SizerDataCollector.Service.exe machine show-perf-params --serial <sn>
SizerDataCollector.Service.exe machine set-perf-params --serial <sn> --min-effective <v> --low-ratio <v> --cap-asymptote <v>
```

### Related machine settings

Stored in `public.machine_settings` per `serial_no`:

- `target_machine_speed`
- `lane_count`
- `target_percentage`
- `recycle_outlet`

CLI:

```powershell
SizerDataCollector.Service.exe machine set-settings --serial <sn> --target-speed <v> --lane-count <v> --target-pct <v> --recycle-outlet <v>
```

## SQL behavior after this rollout

## Quality path

The authoritative quality views now call the serial-aware function:

```sql
oee.calc_quality_ratio_qv1(serial_no, good_qty, peddler_qty, bad_qty, recycle_qty)
```

That function reads `oee.quality_params` for the matching serial and falls back to the previous hardcoded defaults if no row exists.

### Serial-aware quality objects

- `oee.v_quality_minute_batch`
- `oee.v_quality_batch_components`
- `oee.v_quality_daily_batch`
- `oee.v_quality_daily_components`
- `oee.v_quality_minute_components`
- `public.minute_quality_view_qv1_old`
- `public.batch_grade_components_qv1`
- `public.daily_grade_components_qv1`

## Performance path

The serial-aware performance function is:

```sql
oee.calc_perf_ratio(serial_no, total_fpm, missed_fpm, recycle_fpm, target_fpm)
```

It reads `oee.perf_params` for the matching serial and falls back to the previous defaults if no row exists.

### Serial-aware performance objects

- `oee.cagg_throughput_minute_batch`
- `oee.cagg_throughput_daily_batch`
- `oee.v_throughput_minute_batch`
- `oee.v_throughput_daily_batch`
- `oee.cagg_oee_minute_batch`
- `oee.oee_minute_batch`
- `public.daily_throughput_components`

## Grade-map dependency

The adaptive quality work does **not** replace the existing category mapping flow.

These objects still rely on:

- `oee.grade_map`
- `oee.grade_to_cat(serial_no, grade_key)`

That means grade/category overrides continue to feed the good/peddler/bad/recycle quantities that the quality sigmoid consumes.

## Important compatibility note

Some older public throughput views still use non-serial wrappers and should be treated as legacy/global views for adaptive-threshold work:

- `public.v_throughput_daily`
- `public.v_throughput_minute`

For adaptive-threshold tooling, prefer:

- `public.daily_throughput_components`
- `oee.v_throughput_minute_batch`
- `oee.v_throughput_daily_batch`

## One-time rollout workflow

After deploying SQL changes from this repo, apply the authoritative definition files from the service output directory:

```powershell
SizerDataCollector.Service.exe db apply-functions
SizerDataCollector.Service.exe db apply-caggs
SizerDataCollector.Service.exe db apply-views
```

Or:

```powershell
SizerDataCollector.Service.exe db apply-all
```

This is required when the SQL definitions themselves change.

## Ongoing tuning workflow for an AI agent

The external AI agent should use this workflow.

### 1. Inspect current machine configuration

```powershell
SizerDataCollector.Service.exe machine show-quality-params --serial <sn>
SizerDataCollector.Service.exe machine show-perf-params --serial <sn>
SizerDataCollector.Service.exe machine set-settings --serial <sn> ...
```

Also inspect baseline outputs from the database for the target date range.

Recommended read surfaces:

- Quality: `oee.v_quality_minute_batch`, `oee.v_quality_daily_batch`
- Throughput: `public.daily_throughput_components`, `oee.v_throughput_minute_batch`

### 2. Capture rollback values first

Before changing anything, record:

- Existing `oee.quality_params` row for the serial, or note that it is absent.
- Existing `oee.perf_params` row for the serial, or note that it is absent.
- Baseline quality and throughput ratios for the chosen time window.

### 3. Update the machine-specific parameters

Use the CLI, not manual file edits.

Example:

```powershell
SizerDataCollector.Service.exe machine set-quality-params --serial BF458031 --tgt-good 0.78 --tgt-peddler 0.14 --tgt-bad 0.04 --tgt-recycle 0.04 --sig-k 5.5
SizerDataCollector.Service.exe machine set-perf-params --serial BF458031 --min-effective 5 --low-ratio 0.5 --cap-asymptote 0.15
```

### 4. Re-query serial-aware throughput views after perf changes

Quality views compute their ratio at query time, so they pick up new `oee.quality_params` immediately.

The preferred throughput read surfaces also compute the serial-aware performance ratio at query time, so these objects update immediately after `oee.perf_params` changes:

- `public.daily_throughput_components`
- `oee.v_throughput_minute_batch`
- `oee.v_throughput_daily_batch`
- `oee.cagg_oee_minute_batch`
- `oee.oee_minute_batch`

If you need the materialized `throughput_ratio` values inside the raw throughput CAGGs themselves to be regenerated, refresh the affected window:

```sql
CALL refresh_continuous_aggregate('oee.cagg_throughput_minute_batch', <from_ts>, <to_ts>);
CALL refresh_continuous_aggregate('oee.cagg_throughput_daily_batch', <from_day>, <to_day>);
```

Most adaptive-threshold tooling should read the serial-aware views above and avoid depending on the raw materialized `throughput_ratio` column in the underlying CAGGs.

### 5. Validate the effect

Re-query the same time window and compare:

- Did `quality_ratio` move in the expected direction?
- Did `throughput_ratio` move in the expected direction?
- Did the change affect only the intended serial?

### 6. Roll back if needed

Restore the saved parameter values through the same CLI.

If a row did not previously exist, remove it from the DB only if your tooling explicitly supports full-state cleanup.

## Safe operating rules

- Do not change SQL definitions from the external tuning workspace.
- Do not update runtime config JSON by hand; use CLI commands.
- Scope every adaptive-threshold update to one serial unless there is a deliberate fleet-wide rollout.
- Always capture a before/after snapshot for the same time range.
- Prefer non-production or staging DBs when validating a new tuning policy.
- If a required table, function, or CAGG is missing, stop and report it instead of creating objects automatically.

## Verification script in this repo

This repo includes a reversible smoke test script:

`scripts/verify-adaptive-thresholds.py`

What it does:

1. Reads the current DB from `collector_config.json`.
2. Captures baseline quality and throughput values.
3. Updates quality/perf params via the CLI.
4. Refreshes the throughput batch CAGGs.
5. Verifies the values changed.
6. Restores the original parameter state.

Example:

```powershell
python scripts/verify-adaptive-thresholds.py --serial BF458031
```

Optional explicit range:

```powershell
python scripts/verify-adaptive-thresholds.py --serial BF458031 --from 2026-04-01T00:00:00Z --to 2026-04-02T00:00:00Z
```

## Troubleshooting / stop conditions

Stop and report if any of these are missing:

- `oee.quality_params`
- `oee.perf_params`
- `oee.v_quality_minute_batch`
- `public.daily_throughput_components`
- `oee.cagg_throughput_minute_batch`
- `oee.cagg_throughput_daily_batch`
- `oee.grade_to_cat(text, text)`
- `oee.calc_quality_ratio_qv1(text, ...)`
- `oee.calc_perf_ratio(text, ...)`

Also stop if:

- the target serial has no data in the selected time window,
- the CLI cannot read the configured DB,
- throughput CAGG refresh fails,
- or the external agent cannot capture rollback values before editing parameters.
