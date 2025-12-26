-- Serial-aware settings functions and refreshed throughput CAGGs
-- Replaces global-first semantics with per-serial lookups that return NULL when missing.

-- 1) Serial-aware functions (no implicit global fallback)
CREATE OR REPLACE FUNCTION oee.get_recycle_outlet(p_serial_no text) RETURNS integer
    LANGUAGE sql STABLE
AS $$
SELECT recycle_outlet
FROM public.machine_settings
WHERE serial_no = p_serial_no
ORDER BY id
LIMIT 1;
$$;

CREATE OR REPLACE FUNCTION oee.get_target_throughput(p_serial_no text) RETURNS numeric
    LANGUAGE sql STABLE
AS $$
SELECT target_machine_speed * lane_count * target_percentage / 100.0
FROM public.machine_settings
WHERE serial_no = p_serial_no
ORDER BY id
LIMIT 1;
$$;

-- 2) Backward-compatible wrappers (no-arg versions return NULL when serial unknown)
CREATE OR REPLACE FUNCTION oee.get_recycle_outlet() RETURNS integer
    LANGUAGE sql STABLE
AS $$ SELECT oee.get_recycle_outlet(NULL); $$;

CREATE OR REPLACE FUNCTION oee.get_target_throughput() RETURNS numeric
    LANGUAGE sql STABLE
AS $$ SELECT oee.get_target_throughput(NULL); $$;

-- 3) Recreate throughput CAGGs using serial-aware functions (per-serial results)
DROP MATERIALIZED VIEW IF EXISTS oee.cagg_throughput_daily_batch;
DROP MATERIALIZED VIEW IF EXISTS oee.cagg_throughput_minute_batch;
DROP MATERIALIZED VIEW IF EXISTS public.cagg_throughput_daily;
DROP MATERIALIZED VIEW IF EXISTS public.cagg_throughput_minute;

-- Minute-level (oee) — no table-reading functions inside the CAGG; join machine_settings
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
       oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE m.metric = 'outlets_details'))::jsonb, ms.recycle_outlet) AS outlet_recycle_fpm,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_cupfill'))::jsonb) AS cupfill_pct,
       oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_tph'))::jsonb) AS tph,
       (oee.num((max((m.value_json)::text) FILTER (WHERE m.metric = 'machine_recycle_fpm'))::jsonb)
        + oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE m.metric = 'outlets_details'))::jsonb, ms.recycle_outlet)) AS combined_recycle_fpm,
       (ms.target_machine_speed * ms.lane_count * ms.target_percentage / 100.0)::numeric AS target_throughput
FROM metrics m
LEFT JOIN batches b ON b.id = m.batch_record_id
LEFT JOIN public.machine_settings ms ON ms.serial_no = m.serial_no
WHERE m.metric = ANY (ARRAY['machine_total_fpm', 'machine_missed_fpm', 'machine_recycle_fpm', 'outlets_details', 'machine_cupfill', 'machine_tph'])
GROUP BY time_bucket('00:01:00'::interval, m.ts), m.serial_no, m.batch_record_id,
         ms.target_machine_speed, ms.lane_count, ms.target_percentage, ms.recycle_outlet
WITH NO DATA;

-- Daily (oee)
CREATE MATERIALIZED VIEW oee.cagg_throughput_daily_batch
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day'::interval, minute_ts) AS day,
       serial_no,
       batch_record_id,
       min(lot) AS lot,
       min(variety) AS variety,
       avg(total_fpm) AS total_fpm,
       avg(missed_fpm) AS missed_fpm,
       avg(machine_recycle_fpm) AS recycle_fpm,
       avg(outlet_recycle_fpm) AS outlet_recycle_fpm,
       avg(combined_recycle_fpm) AS combined_recycle_fpm,
       avg(cupfill_pct) AS cupfill_pct,
       avg(tph) AS tph
FROM oee.cagg_throughput_minute_batch
GROUP BY time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id
WITH NO DATA;

-- Minute-level (public) per-serial, no table-reading functions in the CAGG
CREATE MATERIALIZED VIEW public.cagg_throughput_minute
WITH (timescaledb.continuous) AS
SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
       m.serial_no,
       max((m.value_json)::double precision) FILTER (WHERE m.metric = 'machine_total_fpm') AS total_fpm,
       COALESCE(max((m.value_json)::double precision) FILTER (WHERE m.metric = 'machine_missed_fpm'), 0) AS missed_fpm,
       COALESCE(max((m.value_json)::double precision) FILTER (WHERE m.metric = 'machine_recycle_fpm'), 0) AS machine_recycle_fpm,
       max((m.value_json)::double precision) FILTER (WHERE m.metric = 'machine_cupfill') AS cupfill_pct,
       max((m.value_json)::double precision) FILTER (WHERE m.metric = 'machine_tph') AS tph,
       max(public.outlet_recycle_fpm(m.value_json, ms.recycle_outlet)) FILTER (WHERE m.metric = 'outlets_details') AS outlet_recycle_fpm,
       (COALESCE(max((m.value_json)::double precision) FILTER (WHERE m.metric = 'machine_recycle_fpm'), 0)
        + COALESCE(max(public.outlet_recycle_fpm(m.value_json, ms.recycle_outlet)) FILTER (WHERE m.metric = 'outlets_details'), 0)) AS combined_recycle_fpm,
       (ms.target_machine_speed * ms.lane_count * ms.target_percentage / 100.0)::double precision AS target_throughput
FROM metrics m
LEFT JOIN public.machine_settings ms ON ms.serial_no = m.serial_no
WHERE m.metric = ANY (ARRAY['machine_total_fpm', 'machine_missed_fpm', 'machine_recycle_fpm', 'machine_cupfill', 'machine_tph', 'outlets_details'])
GROUP BY time_bucket('00:01:00'::interval, m.ts), m.serial_no,
         ms.target_machine_speed, ms.lane_count, ms.target_percentage, ms.recycle_outlet
WITH NO DATA;

-- Daily (public) per-serial
CREATE MATERIALIZED VIEW public.cagg_throughput_daily
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day'::interval, minute_ts) AS day,
       serial_no,
       avg(total_fpm) AS total_fpm,
       avg(missed_fpm) AS missed_fpm,
       avg(machine_recycle_fpm) AS recycle_fpm,
       avg(outlet_recycle_fpm) AS outlet_recycle_fpm,
       avg(combined_recycle_fpm) AS combined_recycle_fpm,
       avg(cupfill_pct) AS cupfill_pct,
       avg(tph) AS tph
FROM public.cagg_throughput_minute
GROUP BY time_bucket('1 day'::interval, minute_ts), serial_no
WITH NO DATA;

-- 4) Re-add refresh policies (idempotent)
SELECT public.add_continuous_aggregate_policy('oee.cagg_throughput_daily_batch'::regclass,  start_offset => '14 days', end_offset => '01:00:00', schedule_interval => '01:00:00'::interval, if_not_exists => true);
SELECT public.add_continuous_aggregate_policy('oee.cagg_throughput_minute_batch'::regclass, start_offset => '02:00:00', end_offset => '00:01:00', schedule_interval => '00:01:00'::interval, if_not_exists => true);
SELECT public.add_continuous_aggregate_policy('public.cagg_throughput_daily'::regclass,     start_offset => '2 days',   end_offset => '00:00:00', schedule_interval => '01:00:00'::interval, if_not_exists => true);
SELECT public.add_continuous_aggregate_policy('public.cagg_throughput_minute'::regclass,    start_offset => '00:02:00', end_offset => '00:00:00', schedule_interval => '00:01:00'::interval, if_not_exists => true);

