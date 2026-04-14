# Anomaly Reporting Workflow

This document explains how to use the anomaly reporting primitives that now exist inside `SizerDataCollector.Service`.

It is intended for operators, developers, and future AI agents that need to inspect recurring anomaly patterns, correlate anomalies with OEE impact, or compare detector behavior across two historical windows.

## Goal

Use persisted anomaly events as reporting inputs, not just alarm outputs.

The reporting flow sits downstream of:

- live anomaly detection
- `replay-anomaly`
- `size-health`
- alarm delivery testing

This workflow does not replace anomaly tuning. It complements it by turning anomaly history into repeatable evidence.

## What These Reports Are Good At

The current reporting layer is already useful for:

- hotspot detection
- triage support
- tuning evidence
- impact screening

It is not yet root-cause reporting.

Use the current reports to prioritize what deserves attention. Do not use them to claim that a specific anomaly definitively caused an operational outcome unless that conclusion is supported elsewhere.

## Available command surfaces

Run these from the `SizerDataCollector.Service` output directory.

### Recurring offenders

```text
SizerDataCollector.Service.exe anomaly offenders --serial <sn> --type grade|size|both [--hours <h>]
SizerDataCollector.Service.exe anomaly offenders --serial <sn> --type grade|size|both --from "2025-12-01 00:00:00" --to "2026-01-01 00:00:00" [--limit 10]
```

Use this when you want to answer:

- Which lanes keep reappearing?
- Which grade keys are repeat offenders?
- Are the repeats mostly low noise or high-severity events?

Interpretation guardrail:

- `repeat_count` is a recurrence and persistence signal, not a clean count of distinct failures
- the grade detector applies cooldowns, so repeated alarms can reflect the same underlying issue continuing to re-trigger over time

### Anomaly-to-impact correlation

```text
SizerDataCollector.Service.exe anomaly impact --serial <sn> --type grade|size|both [--hours <h>]
SizerDataCollector.Service.exe anomaly impact --serial <sn> --type grade|size|both --from "2025-12-01 00:00:00" --to "2026-01-01 00:00:00" [--limit 10]
```

Use this when you want to answer:

- Did the anomaly coincide with throughput loss?
- Was there a quality or OEE drop around the event?
- Did the metric recover after the event window?

Interpretation guardrail:

- this report shows temporal association with operational metrics
- it does not, by itself, prove causation

### Aggregate impact rollups (phase 2)

```text
SizerDataCollector.Service.exe anomaly impact-summary --serial <sn> --type grade|size|both [--hours <h>]
SizerDataCollector.Service.exe anomaly impact-summary --serial <sn> --type grade|size|both --from "2025-12-01 00:00:00" --to "2026-01-01 00:00:00" [--limit 10]
```

Use this when you want to answer:

- Which anomaly families show the worst average post-event OEE drift?
- Which families show the worst average post-event throughput drift?
- Which high-severity families repeatedly have negative post impact?

### Replay-based tuning comparison

```text
SizerDataCollector.Service.exe anomaly tuning-compare --serial <sn> --type grade|size|both --baseline-from "2025-12-01 00:00:00" --baseline-to "2025-12-16 00:00:00" --candidate-from "2025-12-16 00:00:00" --candidate-to "2026-01-01 00:00:00" [--limit 5]
```

Use this when you want to answer:

- Did the later window generate fewer events?
- Did high-severity events go down?
- Did the top offenders change after a threshold or machine adjustment?

## Required inputs

Every reporting run should define:

- `serial_no`
- anomaly type: `grade`, `size`, or `both`
- a time window
- the question being answered

For tuning comparison, define:

- one baseline window
- one candidate window
- the same serial and anomaly type for both

## Prerequisites

The reporting commands depend on persisted anomaly events.

Primary event sources:

- `oee.grade_lane_anomalies`
- `oee.lane_size_anomalies`

If the report returns no rows, first confirm whether anomaly events were ever persisted for that serial and window. If not, seed the history with replay where appropriate:

