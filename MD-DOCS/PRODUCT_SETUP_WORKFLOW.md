# Product Setup Tracking Workflow

This workflow explains how SizerDataCollector captures the active product setup on a machine and how to use it to answer "what was running at outlet N at time T, was it the right product, and was it within rate?"

ELI5 version: every outlet on the sizer is supposed to be receiving a particular product (a class/grade/size combination, in a particular pack). The collector now records what the active variety and layout are, which product each outlet is assigned to, and what that product is made of, so later analysis can tell whether the machine's product makeup matched the plan and whether any outlet was being overdriven.

## What It Captures

Three layers of information, all from the Compac Sizer WCF service:

| Layer | API source | Metric / table | Update cadence |
| --- | --- | --- | --- |
| Outlet runtime state | `GetOutlets()` | `metric = 'outlets_details'` in `public.metrics` | Every poll cycle |
| Per-outlet FPM | `GetOutletsFPM()` | `metric = 'outlets_fpm'` in `public.metrics` | Every poll cycle |
| Variety + layout + product definitions | `GetActiveVariety()` then `GetActiveLayout(varietyId)` | `metric = 'product_setup'` in `public.metrics` | Change-driven (see below) |

Outlets and outlet FPM are lightweight calls and run on the normal poll loop. The variety and layout calls return large payloads, so they follow Compac's vendor guidance and are cached. They are refreshed only when there is a reason to believe the setup has changed.

## Refresh Triggers (`product_setup`)

`ProductSetupTracker` decides whether to refetch the variety and layout each poll cycle. It refreshes when any of these are true:

| Trigger | Meaning |
| --- | --- |
| `first_run` | Tracker has no cached setup yet (service just started). |
| `variety_change` | Current batch reports a different `VarietyId` than the cached one. |
| `layout_change` | Current batch reports a different `LayoutId` than the cached one. |
| `unknown_product_id` | An outlet's `CurrentProductId` or `PendingProductId` is not in the cached product list. |

When a trigger fires, the tracker calls `GetActiveVariety()` then `GetActiveLayout(varietyId)`, builds a snapshot envelope, computes a SHA-256 of the envelope, and only stores a new `product_setup` metric row if that hash differs from the previous one. Repeated triggers with identical content (for example, identical layout but a benign operator action) do not produce duplicate rows.

If `GetActiveVariety` or `GetActiveLayout` throws, the cycle logs a warning and the rest of metric collection continues normally; no row is emitted that cycle.

## `product_setup` Metric Shape

The `value_json` payload on each `product_setup` metric row is a single envelope with the following fields:

```json
{
  "variety_id": "...",
  "variety_name": "Gala",
  "layout_id": "...",
  "layout_name": "Default",
  "batch_id": 1234,
  "batch_record_id": 5678,
  "captured_at": "2026-05-17T...",
  "trigger": "first_run | variety_change | layout_change | unknown_product_id",
  "assignments": {
    "1": {
      "outlet_id": 1,
      "outlet_name": "Lane 1 Class 1",
      "status": "Running",
      "delivered_fpm": 720.0,
      "max_rate_sqcm_per_min": 1000,
      "current_product_id": "...",
      "pending_product_id": null,
      "planned_product_id": "...",
      "matches_plan": true,
      "product_name": "Class 1 70ct",
      "product_display_name": "...",
      "elements": [{ "Grade": "A", "Size": "70", "Quality": "Premium", "Label": "..." }],
      "pack_name": "..."
    }
  },
  "products": [
    {
      "id": "...",
      "name": "Class 1 70ct",
      "display_name": "...",
      "elements": [...],
      "pack": {...},
      "target_fill": {...}
    }
  ],
  "raw_variety": {...},
  "raw_layout": {...}
}
```

