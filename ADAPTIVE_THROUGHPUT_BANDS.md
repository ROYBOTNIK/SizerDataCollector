# Adaptive Throughput Bands

This workflow explains how throughput banding works on top of the existing adaptive performance score.

## What Is Classified

Throughput bands classify `throughput_ratio`, not raw FPM.

The ratio is already serial-aware through:

```sql
oee.calc_perf_ratio(serial_no, total_fpm, missed_fpm, recycle_fpm, target_fpm)
```

That function uses `oee.perf_params` when a machine-specific row exists, and falls back to default performance curve settings otherwise. Bands are a presentation layer over that score.

## Band Storage

Bands live in `oee.band_definitions` and are separated by `metric_type`.

Use:

- `metric_type = 'oee'` for OEE score bands.
- `metric_type = 'throughput'` for throughput ratio bands.

Existing OEE band commands still default to `metric_type = 'oee'`.

Throughput tuning metadata is stored with the active band rows:

- `source`
- `confidence`
- `observed_minutes`
- `tuned_from_ts`
- `tuned_to_ts`
- `notes`

## Default Throughput Bands

If a serial has no active configured throughput bands, `oee.classify_band_value(serial_no, 'throughput', throughput_ratio)` falls back to:

| Band | Range |
| --- | --- |
| `very_low` | `[0.00, 0.50)` |
| `low` | `[0.50, 0.70)` |
| `close` | `[0.70, 0.85)` |
| `on_target` | `[0.85, 0.95)` |
| `surpassing_target` | `[0.95, 1.00]` |

## CLI Commands

Show throughput bands:

```powershell
SizerDataCollector.Service.exe machine show-bands --serial <sn> --metric throughput
```

Manually set a throughput band:

```powershell
SizerDataCollector.Service.exe machine set-band --serial <sn> --metric throughput --band on_target --lower 0.85 --upper 0.95
```

Deactivate a throughput band:

```powershell
SizerDataCollector.Service.exe machine remove-band --serial <sn> --metric throughput --band on_target
```

Dry-run conservative tuning from recent history:

```powershell
SizerDataCollector.Service.exe machine tune-bands --serial <sn> --metric throughput
```

Apply tuned bands:

```powershell
SizerDataCollector.Service.exe machine tune-bands --serial <sn> --metric throughput --apply
```

The default tuning window is the last 7 days. Override it with `--history-days`, or pass explicit `--from` and `--to` timestamps. The command requires at least 240 valid running minutes unless `--min-minutes` is provided.

## Tuning Method

The CLI reads valid running minutes from `oee.v_operational_minute_batch`:

- `target_throughput > 0`
- `total_fpm > 0`
- `throughput_ratio IS NOT NULL`

It computes candidate boundaries from conservative quantiles:

- q20 for `very_low / low`
- q45 for `low / close`
- q70 for `close / on_target`
- q90 for `on_target / surpassing_target`

Then it clamps the boundaries to keep labels meaningful:

- b1: `0.45..0.60`
- b2: `0.65..0.80`
- b3: `0.80..0.92`
- b4: `0.90..0.98`

Adjacent boundaries must remain at least `0.05` apart.

## Reporting Surface

External reporting should prefer:

```sql
SELECT *
FROM oee.v_throughput_minute_classified
WHERE serial_no = '<sn>'
  AND minute_ts >= '<from>'
  AND minute_ts < '<to>';
```

The view exposes:

- operational state: `no_target`, `stopped`, or `running`
- throughput target band
- primary reason
- effective FPM
- target gap
- gross-flow, missed-fruit, and recycle loss attribution

## Future Service Tuning

Automatic service-side tuning is intentionally not enabled yet. The first production workflow is agent/operator-triggered CLI tuning so suggested thresholds can be reviewed, compared, and rolled back before a background job is allowed to mutate active band definitions.
