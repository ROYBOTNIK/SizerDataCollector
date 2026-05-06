# Machine Downtime and Slowdown Event Workflow

This workflow explains how SizerDataCollector turns minute-level OEE data into persisted machine downtime and non-transition slowdown events.

Use this workflow when an operator asks about stops or slow running that are **not** primarily grower lot transitions. Lot transitions remain covered by `LOT_TRANSITION_WORKFLOW.md` and `oee.lot_transition_throughput_events`.

## What It Measures

The detector stores two event types:

- `oee.downtime_events`: contiguous windows where `availability_ratio` is at or below the configured downtime threshold.
- `oee.slowdown_events`: contiguous windows where throughput is below the configured slowdown threshold while the machine is still considered available enough to be running.

Both event tables store start/end time, duration, serial number, batch context when the full event belongs to one batch, average/min availability, average/min throughput, average/min FPM, average OEE score, the reason string, lot-transition overlap flag, an `explanation` JSON payload, model version, and delivery target.

## Data Sources

- `oee.v_operational_minute_batch`: canonical minute-level source for availability, throughput, OEE, total FPM, lot, variety, and batch context.
- `oee.lot_transition_throughput_events`: optional exclusion windows so changeover disruption does not get double-counted as generic downtime or slowdown.

The detector does not read raw `public.metrics` directly. It relies on the existing OEE continuous aggregates and views.

## Detection Logic

For each scan window and serial number:

1. Load minute rows from `oee.v_operational_minute_batch`.
2. Optionally mark minutes that fall inside saved lot-transition opportunity windows.
3. Classify downtime candidate minutes when `availability_ratio <= MachineEventDowntimeMaxAvailabilityRatio`.
4. Classify slowdown candidate minutes when:
   - `throughput_ratio <= MachineEventSlowdownMaxThroughputRatio`
   - `availability_ratio >= MachineEventSlowdownMinAvailabilityRatio` when availability is present
   - `total_fpm >= MachineEventSlowdownMinTotalFpm` when FPM is present
5. Merge adjacent candidate minutes into events, allowing gaps up to `MachineEventMergeGapMinutes`.
6. Drop windows shorter than `MachineEventMinDurationMinutes`.
7. Persist events idempotently on `(serial_no, start_ts, end_ts)`.

Default behavior excludes lot-transition windows, so the generic machine event tables focus on non-transition downtime and slowdowns.

## CLI Usage

Run from the `SizerDataCollector.Service` output directory.

Enable the background service loop:

```text
SizerDataCollector.Service.exe set-machine-events --enabled true --interval 15 --scan-hours 24
```

Tune detection thresholds:

```text
SizerDataCollector.Service.exe set-machine-events --downtime-max-availability 0 --slowdown-max-throughput 0.75 --slowdown-min-availability 0.5 --slowdown-min-fpm 100 --min-duration 3 --merge-gap 2 --exclude-lot-transitions true
```

Preview both downtime and slowdown events without inserting:

```text
SizerDataCollector.Service.exe machine-event scan --serial 140578 --day 2026-04-23 --no-persist
```

Persist only downtime events:

```text
SizerDataCollector.Service.exe downtime scan --serial 140578 --hours 24
```

Persist only slowdown events:

```text
SizerDataCollector.Service.exe slowdown scan --serial 140578 --hours 24
```

List saved events:

```text
SizerDataCollector.Service.exe machine-event list --serial 140578 --hours 72
SizerDataCollector.Service.exe downtime list --serial 140578 --day 2026-04-23
SizerDataCollector.Service.exe slowdown list --serial 140578 --day 2026-04-23 --format csv
```

Export CSV:

```text
SizerDataCollector.Service.exe machine-event export --serial 140578 --hours 168 --type both
```

## Reporting Guidance

Use these views for reports and dashboards:

- `oee.v_downtime_event_detail`
- `oee.v_slowdown_event_detail`
- `oee.v_machine_event_detail`

Good questions to answer:

- How many minutes was a machine fully unavailable outside lot transitions?
- Which batches had repeated slow running not explained by changeovers?
- How much non-transition slowdown time was driven by low throughput while availability remained acceptable?
- Are the same serials or batches repeatedly producing downtime events?

Interpretation guardrails:

- Downtime and slowdown events are interval summaries over OEE minute rows; validate unusual events against `oee.v_operational_minute_batch` and raw `public.metrics` before using them for high-stakes operational decisions.
- `slowdown_events` are not mutually exclusive with low OEE; they are specifically low-throughput windows that pass the configured minimum availability and FPM gates.
- If `MachineEventExcludeLotTransitions` is false, machine events may overlap lot-transition opportunity windows and should be reported with the `overlaps_lot_transition` flag.
- Very short interruptions are intentionally skipped unless `MachineEventMinDurationMinutes` is lowered.

## Key Files

- `SizerDataCollector.Core/AnomalyDetection/MachineEventAnalyzer.cs`: classification, merge, summary, and lot-transition exclusion logic.
- `SizerDataCollector.Core/AnomalyDetection/MachineEventConfig.cs`: runtime tuning values.
- `SizerDataCollector.Core/AnomalyDetection/MachineEventModels.cs`: report/event models.
- `SizerDataCollector.Core/AnomalyDetection/MachineEventDatabaseSink.cs`: idempotent DB inserts.
- `SizerDataCollector.Service/Commands/MachineEventCommands.cs`: scan, list, and export CLI commands.
- `SizerDataCollector.Service/SizerCollectorService.cs`: optional background scan loop.
- `SizerDataCollector.Service/sql/definitions/schema.sql`: `oee.downtime_events` and `oee.slowdown_events`.
- `SizerDataCollector.Service/sql/definitions/views.sql`: event detail views.