`assignments` is keyed by outlet ID as a string. Each entry blends live outlet state (from the current poll cycle's `outlets_details`) with the planned layout product (from `GetActiveLayout`) so a single object answers "what is at outlet 3 right now and is it the right thing?"

`raw_variety` and `raw_layout` keep the full untransformed responses for any downstream code that needs fields the envelope did not promote.

## What "assignments" means (do not confuse the two senses)

The word **assignments** appears in two related places. They are not the same thing.

| Sense | Where it comes from | What it is |
| --- | --- | --- |
| **Layout assignments (planned)** | Compac `GetActiveLayout()` → `Layout.Assignments` | The **plan**: outlet 3 *should* run product X. This is part of the layout definition. |
| **`assignments` in `product_setup` JSON** | Built by `ProductSetupTracker` at refresh time | A **snapshot** per outlet that merges plan + live state at that moment. |

Fields inside each `product_setup.assignments["<outlet_id>"]` entry:

| Field | Source | Meaning |
| --- | --- | --- |
| `planned_product_id` | Layout | What the layout says should be on this outlet. |
| `current_product_id`, `pending_product_id`, `status`, `delivered_fpm` | `outlets_details` at refresh time | What was live on the machine when the snapshot was taken. |
| `product_name`, `elements`, `pack_name` | Product definitions from the layout | Grade/size/quality/pack for the product looked up at refresh time. |
| `matches_plan` | Computed | `current_product_id` equals `planned_product_id` at snapshot time. |

**Important:** `assignments` in stored `product_setup` rows is a point-in-time picture. It does not update on every poll; only when a refresh trigger fires and a new row is stored.

## Product makeup (grade, size, pack, elements)

**Product makeup** means the *definition* of a product, not which outlet it sits on:

- `elements[]`: `Grade`, `Size`, `Quality`, `Label`
- `pack`, `target_fill`, `display_name`, `special_instructions`, and related fields on the `Product` object

That detail comes from **`GetActiveLayout()`** (the `Products` list and layout assignments), **not** from `GetOutlets()`. Outlets only report **which product GUID** is active (`CurrentProductId`, `PendingProductId`); they do not include grade/size breakdown.

| Question | Primary source |
| --- | --- |
| "Which product GUID is on outlet 3 **right now**?" | `outlets_details` (every poll) |
| "What grade/size/pack does that product **mean**?" | `product_setup` (or `v_outlet_product_detail` joined to latest setup) |

## What counts as a "change"? (reposition vs makeup vs plan)

Not every operator action causes a new `product_setup` row. The tracker follows Compac guidance: call `GetOutlets()` frequently for live positions; call `GetActiveLayout()` only when you have reason to believe setup changed.

### Moving or swapping products between outlets (reposition only)

Example: move "Class 1 70ct" from outlet 1 to outlet 3. Same products, same layout, same variety.

| Layer | Picked up? | Notes |
| --- | --- | --- |
| **`outlets_details`** | **Yes, every poll** | `CurrentProductId` per outlet updates immediately. This is the right source for "who is on which outlet right now?" |
| **`product_setup` snapshot** | **Usually no** | No `variety_change` / `layout_change`; both GUIDs were already on the layout → no `unknown_product_id`. Matches Compac: reassigning known products does not require re-fetching the full layout. |

So **repositioning alone does not force a new `product_setup` row** in the current implementation. That is intentional, not a bug.

### Changing the makeup of an existing product (edit in place)

Example: same product GUID, but elements change from size 70 to size 80, or pack style changes, without changing layout ID or outlet positions.

| Layer | Picked up? | Notes |
| --- | --- | --- |
| **`outlets_details`** | **No makeup detail** | Still shows the same `CurrentProductId`; cannot see that grade/size definition changed. |
| **`product_setup` snapshot** | **Usually no** | Layout/variety IDs unchanged; GUID already in cached product list → no `unknown_product_id`. **In-place product edits are the main gap.** |

Makeup text (`elements`, `pack_name`, etc.) stays whatever the **last** `product_setup` row captured until something triggers a refresh (see table below).

### When a new `product_setup` row *is* stored

| Situation | Typical trigger | Makeup / plan refreshed? |
| --- | --- | --- |
| Service just started | `first_run` | Yes |
| Batch reports new variety | `variety_change` | Yes |
| Batch reports new layout | `layout_change` | Yes |
| New product GUID appears at an outlet (`CurrentProductId` or `PendingProductId`) | `unknown_product_id` | Yes |
| Swap to another **existing** product on an outlet | *(none)* | Live GUID in `outlets_details`; makeup labels may be stale until a row above fires |
| Move products between outlets only | *(none)* | Live positions in `outlets_details`; snapshot unchanged |
| Edit product definition in place (same GUID) | *(none)* | Stale until refresh trigger above |

After a trigger, the tracker still deduplicates: if the new envelope's SHA-256 matches the previous one, no duplicate row is written.

### Practical takeaway (which table to trust)

| You need to know… | Use… | Caveat |
| --- | --- | --- |
| Live product GUID per outlet, FPM, status | `outlets_details` / `v_outlet_product_detail` (`current_product_id`, `delivered_fpm`) | Always current each poll. |
| Planned vs live (on plan?) | `v_outlet_product_detail.matches_plan` | `planned_product_id` comes from last `product_setup`; live GUID is current. After reposition without refresh, `matches_plan` can be meaningful for GUID comparison but enriched names may lag. |
| Grade / size / quality / pack text | `elements`, `product_name` on `v_outlet_product_detail` | Enriched from **latest** `product_setup` at or before that timestamp. Stale after in-place product edits until a refresh. |
| When variety/layout/setup era changed | `v_product_setup_history` | One row per stored snapshot, not per outlet shuffle. |

We do **not** subscribe to Compac event services yet. See [SIZEREVENTSERVICE.MD](SIZEREVENTSERVICE.MD) for the supported event name list and `IEventCallback.OnEvent`.

Important: Compac’s `SizerEventServiceEventNames` does **not** include layout-change or outlet-assignment events. Events such as `BatchChange`, `BatchStageChanged`, `PackComplete`, `ReSync`, and `UserDataChanged` can still justify refreshing `product_setup`, but **outlet shuffle** and **in-place product makeup edits** may still only appear in polled `outlets_details` until we confirm `EventArgs` on `UserData*` / `ReSync` or add other mitigations (scheduled layout refresh, outlet-map comparison). Possible future work: subscribe via `ISizerEventService` or `ISizerCallbackEventService` and call `ProductSetupTracker` from `OnEvent` when batch or resync events arrive.

## Data Sources For Reporting

Two reporting views and one helper function are defined for product setup queries.

- `public.latest_product_setup(p_serial text, p_ts timestamptz)`: returns the most recent `product_setup` row at or before `p_ts` for a serial. Use this when you have an arbitrary timestamp and need the active setup era.
- `public.v_product_setup_history`: one row per setup change, including `setup_ts`, `next_setup_ts`, variety, layout, trigger, product count, and assignment count. Use this for "when did the setup change?" timelines.
- `public.v_outlet_product_detail`: explodes the per-poll `outlets_details` array into one row per outlet per timestamp, then joins each row to the active product setup. Use this for "what was at outlet N at time T?" questions.

Important columns on `public.v_outlet_product_detail`:

| Column | Meaning |
| --- | --- |
| `ts`, `serial_no`, `batch_record_id`, `outlet_id`, `outlet_name` | When and where. |
| `current_product_id`, `pending_product_id` | Live state from the outlet payload. |
| `delivered_fpm`, `max_rate_sqcm_per_min` | Per-outlet throughput and rate cap. |
| `setup_ts`, `variety_id`, `variety_name`, `layout_id`, `layout_name` | Active setup era context. |
| `product_name`, `product_display_name`, `elements`, `pack_name` | Product definition for that outlet, from the active layout. |
| `planned_product_id` | What the layout said should be there. |
| `matches_plan` | True when live product equals planned product. Null if either side is unknown. |
| `fpm_exceeds_max` | True when `delivered_fpm > max_rate_sqcm_per_min`. Null when rate is unknown or zero. |
| `assignment_entry` | The full per-outlet JSON entry from `product_setup.assignments`, in case downstream code needs more fields. |

`elements` is the JSONB array of `{Grade, Size, Quality, Label}` objects from the product definition.

## What It Does Not Do (Yet)

- It does not subscribe to Compac event services (see **What counts as a "change"?** above). In particular: **outlet reposition** and **in-place product makeup edits** usually do **not** emit a new `product_setup` row; only `outlets_details` reflects reposition immediately, and makeup can stay stale until variety/layout change, a new product GUID, or service restart.
- It does not detect outlet-to-product **shuffles** by comparing each poll's outlet map to the previous map (only the four refresh triggers listed earlier).
- It does not store any new dedicated table. Setup history lives in `public.metrics` under `metric = 'product_setup'`, the same way other Sizer metrics are stored.
- It does not currently emit alarms when `matches_plan = false` or `fpm_exceeds_max = true`. Those are reporting flags, not anomaly events.

## CLI And Config

There is no dedicated CLI for product setup. Two collector-config touch points are relevant.

- `outlets_details` and `outlets_fpm` are in `DefaultEnabledMetrics` and need to be present in `EnabledMetrics` for product setup tracking to function. `outlets_details` is required (the tracker reads its payload to detect unknown product IDs); `outlets_fpm` is optional but recommended for compact per-outlet throughput time series.
- `product_setup` is not a polled metric and must not be added to `EnabledMetrics`. It is emitted by `ProductSetupTracker` from inside the poll cycle whenever a refresh trigger fires.

Existing deployments with a custom `EnabledMetrics` override should ensure their list contains at least:

```json
{
  "EnabledMetrics": [
    "lanes_grade_fpm",
    "lanes_size_fpm",
    "machine_total_fpm",
    "machine_cupfill",
    "outlets_details",
    "outlets_fpm"
  ]
}
```

After deploying the new build, apply the SQL definitions so the helper function and views exist:

```text
SizerDataCollector.Service.exe db apply-functions
SizerDataCollector.Service.exe db apply-views
```

Restart the Windows service so the new collector binary takes effect.

## Example Queries

What product is at every outlet of a serial right now, and is anything overdriven or off-plan?

```sql
SELECT outlet_id,
       outlet_name,
       product_name,
       elements,
       delivered_fpm,
       max_rate_sqcm_per_min,
       matches_plan,
       fpm_exceeds_max
FROM public.v_outlet_product_detail
WHERE serial_no = '<SERIAL>'
  AND ts = (
      SELECT max(ts)
      FROM public.metrics
      WHERE metric = 'outlets_details' AND serial_no = '<SERIAL>'
  )
ORDER BY outlet_id;
```

When did the active setup change today, and what was the trigger each time?

```sql
SELECT setup_ts,
       trigger,
       variety_name,
       layout_name,
       product_count,
       assignment_count,
       next_setup_ts
FROM public.v_product_setup_history
WHERE serial_no = '<SERIAL>'
  AND setup_ts >= now() - interval '1 day'
ORDER BY setup_ts;
```

Which outlets exceeded their max rate in the last hour, and what product was assigned to them at the time?

```sql
SELECT ts,
       outlet_id,
       product_name,
       delivered_fpm,
       max_rate_sqcm_per_min
FROM public.v_outlet_product_detail
WHERE serial_no = '<SERIAL>'
  AND ts >= now() - interval '1 hour'
  AND fpm_exceeds_max = true
ORDER BY ts, outlet_id;
```

Which minutes had outlets running a product that did not match the planned layout?

```sql
SELECT ts,
       outlet_id,
       outlet_name,
       product_name,
       current_product_id,
       planned_product_id
FROM public.v_outlet_product_detail
WHERE serial_no = '<SERIAL>'
  AND matches_plan = false
ORDER BY ts, outlet_id;
```

What is the full set of fruit elements (grade/size/quality) being run right now across all outlets?

```sql
SELECT DISTINCT product_name,
       jsonb_array_elements(elements) AS element
FROM public.v_outlet_product_detail
WHERE serial_no = '<SERIAL>'
  AND ts = (
      SELECT max(ts)
      FROM public.metrics
      WHERE metric = 'outlets_details' AND serial_no = '<SERIAL>'
  );
```

## Validation Notes

- The `product_setup` metric uses the same `(ts, serial_no, metric)` primary key as every other metric. Duplicate inserts within the same second collapse via `ON CONFLICT DO NOTHING`.
- The tracker only emits a row when the SHA-256 of the envelope changes. If you expected a refresh and do not see a row, check the collector log for `ProductSetupTracker:` lines; a "setup content unchanged after refresh" debug line means the refresh ran but produced an identical payload.
- `v_outlet_product_detail` joins outlets to the most recent setup at or before each outlet timestamp via `LATERAL public.latest_product_setup(...)`. If `product_setup` rows are missing for a period, outlet rows in that range will still appear but with null product fields.
- `fpm_exceeds_max` and `matches_plan` are intentionally three-valued (`true`, `false`, `null`). Filters that want only confirmed cases should use `= true` rather than `IS NOT FALSE`.
