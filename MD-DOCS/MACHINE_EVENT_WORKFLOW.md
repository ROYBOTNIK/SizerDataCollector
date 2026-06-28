# Machine Downtime and Slowdown Event Workflow

This workflow explains how SizerDataCollector detects general machine downtime and non-transition slow running.

ELI5 version: this workflow watches normal production minutes. It separates "the machine was not really running" from "the machine was running, but fruit was moving too slowly." Lot changes are handled by `LOT_TRANSITION_WORKFLOW.md`.

## Downtime, Slowdown, And Lot Transition

| Event | Simple meaning | Main signal | Stored in |
| --- | --- | --- | --- |
| Downtime | The machine was unavailable or effectively stopped. | Low `availability_ratio` | `oee.downtime_events` |
| Slowdown | The machine was available, but throughput was too low. | Low `throughput_ratio` plus running gates | `oee.slowdown_events` |
| Lot transition | Fruit flow dipped because the grower lot/batch changed. | `machine_total_fpm` dip/recovery around batch change | `oee.lot_transition_throughput_events` |

Throughput means fruit moving through the machine. Availability means the machine was able to run. A slowdown needs both ideas: low throughput, but enough availability/FPM to prove this was running slowly rather than fully stopped.

## What It Measures

The detector stores two event types:

- `oee.downtime_events`: contiguous windows where `availability_ratio <= MachineEventDowntimeMaxAvailabilityRatio`.
- `oee.slowdown_events`: contiguous windows where `throughput_ratio <= MachineEventSlowdownMaxThroughputRatio` while availability and FPM show the machine was still running enough.

Both event tables store start/end time, duration, serial number, batch context, average/min availability, average/min throughput, average/min FPM, average OEE, reason, lot-transition overlap flag, explanation JSON, model version, and delivery target.

## Data Sources

- `oee.v_operational_minute_batch`: canonical minute-level source for availability, throughput, OEE, total FPM, lot, variety, and batch context.
- `oee.lot_transition_throughput_events`: optional exclusion windows so lot-transition disruption is not double-counted as generic downtime or slowdown.

This detector does not read raw `public.metrics` directly.

## Detection Logic

For each scan window and serial number:

1. Load minute rows from `oee.v_operational_minute_batch`.
2. Mark minutes that fall inside saved lot-transition opportunity windows when exclusion is enabled.
3. Classify downtime candidate minutes when availability is at or below the downtime threshold.
4. Classify slowdown candidate minutes when throughput is low, availability is high enough, and total FPM is high enough.
5. Merge adjacent candidate minutes, allowing gaps up to `MachineEventMergeGapMinutes`.
6. Drop windows shorter than `MachineEventMinDurationMinutes`.
7. Persist events idempotently on `(serial_no, start_ts, end_ts)`.

Default behavior excludes lot-transition windows. Usually keep `MachineEventExcludeLotTransitions = true` so a changeover is reported once as transition throughput opportunity loss, not again as a generic slowdown.

## CLI Usage

Run from the `SizerDataCollector.Service` output directory.

Enable the background loop:

```text
SizerDataCollector.Service.exe set-machine-events --enabled true --interval 15 --scan-hours 24
```

Tune detection:

```text
SizerDataCollector.Service.exe set-machine-events --downtime-max-availability 0 --slowdown-max-throughput 0.75 --slowdown-min-availability 0.5 --slowdown-min-fpm 100 --min-duration 3 --merge-gap 2 --exclude-lot-transitions true
```

Preview without inserting:

```text
SizerDataCollector.Service.exe machine-event scan --serial 140578 --day 2026-04-23 --no-persist
```

List or export:

```text
SizerDataCollector.Service.exe machine-event list --serial 140578 --hours 72
SizerDataCollector.Service.exe downtime list --serial 140578 --day 2026-04-23
SizerDataCollector.Service.exe slowdown list --serial 140578 --day 2026-04-23 --format csv
SizerDataCollector.Service.exe machine-event export --serial 140578 --hours 168 --type both
```

Restart the Windows service after `set-machine-events` changes so the background loop reloads runtime settings.