```text
SizerDataCollector.Service.exe replay-anomaly --serial <sn> --from "2025-12-01 00:00:00" --to "2025-12-02 00:00:00" --persist
```

For size anomaly reporting, ensure the evaluator has run live against the target period or that equivalent persisted size events exist.

## Agent run sequence

Future AI agents should treat this as a direct CLI runbook.

### 1. Establish scope

Collect these inputs before running any command:

- `--serial`
- `--type grade|size|both`
- either `--hours <h>` or an explicit `--from` / `--to` range
- `--limit <n>` if the user wants only the top rows

### 2. Run the three reporting commands in order

Start with recurring offenders:

```text
SizerDataCollector.Service.exe anomaly offenders --serial <sn> --type both --from "2025-12-29 00:00:00" --to "2025-12-31 00:00:00" --limit 10
```

Then run the impact correlation:

```text
SizerDataCollector.Service.exe anomaly impact --serial <sn> --type both --from "2025-12-29 00:00:00" --to "2025-12-31 00:00:00" --limit 10
```

If the task is a before/after review, finish with tuning comparison:

```text
SizerDataCollector.Service.exe anomaly tuning-compare --serial <sn> --type both --baseline-from "2025-12-29 00:00:00" --baseline-to "2025-12-30 00:00:00" --candidate-from "2025-12-30 00:00:00" --candidate-to "2025-12-31 00:00:00" --limit 5
```

### 3. Interpret command success

Expected command behavior:

- `anomaly offenders` prints a ranked offender table.
- `anomaly impact` prints event-centered before/during/after OEE, throughput, and quality context.
- `anomaly impact-summary` prints family-level aggregate rankings and materiality labels.
- `anomaly tuning-compare` prints baseline and candidate summaries plus top offenders for each window.

If a command exits successfully but prints no rows, treat that as a data interpretation task, not a CLI failure.

## Decision Rubric

Use the impact output to classify events into one of three decision buckets:

### Likely material

Use this label when most of the following are true:

- event OEE is meaningfully below pre-event OEE
- post-event OEE remains below pre-event OEE
- throughput drops during the event or remains depressed after it
- the anomaly is medium/high severity or repeats frequently

Typical next action:

- escalate for tuning review, mechanical inspection, or operator/process review

### Mixed / unclear

Use this label when the anomaly looks statistically strong but the operational context is inconsistent.

Examples:

- z-score and percent deviation are large but OEE improves
- throughput drops briefly but recovers immediately
- one metric worsens while another improves

Typical next action:

- gather more history
- compare with `anomaly tuning-compare`
- avoid escalation until the pattern repeats

### Likely non-material

Use this label when the anomaly is real but the operational effect is small or absent.

Typical signs:

- event and post-event values stay close to pre-event values
- post-event OEE recovers immediately or remains stable
- the anomaly is low severity and isolated

Typical next action:

- treat as a tuning/noise candidate before escalating

## SQL objects behind the reports

The reporting layer is defined in the canonical SQL files for the service.

### Tables and indexes

- `oee.grade_lane_anomalies`
- `oee.lane_size_anomalies`

Indexes for reporting filters are defined in `SizerDataCollector.Service/sql/definitions/schema.sql`.

### Reporting views

Defined in `SizerDataCollector.Service/sql/definitions/views.sql`:

- `oee.v_grade_anomaly_event_detail`
- `oee.v_size_anomaly_event_detail`
- `oee.v_anomaly_event_detail`
- `oee.v_anomaly_offender_scorecard_daily`
- `oee.v_operational_minute_batch`
- `oee.v_grade_anomaly_impact_summary`
- `oee.v_size_anomaly_impact_summary`
- `oee.v_anomaly_impact_summary`
- `oee.v_anomaly_offender_cluster_daily`
- `oee.v_anomaly_impact_family_summary_daily`

## How to interpret outputs

### Offenders

Look at:

- repeat count
- severity mix
- max percent deviation
- max z-score
- first seen timestamp
- last seen timestamp
- batch context when shown

Recurring high-count and high-severity rows are the best candidates for:

- threshold tuning
- grade-map review
- mechanical inspection
- process/operator review

