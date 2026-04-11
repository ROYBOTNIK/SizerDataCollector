# Database reference snapshots (historical)

Files in this folder (`01_app_schema_only_*.sql`, `03_cagg_create_*.sql`, `04_cagg_policies_*.sql`, `05_inventory_*.txt`, etc.) are **point-in-time exports** from a production-style database. They are useful for diffing and archaeology, **not** as the live schema contract.

**Superseded objects:** Older snapshots may include **`public.cagg_lane_grade_minute`** and views that referenced it (for example **`public.v_quality_minute_filled`**). The current canonical definitions under `SizerDataCollector.Service/sql/definitions/` do **not** recreate that CAGG; grade rollups use **`oee.lane_grade_minute`**, the **`oee.refresh_lane_grade_minute`** job, and **`oee.cagg_lane_grade_qty_*`** CAGGs instead.

For automation and health checks, follow **`AI_AGENT_GUIDE.md`** and the service **`db list-caggs` / `db list-views`** commands (legacy objects are hidden unless `--include-legacy`).
