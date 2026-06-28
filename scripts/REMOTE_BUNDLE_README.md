# CollectorAgent Remote Install

1. Copy this bundle folder to the target PC.
2. Open PowerShell as Administrator in the bundle folder.
3. Run:

```powershell
.\install-from-bundle.ps1
```

Optional (initialize/update DB schema after install):

```powershell
.\install-from-bundle.ps1 -RunDbInit
```

After upgrading, apply new SQL definitions (product setup views):

```powershell
& "C:\Program Files (x86)\OPTI-FRESH\CollectorAgent\service\SizerDataCollector.Service.exe" db apply-functions
& "C:\Program Files (x86)\OPTI-FRESH\CollectorAgent\service\SizerDataCollector.Service.exe" db apply-views
```

Restart the Windows service after replacing binaries.

## What changed in v16 (vs v15)

- **Product setup tracking** — The collector now captures active variety, layout, and outlet-to-product assignments when the setup changes (first run, variety/layout change from batch context, or unknown product ID at an outlet). Snapshots are stored as `metric = 'product_setup'` in `public.metrics` (not polled on a timer; no config entry needed).
- **Per-outlet throughput** — `outlets_fpm` is included in default `EnabledMetrics` (lightweight `GetOutletsFPM()` array). Existing installs with a custom `EnabledMetrics` list should add `"outlets_fpm"` alongside `"outlets_details"`.
- **Batch context** — Current batch now carries `VarietyId`, `VarietyName`, `LayoutId`, and `LayoutName` for change detection.
- **Reporting** — New `public.latest_product_setup(serial, ts)`, `public.v_product_setup_history`, and `public.v_outlet_product_detail` (product name, elements, `matches_plan`, `fpm_exceeds_max`). See `MD-DOCS/PRODUCT_SETUP_WORKFLOW.md`.

## What changed in v13 (vs v12)

- **Lot transition throughput** — Events now carry **stable**, **peak**, and optional **target** baselines (FPM-minutes shortfall, loss ratio, equivalent lost minutes). The default operator-facing impact is the **stable pre-transition** baseline; legacy peak-based numbers remain for comparison. New columns are added on `oee.lot_transition_throughput_events` (existing installs: run **`db apply-schema`** / **`db init`** as you usually would). Rescans are idempotent on `(serial_no, incoming_batch_record_id)`.
- **Reporting view** — `oee.v_lot_transition_throughput_event_detail` exposes `primary_fruit_opportunity_shortfall`, `primary_throughput_loss_ratio`, `primary_equivalent_lost_minutes`, and `primary_baseline_label` (stable default), plus the explicit stable / peak / target columns. **`lot-transition list`** / **`export`** read from this view.
- **Machine downtime and slowdowns** — `oee.downtime_events` and `oee.slowdown_events` now include **`overlaps_lot_transition`**, and **`oee.v_downtime_event_detail`**, **`oee.v_slowdown_event_detail`**, and **`oee.v_machine_event_detail`** surface it. Use this when reconciling transition throughput opportunity loss with general machine-event reporting (see `MachineEventExcludeLotTransitions` in `MACHINE_EVENT_WORKFLOW.md`).

## What changed in v9 (vs v8)

- **Fresh Timescale installs: `continuous_aggregates.sql` throughput CAGGs** use immutable `oee.calc_perf_ratio(...)` (four-argument overload) inside the aggregate definition. Serial-aware throughput still comes from **`oee.v_throughput_minute_batch`** / **`oee.v_throughput_daily_batch`**, which call `oee.calc_perf_ratio(serial_no, ...)`. This avoids Timescale error `only immutable functions supported in continuous aggregate view` on brand-new databases.
- **Fresh DB: `views.sql` apply order** — `oee.v_throughput_*` views are created **before** `oee.cagg_oee_minute_batch` so **`db apply-views`** / **`db init`** does not fail with `relation "oee.v_throughput_minute_batch" does not exist` on empty installs.

## What changed in v7 (vs v6)

- **Detector is now stable across grade-set fluctuation.** The rolling window no longer resets when a grade appears or disappears between minutes, and the lane count is monotonically non-decreasing. Previously this caused the window to shrink to 1-2 samples and suppress alarms on live data. Internal model bumped to `composition-mad-v3`.
- **Alarm messages are human-readable.** No more "composition skew", "peer median", or "score=..." in the alarm text. Titles now read like `"Lane 32: producing mostly PEDDLER (63% vs 18% typical)"` or `"Lane 32: heavy on PEDDLER, light on GREEN"`. Technical numbers (robust score, deltas, medians) are still preserved in the event's `explanation_json` for reporting.
- Unchanged: config surface, DB schema, CLI commands, alarm sinks.

## Diagnostic replay (v4+)

Run the replay with `--diag` to dump what the detector actually sees on the window. This tells us whether lanes are excluded by guardrails, whether peer counts are sufficient, and whether per-grade deltas are firing the gates.

```powershell
& "C:\Program Files (x86)\OPTI-FRESH\CollectorAgent\service\SizerDataCollector.Service.exe" `
    replay-anomaly `
    --serial 140578 `
    --from "2026-04-23T15:09:00Z" `
    --to   "2026-04-23T17:39:00Z" `
    --diag `
    --diag-lane 32
```

Paste the full console output back. The section labelled `========== DIAGNOSTIC DUMP ==========` is the important part.

Key things the diag output will reveal:

- **Detector config** (active thresholds in force on this machine).
- **Snapshots processed / Detector resets (batch)**: if the batch-id changes many times inside the window, the detector keeps resetting and never accumulates enough history.
- **Lane summary (top 10 by composition skew)**: whether lane 32 is listed, its `AvgFpm`, eligible `Peers`, `SkewL1` (sum |delta|/2), and guard-pass status (`ok` / `LOW_FPM` / `LOW_PEERS`).
- **Focus lane grade breakdown**: per-grade `LanePct`, `PeerMed`, `DeltaPts`, `Score`, and gate outcomes (`base`, `z`, `extreme` => `TRIGGER` / `.`). This is where you'll see why nothing tripped.

After install, you can also still verify the upgraded anomaly CLI options:

```powershell
& "C:\Program Files (x86)\OPTI-FRESH\CollectorAgent\service\SizerDataCollector.Service.exe" set-anomaly
```

You should see:
- `--min-lane-fpm`
- `--min-peer-lane-fpm`
- `--min-peer-lanes`
- `--consecutive-windows`
