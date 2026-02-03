-- Serial-aware grade categorization for CAGG (per-machine overrides)

-- 1) Serial-aware grade_qty that respects overrides via oee.grade_to_cat(p_serial_no, p_grade)
CREATE OR REPLACE FUNCTION oee.grade_qty(p_serial_no text, j jsonb, desired_cat integer) RETURNS numeric
    LANGUAGE sql STABLE
AS $$
SELECT COALESCE(
         SUM((kv.value)::numeric), 0)
FROM   jsonb_array_elements(j)               AS lane(lj)
       CROSS JOIN LATERAL jsonb_each_text(lj) AS kv(key,value)
WHERE  oee.grade_to_cat(p_serial_no, kv.key) = desired_cat;
$$;

-- 2) Drop and recreate grade CAGGs (legacy pattern only inside CAGG to satisfy immutable requirement)
DROP MATERIALIZED VIEW IF EXISTS oee.cagg_grade_daily_batch;
DROP MATERIALIZED VIEW IF EXISTS oee.cagg_grade_minute_batch;

CREATE MATERIALIZED VIEW oee.cagg_grade_minute_batch
WITH (timescaledb.continuous) AS
SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
       m.serial_no,
       m.batch_record_id,
       min(b.grower_code) AS lot,
       min(b.comments) AS variety,
       oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 0) AS good_qty,
       oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 1) AS peddler_qty,
       oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 2) AS bad_qty,
       oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 3) AS recycle_qty,
       oee.calc_quality_ratio_qv1(
           oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 0),
           oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 1),
           oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 2),
           oee.grade_qty((max((m.value_json)::text) FILTER (WHERE m.metric = 'lanes_grade_fpm'))::jsonb, 3)
       ) AS quality_ratio
FROM metrics m
LEFT JOIN batches b ON b.id = m.batch_record_id
WHERE m.metric = 'lanes_grade_fpm'
GROUP BY time_bucket('00:01:00'::interval, m.ts), m.serial_no, m.batch_record_id
WITH NO DATA;

CREATE MATERIALIZED VIEW oee.cagg_grade_daily_batch
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 day'::interval, minute_ts) AS day,
       serial_no,
       batch_record_id,
       min(lot) AS lot,
       min(variety) AS variety,
       sum(good_qty) AS good_qty,
       sum(peddler_qty) AS peddler_qty,
       sum(bad_qty) AS bad_qty,
       sum(recycle_qty) AS recycle_qty,
       oee.calc_quality_ratio_qv1(sum(good_qty), sum(peddler_qty), sum(bad_qty), sum(recycle_qty)) AS quality_ratio
FROM oee.cagg_grade_minute_batch
GROUP BY time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id
WITH NO DATA;

-- 3) Re-add refresh policies (idempotent)
SELECT public.add_continuous_aggregate_policy('oee.cagg_grade_minute_batch'::regclass, start_offset => '02:00:00', end_offset => '00:01:00', schedule_interval => '00:01:00'::interval, if_not_exists => true);
SELECT public.add_continuous_aggregate_policy('oee.cagg_grade_daily_batch'::regclass,  start_offset => '14 days', end_offset => '01:00:00', schedule_interval => '01:00:00'::interval, if_not_exists => true);