## Config Tuning

| Config | Default | What it means | Move up | Move down | Example |
| --- | ---: | --- | --- | --- | --- |
| `EnableMachineEventDetection` | `false` | Turns background downtime/slowdown detection on/off. | Enables detection when `true`. | Disables background detection when `false`. | `set-machine-events --enabled true` |
| `MachineEventEvalIntervalMinutes` | `15` | How often the service scans. | Less frequent, lower load, slower detection. | More frequent, faster detection, more DB work. | `set-machine-events --interval 10` |
| `MachineEventScanWindowHours` | `24` | How far back each scan looks. | Catches older missed events, more work. | Less work, may miss late-arriving data. | `set-machine-events --scan-hours 48` |
| `MachineEventDowntimeMaxAvailabilityRatio` | `0.0` | Availability at/below this means downtime. | More downtime minutes are detected. | Fewer downtime minutes are detected. | `set-machine-events --downtime-max-availability 0.05` |
| `MachineEventSlowdownMaxThroughputRatio` | `0.75` | Throughput at/below this can be slowdown. | Looser; more slowdown minutes. | Stricter; only worse slowdowns. | `set-machine-events --slowdown-max-throughput 0.80` |
| `MachineEventSlowdownMinAvailabilityRatio` | `0.5` | Minimum availability for slowdown. | Stricter; excludes questionable running minutes. | Looser; includes lower-availability slow minutes. | `set-machine-events --slowdown-min-availability 0.60` |
| `MachineEventSlowdownMinTotalFpm` | `100` | Minimum FPM for slowdown. | Stricter; avoids near-stop minutes. | Looser; includes very low-flow minutes. | `set-machine-events --slowdown-min-fpm 250` |
| `MachineEventMinDurationMinutes` | `3` | Minimum event length. | Stricter; fewer short events. | Looser; more short events. | `set-machine-events --min-duration 5` |
| `MachineEventMergeGapMinutes` | `2` | Small gaps allowed inside an event. | Merges more nearby dips into one event. | Splits dips into separate events. | `set-machine-events --merge-gap 1` |
| `MachineEventExcludeLotTransitions` | `true` | Removes saved lot-transition windows from generic downtime/slowdown. | `true` avoids double-counting transitions. | `false` allows overlap for investigations. | `set-machine-events --exclude-lot-transitions true` |

## Reporting Guidance

Use these views for reports and dashboards:

- `oee.v_downtime_event_detail`
- `oee.v_slowdown_event_detail`
- `oee.v_machine_event_detail`
- `oee.v_operational_minute_batch` for validation

Before reporting downtime or slowdown conclusions:

- Check whether `MachineEventExcludeLotTransitions` is enabled.
- Check whether any event has `overlaps_lot_transition = true`.
- Keep downtime, slowdown, and lot-transition reports separate unless a combined operational-impact report is explicitly requested.

Good questions to answer:

- How many minutes was a machine unavailable outside lot transitions?
- Which batches had repeated non-transition slow running?
- How much slow running happened while the machine was available enough to run?
- Are the same serials or batches repeatedly producing downtime events?

Interpretation guardrails:

- Downtime and slowdown events are model output over minute rows.
- Validate unusual events against `oee.v_operational_minute_batch` and, when necessary, raw `public.metrics`.
- `slowdown_events` are low-throughput running windows, not complete stops.
- If exclusion is off, overlapping transition events should be reported with the overlap flag.

## Key Files

- `SizerDataCollector.Core/AnomalyDetection/MachineEventAnalyzer.cs`: classification, merge, summary, and lot-transition exclusion logic.
- `SizerDataCollector.Core/AnomalyDetection/MachineEventConfig.cs`: runtime tuning values.
- `SizerDataCollector.Service/Commands/MachineEventCommands.cs`: scan, list, and export CLI.
- `SizerDataCollector.Service/sql/definitions/schema.sql`: `oee.downtime_events` and `oee.slowdown_events`.
- `SizerDataCollector.Service/sql/definitions/views.sql`: event detail views.
