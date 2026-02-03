# Refresh Policy Self-Test (TimescaleDB)

Run this psql query against the target database to verify refresh policies are attached to all continuous aggregates:

```sql
SELECT
  ca.view_schema,
  ca.view_name,
  j.job_id,
  j.schedule_interval,
  j.config
FROM timescaledb_information.continuous_aggregates ca
JOIN timescaledb_information.jobs j
  ON j.proc_name = 'policy_refresh_continuous_aggregate'
 AND (j.config->>'mat_hypertable_id')::int =
     (SELECT h.id
      FROM _timescaledb_catalog.hypertable h
      WHERE h.schema_name = ca.materialization_hypertable_schema
        AND h.table_name  = ca.materialization_hypertable_name)
ORDER BY ca.view_schema, ca.view_name;
```

Expected result: **12 rows** (one per continuous aggregate).

If you get 12 rows here but the UI still shows missing policies, the issue is in the DbIntrospector mapping logic (schema/name mismatch or case/whitespace). The code compares CAGG identity using the schema and view_name returned by Timescale system views, exactly.