Read `repeat_count` carefully:

- high repeats can mean the same issue persisted and kept re-firing across cooldown windows
- use `first seen` and `last seen` to judge span and persistence
- if two rows look similar, compare batch and time span before assuming the CLI duplicated a row
- use cluster fields (`dir`, `activeMin`, `spanMin`, `batches`, `lots`, `runtime`) to distinguish persistent drift from isolated spikes

### Example offender output

```text
SizerDataCollector.Service.exe anomaly offenders --serial 140578 --type grade --from "2025-12-29 00:00:00" --to "2025-12-31 00:00:00" --limit 5

Anomaly offenders for '140578' from 2025-12-29 00:00:00Z to 2025-12-31 00:00:00Z

Type  Lane Batch Scope                    Repeats High Med Low MaxPct  MaxZ   First Seen          Last Seen
----  ---- ----- ------------------------ ------- ---- --- --- ------- ------ ------------------- -------------------
grad     8  9211 8 RCY                         57   18  21  18    34.7    4.9 2025-12-29 03:11Z  2025-12-30 22:47Z
grad     1  9211 1 RCY                         42   11  19  12    28.1    4.1 2025-12-29 05:09Z  2025-12-30 21:55Z
grad     2  9212 2 GATE                        31    8  14   9    22.6    3.8 2025-12-30 01:14Z  2025-12-30 19:06Z
```

How to read this:

- lane 8 `RCY` is a strong hotspot because it repeats across a wide span and includes many high-severity events
- `57` does not mean `57` distinct machine failures; it means the same lane/grade area kept reappearing in the reporting window
- batch context helps distinguish rows that might otherwise print similarly

### Duplicate-looking offender rows

If two offender rows appear to show the same grade or lane:

- first compare the `Batch` value
- then compare `First Seen` and `Last Seen`
- then confirm whether the rows belong to different anomaly types or windows

If they are still visually ambiguous:

- treat that as a presentation issue first, not an operational conclusion
- verify the underlying grouping dimensions in `oee.v_anomaly_offender_scorecard_daily`
- prefer the richer time window and batch context over the shortened scope label

If `anomaly offenders` returns no rows:

- persisted anomaly events may not exist for that serial and window
- the detector may not have been replayed or run live for that period
- the next CLI action is usually `replay-anomaly --persist` for grade events or a different historical window with known persisted data

### Impact

Focus on:

- pre-event, event, and post-event throughput ratio
- pre-event, event, and post-event quality ratio
- pre-event, event, and post-event OEE score
- delta from pre-event baseline

This is an impact-context report, not a causation proof report.

If the anomaly has little or no operational effect, treat it as a tuning/noise candidate before escalating.

If `anomaly impact` returns rows with small deltas:

- the event may be operationally non-material
- the next CLI action is often `anomaly tuning-compare` to decide whether detector thresholds should be adjusted

If `anomaly impact` returns no rows:

- first verify that persisted anomaly events exist for the target window
- then verify the window overlaps real operational data for `oee.oee_minute_batch` and related minute views

### Impact summary

`anomaly impact-summary` emits three aggregate sections:

- top families by average post-event OEE drop
- top families by average post-event throughput drop
- most frequent high-severity families with negative post impact

Each family also carries a materiality label:

- `likely_material`
- `mixed_unclear`
- `likely_non_material`

### Example impact classifications

#### Example 1: likely material

```text
2025-12-30 10:18:00Z [grade/High] lane 8 PINK pct=+24.8% z=+4.1
  OEE 0.742 -> 0.611 -> 0.598  delta(pre)=-0.131 delta(post)=-0.144
  Throughput 0.881 -> 0.701 -> 0.689  Quality 0.954 -> 0.947 -> 0.946
  FPM=196.2 recycle=21.7 cupfill=83.0 tph=14.1 batch=9211 lot=G123 variety=Pink Lady
```

Interpretation:

- likely material
- the anomaly coincides with a meaningful event-time drop and the post window remains depressed
- this is a good candidate for escalation or tuning review

