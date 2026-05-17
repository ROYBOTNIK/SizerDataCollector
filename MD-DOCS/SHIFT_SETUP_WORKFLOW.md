# Shift Setup and Shift Reporting Window Workflow

This workflow explains how to define shift boundaries per machine serial and how to verify the shift windows and rollups used by shift-level OEE reporting.

Use this workflow when an operator asks to:

- add or change day/night shifts
- align shift boundaries to a local timezone
- validate which UTC windows a shift maps to
- inspect shift-level OEE summaries

## What It Measures

Shift definitions are stored in `oee.shifts` and expanded into concrete UTC windows through `oee.v_shift_window`.

Shift reporting rollups are available in:

- `oee.v_availability_shift_batch`
- `oee.v_throughput_shift_batch`
- `oee.v_quality_shift_batch`
- `oee.v_oee_shift_batch`
- `oee.v_oee_shift`

## Data Sources

- `oee.shifts`: per-serial shift definitions (`shift_name`, local start/end clock, timezone, day-of-week mask, effective dates, active flag).
- `oee.v_oee_minute_batch`: minute-level OEE source used to derive active local calendar days.
- `oee.v_operational_minute_batch`: minute-level operational source used for shift rollups.

## CLI Usage

Run from the `SizerDataCollector.Service` output directory.

List configured shifts:

```text
SizerDataCollector.Service.exe shift list --serial 140578
```

Add a shift:

```text
SizerDataCollector.Service.exe shift add --serial 140578 --name Day --start 06:00 --end 18:00 --tz Pacific/Auckland --dow Mon-Fri
```

Update an existing shift:

```text
SizerDataCollector.Service.exe shift update --serial 140578 --name Day --start 07:00 --end 19:00 --active true
```

Remove a shift:

```text
SizerDataCollector.Service.exe shift remove --serial 140578 --name Day
```

Show expanded UTC windows for a local day:

```text
SizerDataCollector.Service.exe shift show --serial 140578 --day 2026-05-08
```

## Reporting Guidance

Use `shift show` first when validating report boundaries, especially around DST transitions.

When reporting shift-level OEE:

- pull shift windows from `oee.v_shift_window`
- aggregate from shift views (`oee.v_*_shift_batch`, `oee.v_oee_shift`) instead of rebuilding ad hoc windows
- keep one serial per report artifact unless a combined view is explicitly requested

## Interpretation guardrails

- Shift windows are local-clock definitions converted to UTC using the configured `timezone`.
- A shift where `end_local <= start_local` is treated as crossing midnight.
- `dow_mask` gates which local weekdays a shift is active.
- Effective dates are applied on the local day (`day_local`), not UTC day.
- If a serial has no rows in `oee.shifts`, shift rollups will not produce rows for that serial.

## Key Files

- `SizerDataCollector.Service/Commands/ShiftCommands.cs`: CLI for shift list/add/update/remove/show.
- `SizerDataCollector.Service/sql/definitions/schema.sql`: `oee.shifts` table.
- `SizerDataCollector.Service/sql/definitions/views.sql`: `oee.v_shift_window` and shift rollup views.
- `SizerDataCollector.Service/Program.cs`: top-level `shift` command routing.
