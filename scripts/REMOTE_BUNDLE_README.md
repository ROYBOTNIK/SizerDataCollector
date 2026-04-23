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