#### Example 2: mixed / unclear

```text
2025-12-30 14:06:00Z [grade/Medium] lane 3 D/S pct=-19.2% z=-3.6
  OEE 0.661 -> 0.676 -> 0.689  delta(pre)=+0.015 delta(post)=+0.028
  Throughput 0.744 -> 0.757 -> 0.762  Quality 0.931 -> 0.928 -> 0.935
  FPM=209.4 recycle=17.1 cupfill=84.6 tph=15.2 batch=9211 lot=G123 variety=Pink Lady
```

Interpretation:

- mixed / unclear
- the anomaly is statistically strong, but machine-level performance is stable or improving
- do not overclaim; gather more examples before escalating

#### Example 3: likely non-material

```text
2025-12-30 18:42:00Z [size/Low] lane 5 24h size pct=+3.4% z=+2.2
  OEE 0.804 -> 0.799 -> 0.806  delta(pre)=-0.005 delta(post)=+0.002
  Throughput 0.904 -> 0.898 -> 0.905  Quality 0.962 -> 0.961 -> 0.963
  FPM=228.0 recycle=12.4 cupfill=86.1 tph=16.3 batch=9212 lot=G129 variety=Royal Gala
```

Interpretation:

- likely non-material
- this is more useful as a tuning/noise signal than an escalation trigger

### Tuning comparison

Compare:

- total event count
- high-severity count
- top offender shifts
- max percent deviation and z-score

Use this to decide:

- keep the new settings
- revert
- gather more history

If `anomaly tuning-compare` returns zero events in both windows:

- do not infer that the detector is healthy
- infer only that no persisted anomaly rows were found in the compared windows
- the next CLI action is to choose a window with known anomaly history or seed grade history with `replay-anomaly --persist`

## Recommended agent workflow

1. Confirm the serial, anomaly type, and time window.
2. Run `anomaly offenders` to locate recurring lanes, grades, or windows.
3. Run `anomaly impact` to determine whether the top events had operational effect.
4. Run `anomaly impact-summary` to prioritize families by aggregate post impact.
5. If the task is comparative, run `anomaly tuning-compare`.
6. If all reports are empty, verify event persistence before assuming the detector is healthy.
7. If grade history is missing but raw `lanes_grade_fpm` data exists, use `replay-anomaly --persist` to seed reportable events.

## Documentation Validation Checklist

Use this checklist whenever this workflow is updated:

- confirm all command examples match `SizerDataCollector.Service.exe anomaly` usage
- confirm `README.md`, `AI_AGENT_GUIDE.md`, and this workflow use the same language for recurrence vs distinct failures
- confirm all three docs say impact output is association/context, not causation proof
- confirm empty-report guidance still routes operators to persisted-event checks and replay seeding
- confirm any example output matches the current CLI column order and labels
- confirm links to `README.md` and `AI_AGENT_GUIDE.md` still describe this workflow accurately

## Deployment / refresh rule

If reporting SQL changes are made in this repo, apply the canonical definitions again:

```text
SizerDataCollector.Service.exe db init
```

For narrower changes:

```text
SizerDataCollector.Service.exe db apply-views
SizerDataCollector.Service.exe db status
```

## Important notes for future agents

- Keep reporting SQL in the canonical definition files, not in ad hoc scripts.
- Keep grade and size anomaly paths distinct until the reporting layer combines them.
- Do not treat an empty report as proof that no anomaly occurred; it may mean events were not persisted for that period.
- Prefer historical windows with known persisted events when validating new reporting behavior.
- Keep execution CLI-focused. Do not introduce shell wrapper requirements when the same task can be performed directly through `SizerDataCollector.Service.exe`.

## Next Enhancements

Phase 2 now includes:

- offender cluster metrics and directionality context in the CLI output
- aggregate family-level impact rollups via `anomaly impact-summary`
- materiality labels for family-level impact triage

Further candidates:

- active-span stitching that merges nearby windows into a single incident episode
- richer direction semantics tied to grade families instead of signed drift only
- longitudinal trend views that track materiality changes by week or month
