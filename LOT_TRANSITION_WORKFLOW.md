# Lot Transition Throughput Workflow

This workflow explains how SizerDataCollector detects and reports disruption during a grower lot or batch change.

ELI5 version: fruit normally moves through the machine at a steady speed. During a lot change, the flow often slows down, bottoms out, then recovers. This detector measures that dip as throughput opportunity loss.

## What It Measures

Lot transitions are about throughput, not literal waste and not mostly availability.

- Throughput means fruit moving through the machine, measured as `machine_total_fpm`.
- Availability means whether the machine was able to run. It helps explain an event, but it is not the main loss number.
- The default reported impact is break-adjusted stable throughput opportunity loss: how much fruit movement we missed compared with the stable outgoing rate before the transition, after removing likely smoko/lunch-style stop time from the opportunity window.

The detector stores one event per reportable transition into a new `incoming_batch_record_id`.

Important event measures:

- `disruption_duration_minutes`: from first material slowdown to stable recovery.
- `break_overlap_detected`: `true` when the transition window includes a likely scheduled break or non-transition stop.
- `break_overlap_minutes`: how many minutes of the transition opportunity window looked like break time.
- `break_adjusted_disruption_minutes`: disruption minutes after subtracting likely break overlap.
- `break_adjusted_stable_fruit_opportunity_shortfall`: the default reportable loss estimate for shift summaries.
- `break_adjusted_stable_equivalent_lost_minutes`: default loss converted back into minutes at stable pre-transition speed.
- `stable_fruit_opportunity_shortfall`: unadjusted stable loss estimate, using stable pre-transition FPM.
- `fruit_opportunity_shortfall`: legacy peak-based loss estimate, kept for compatibility.
- `target_fruit_opportunity_shortfall`: optional target-based estimate when `target_throughput` is available.
- `stable_equivalent_lost_minutes`: stable loss converted back into minutes at stable pre-transition speed.
- `availability_avg_during_disruption`: context only. It answers "was the machine available?" not "how much transition throughput did we lose?"

## Baselines In Plain English

Think of a transition like this:

```text
Before change: fruit is moving around 40,000 FPM
During change: fruit dips to 12,000 FPM
After change: fruit recovers near 39,000 FPM
Window length: 5 minutes
Actual movement through the window: 140,000 FPM-minutes
```

The system compares actual movement against three baselines:

| Baseline | Simple meaning | Example | Best use |
| --- | --- | --- | --- |
| Stable | "What if we kept running like we usually were just before the change?" | 40,000 x 5 = 200,000 expected, so loss = 60,000 | Default operator impact |
| Peak | "What if we held the best pre-change minute flat?" | 42,000 x 5 = 210,000 expected, so loss = 70,000 | Worst-case or legacy comparison |
| Target | "What if we ran at configured machine target?" | 38,000 x 5 = 190,000 expected, so loss = 50,000 | OEE/target comparison |

Stable is the baseline because a one-minute peak can exaggerate the loss. The primary reported value is the break-adjusted stable version when break overlap was detected.

## Break Adjustment In Plain English

Sometimes a transition stretches across a real break. You can spot this when fruit flow goes to zero for roughly 15 or 30 minutes. If we count that full zero-flow period as transition loss, the transition looks worse than it really was.

The detector now looks for break-like stop windows inside the transition search area:

- FPM is at or below `LotTransitionMinFpmForBaseline`.
- The low-flow stop lasts at least 10 minutes.
- The low-flow stop lasts no more than 35 minutes.

When a break-like stop overlaps the opportunity window, the normal stable number is still saved, but the primary report number subtracts the break-like minutes first.

Small example:

```text
Stable outgoing speed: 40,000 FPM
Opportunity window: 30 minutes
Break-like zero-flow overlap: 15 minutes
Actual movement outside the break: 500,000 FPM-minutes
```

Without break adjustment:

```text
Expected = 40,000 x 30 = 1,200,000
Loss = 1,200,000 - actual
```

With break adjustment:

