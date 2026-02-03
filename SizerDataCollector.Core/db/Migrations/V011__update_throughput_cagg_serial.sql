-- Update throughput CAGG and view to pass serial_no into serial-aware settings functions
-- Drops and recreates minute-level throughput CAGG and view; reapplies refresh policy.

-- 1) Drop existing CAGG and dependent objects
DROP MATERIALIZED VIEW IF EXISTS oee.cagg_throughput_minute_batch CASCADE;

-- 2) Recreate CAGG using serial-aware outlet lookup
CREATE MATERIALIZED VIEW oee.cagg_throughput_minute_batch
WITH (timescaledb.continuous) AS
SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
       m.serial_no,
       m.batch_record_id,
       min(b.grower_code) AS lot,
       min(b.comments) AS variety,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_total_fpm'))::jsonb) AS total_fpm,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_missed_fpm'))::jsonb) AS missed_fpm,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_recycle_fpm'))::jsonb) AS machine_recycle_fpm,
       oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE m.metric = 'outlets_details'))::jsonb, oee.get_recycle_outlet(m.serial_no)) AS outlet_recycle_fpm,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_cupfill'))::jsonb) AS cupfill_pct,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_tph'))::jsonb) AS tph,
       (oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_recycle_fpm'))::jsonb)
        + oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE m.metric = 'outlets_details'))::jsonb, oee.get_recycle_outlet(m.serial_no))) AS combined_recycle_fpm
FROM metrics m
LEFT JOIN batches b ON b.id = m.batch_record_id
WHERE m.metric = ANY (ARRAY['machine_total_fpm', 'machine_missed_fpm', 'machine_recycle_fpm', 'outlets_details', 'machine_cupfill', 'machine_tph'])
GROUP BY time_bucket('00:01:00'::interval, m.ts), m.serial_no, m.batch_record_id
WITH NO DATA;

-- 3) Refresh policy reapply (idempotent)
SELECT public.add_continuous_aggregate_policy(
    'oee.cagg_throughput_minute_batch'::regclass,
    start_offset => '02:00:00',
    end_offset => '00:01:00',
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

-- 4) Drop and recreate view to use serial-aware target throughput
DROP VIEW IF EXISTS oee.v_throughput_minute_batch;

CREATE VIEW oee.v_throughput_minute_batch AS
SELECT minute_ts,
       serial_no,
       batch_record_id,
       lot,
       variety,
       total_fpm,
       missed_fpm,
       machine_recycle_fpm,
       outlet_recycle_fpm,
       cupfill_pct,
       tph,
       combined_recycle_fpm,
       oee.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, oee.get_target_throughput(serial_no)) AS throughput_ratio
FROM oee.cagg_throughput_minute_batch;

