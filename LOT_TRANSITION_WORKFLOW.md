# Lot Transition Throughput Workflow

This workflow explains how SizerDataCollector detects and records production disruption around grower lot or batch changes.

The goal is to turn the visual “slow down, trough, recover” pattern into repeatable data that can be reported by operators, dashboards, and AI agents.

## What It Measures

The detector records one event for each reportable transition into a new `incoming_batch_record_id`.

It stores two complementary measures:

- `disruption_duration_minutes`: the material slowdown and recovery window. This starts when `machine_total_fpm` first falls materially below the outgoing stable rate and ends when the incoming batch reaches a stable recovered rate.
- `fruit_opportunity_shortfall`: estimated fruit not processed versus holding the outgoing pre-transition peak flat through the peak-to-peak opportunity window. This is throughput opportunity cost, not literal waste.

The event is serial-aware and batch-aware. It stores `serial_no`, outgoing and incoming `batch_record_id`, grower codes, labels, timing milestones, FPM baselines, availability context, and an `explanation` JSON payload with thresholds and sample counts.

## Data Sources

- `public.metrics`: raw `machine_total_fpm` values, ordered by timestamp and filtered by `serial_no`.
- `public.batches`: grower code and comments used to make transitions readable.
- `oee.v_oee_minute_batch`: optional availability enrichment for the disruption and opportunity windows.

The primary trigger is `machine_total_fpm`. Availability is saved as context because it helps explain the event later, but it is not required for detection.

## Detection Logic

For each `batch_record_id` change in `machine_total_fpm`:

1. Compute the outgoing stable FPM from recent positive FPM samples before the transition.
2. Compute the incoming recovered FPM from positive samples after the transition, using the upper stable portion of the early incoming run.
3. Find the first material slowdown using `LotTransitionSlowdownFraction`.
4. Find the trough between slowdown and recovery.
5. Find stable recovery using `LotTransitionRecoveryFraction` and consecutive recovered samples.
6. Find pre- and post-transition peaks inside `LotTransitionPeakSearchMinutes`.
7. Integrate actual FPM through the peak-to-peak window and compare it with a flat pre-peak baseline.
8. Persist the event to `oee.lot_transition_throughput_events`.

The DB insert is idempotent on `(serial_no, incoming_batch_record_id)`, so rescanning the same window is safe.

## CLI Usage

Run from the `SizerDataCollector.Service` output directory.

Scan and persist a day:

```text
SizerDataCollector.Service.exe lot-transition scan --serial 140578 --day 2026-04-23
```

Preview without inserting:

```text
SizerDataCollector.Service.exe lot-transition scan --serial 140578 --hours 72 --no-persist
```

List saved events:

```text
SizerDataCollector.Service.exe lot-transition list --serial 140578 --month 2026-04
```

Export CSV:

```text
SizerDataCollector.Service.exe lot-transition export --serial 140578 --year 2026
```

Enable the background service loop:

```text
SizerDataCollector.Service.exe set-lot-transition --enabled true --interval 30 --scan-hours 72
```

Tune sensitivity:

```text
SizerDataCollector.Service.exe set-lot-transition --slowdown-fraction 0.15 --recovery-fraction 0.10 --stable-window 10 --peak-search 30
```

## Reporting Guidance

Use `lot-transition list` or `oee.v_lot_transition_throughput_event_detail` when building reports.

Good questions to answer:

- Which serials lose the most peak-production minutes during lot changes?
- Which grower or batch transitions repeatedly have long disruption windows?
- How much estimated fruit opportunity is lost by day, month, or year?
- Do high-loss transitions coincide with low availability?
- Are changeovers improving after process changes?

Interpretation guardrails:

- `fruit_opportunity_shortfall` is an opportunity-cost estimate based on the outgoing peak FPM. It should not be described as physical fruit loss.
- Very short windows, flatline zero runs, and transitions without enough stable context are skipped rather than saved as weak events.
- Date inputs are interpreted consistently with the existing CLI date handling. Prefer explicit UTC timestamps for exact investigations.
- Validate unusual results against raw `public.metrics` before using them in operational decisions.

## Key Files

- `SizerDataCollector.Core/AnomalyDetection/LotTransitionAnalyzer.cs`: core detection and integration logic.
- `SizerDataCollector.Core/AnomalyDetection/LotTransitionDatabaseSink.cs`: DB insert with idempotency.
- `SizerDataCollector.Service/Commands/LotTransitionCommands.cs`: scan, list, and export CLI commands.
- `SizerDataCollector.Service/SizerCollectorService.cs`: optional background scan loop.
- `SizerDataCollector.Service/sql/definitions/schema.sql`: `oee.lot_transition_throughput_events`.
- `SizerDataCollector.Service/sql/definitions/views.sql`: `oee.v_lot_transition_throughput_event_detail`.