```text
Expected = 40,000 x (30 - 15) = 600,000
Loss = 600,000 - actual outside the break
```

Use the break-adjusted stable fields for shift-level "what opportunity can we realistically make up?" reporting. Use the unadjusted stable fields when you deliberately want to include every minute the line was not moving during the transition window.

## Data Sources

- `public.metrics`: raw `machine_total_fpm` values, ordered by timestamp and filtered by `serial_no`.
- `public.batches`: grower code and comments used to make transitions readable.
- `oee.v_operational_minute_batch`: optional target throughput context.
- `oee.v_oee_minute_batch`: optional availability context.

The primary trigger is `machine_total_fpm`. Availability is saved as context only.

## Detection Logic

For each `batch_record_id` change in `machine_total_fpm`:

1. Find stable outgoing FPM from recent positive FPM samples before the transition.
2. Find recovered incoming FPM from positive samples after the transition.
3. Find the first material slowdown using `LotTransitionSlowdownFraction`.
4. Find the trough between slowdown and recovery.
5. Find stable recovery using `LotTransitionRecoveryFraction` and consecutive recovered samples.
6. Find pre/post peaks inside `LotTransitionPeakSearchMinutes`.
7. Integrate actual FPM through the transition opportunity window.
8. Detect break-like zero/near-zero flow windows that last 10-35 minutes.
9. Compare actual FPM-minutes with stable, peak, and optional target baselines.
10. Calculate break-adjusted stable impact by removing break overlap from the opportunity window.
11. Persist the event to `oee.lot_transition_throughput_events`.

The DB insert is idempotent on `(serial_no, incoming_batch_record_id)`, so rescanning the same window is safe. Existing saved events are not automatically recalculated; rescan/backfill if old rows need the new impact columns populated. The reporting view falls back to the old stable calculation when a saved row does not yet have break-adjusted fields.

## CLI Usage

Run from the `SizerDataCollector.Service` output directory.

```text
SizerDataCollector.Service.exe lot-transition scan --serial 140578 --day 2026-04-23
SizerDataCollector.Service.exe lot-transition scan --serial 140578 --hours 72 --no-persist
SizerDataCollector.Service.exe lot-transition list --serial 140578 --month 2026-04
SizerDataCollector.Service.exe lot-transition export --serial 140578 --year 2026
```

Enable the background loop:

```text
SizerDataCollector.Service.exe set-lot-transition --enabled true --interval 30 --scan-hours 72
```

Tune sensitivity:

```text
SizerDataCollector.Service.exe set-lot-transition --slowdown-fraction 0.15 --recovery-fraction 0.10 --stable-window 10 --peak-search 30
```

Restart the Windows service after `set-lot-transition` changes so the background loop reloads runtime settings.

## Config Tuning

| Config | Default | What it means | Move up | Move down | Example |
| --- | ---: | --- | --- | --- | --- |
| `EnableLotTransitionDetection` | `false` | Turns background detection on/off. | Enables detection when `true`. | Disables background detection when `false`. | `set-lot-transition --enabled true` |
| `LotTransitionEvalIntervalMinutes` | `30` | How often the service scans. | Less frequent, lower load, slower detection. | More frequent, faster detection, more DB work. | `set-lot-transition --interval 15` |
| `LotTransitionScanWindowHours` | `72` | How far back each scan looks. | Catches older missed transitions, more work. | Less work, may miss late-arriving data. | `set-lot-transition --scan-hours 24` |
| `LotTransitionStableWindowMinutes` | `10` | How much pre-change time defines normal stable FPM. | Smoother baseline, may ignore quick changes. | More reactive, noisier baseline. | `set-lot-transition --stable-window 15` |
| `LotTransitionPeakSearchMinutes` | `30` | How far around the transition to find opportunity peaks. | Wider opportunity window, can include unrelated dips. | Tighter window, may miss slow recoveries. | `set-lot-transition --peak-search 20` |
| `LotTransitionSlowdownFraction` | `0.15` | Required drop from stable FPM to call slowdown. | Stricter; fewer, larger events. | Looser; more, smaller events. | `set-lot-transition --slowdown-fraction 0.20` |
| `LotTransitionRecoveryFraction` | `0.10` | How close incoming FPM must get to recovered baseline. | Looser recovery if fraction is larger. | Stricter recovery if fraction is smaller. | `set-lot-transition --recovery-fraction 0.08` |
| `LotTransitionConsecutiveSamplesForSlowdown` | `1` | Number of consecutive low samples needed. | Stricter; avoids one-sample noise. | More sensitive to brief dips. | `set-lot-transition --slowdown-samples 2` |
| `LotTransitionRecoveryConsecutiveSamples` | `2` | Number of recovered samples needed. | Stricter; recovery must hold longer. | More sensitive; recovery can end sooner. | `set-lot-transition --recovery-samples 3` |
| `LotTransitionMinPreStableSamples` | `3` | Minimum valid pre-transition stable samples. | Stricter; skips weak context. | Looser; accepts less context. | `set-lot-transition --min-pre-samples 5` |
| `LotTransitionMinPostStableSamples` | `3` | Minimum valid post-transition samples. | Stricter; skips weak recoveries. | Looser; accepts less recovery context. | `set-lot-transition --min-post-samples 5` |
| `LotTransitionMinFpmForBaseline` | `100` | Minimum FPM considered real running flow. Also used as the low-flow threshold for break-like stop detection. | Ignores more low/noisy values and makes break-like stops easier to classify as non-production time. | Includes more low values and makes break-like stops less likely to be classified as non-production time. | `set-lot-transition --min-fpm 250` |

## Reporting Guidance

Use `lot-transition list`, `lot-transition export`, or `oee.v_lot_transition_throughput_event_detail`.

For normal shift reports, use:

- `primary_fruit_opportunity_shortfall`
- `primary_throughput_loss_ratio`
- `primary_equivalent_lost_minutes`
- `primary_baseline_label`

Those `primary_*` columns are the report-ready default. New rows use break-adjusted stable throughput opportunity loss. Older rows fall back to stable throughput opportunity loss until they are rescanned or backfilled.

For transition quality review, also show:

- `break_overlap_detected`
- `break_overlap_minutes`
- `break_adjusted_disruption_minutes`
- `break_adjusted_opportunity_window_minutes`
- `stable_fruit_opportunity_shortfall`
- `fruit_opportunity_shortfall`
- `target_fruit_opportunity_shortfall`

Good questions to answer:

- Which serials lose the most stable equivalent production minutes during lot changes?
- Which grower or batch transitions repeatedly have high stable opportunity loss?
- How do stable, peak, and target loss estimates compare?
- Are high-loss transitions also low-availability transitions, or was the machine available but starved/slow?
- Are changeovers improving after process changes?

Interpretation guardrails:

- The loss fields are throughput opportunity estimates, not literal fruit waste.
- Break-adjusted stable loss is the default operational number for shift reports.
- If `break_overlap_detected = true`, call it out in detailed reports so operators know the transition crossed likely non-production time.
- `stable_fruit_opportunity_shortfall` still includes break-like stop time. That is useful for audit/comparison, but it can overstate make-up opportunity when a transition crosses a real break.
- Peak loss is useful for legacy or worst-case comparison.
- Target loss depends on configured target throughput quality.
- Validate unusual results against `oee.v_operational_minute_batch` and raw `public.metrics`.

## Key Files

- `SizerDataCollector.Core/AnomalyDetection/LotTransitionAnalyzer.cs`: detection and impact math.
- `SizerDataCollector.Core/AnomalyDetection/LotTransitionDatabaseSink.cs`: DB insert.
- `SizerDataCollector.Service/Commands/LotTransitionCommands.cs`: scan, list, and export CLI.
- `SizerDataCollector.Service/sql/definitions/schema.sql`: `oee.lot_transition_throughput_events`.
- `SizerDataCollector.Service/sql/definitions/views.sql`: `oee.v_lot_transition_throughput_event_detail`.
