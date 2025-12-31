-- V018__reporting_views.sql
-- Reporting/diagnostic views present in reference DB.

BEGIN;

-- -------------------------
-- oee views
-- -------------------------

CREATE OR REPLACE VIEW oee.v_availability_minute AS
SELECT
    cm.minute_ts,
    cm.serial_no,
    cm.avg_rpm,
    cm.total_fpm,
    cm.min_rpm,
    cm.min_total_fpm,
    cm.state,
    cm.availability,
    CASE
        WHEN sc.break_start IS NOT NULL THEN 0::numeric
        ELSE cm.availability
    END AS availability_adj
FROM oee.cagg_availability_minute cm
LEFT JOIN oee.shift_calendar sc
  ON cm.minute_ts >= sc.break_start
 AND cm.minute_ts <  sc.break_end;

CREATE OR REPLACE VIEW oee.v_availability_minute_raw AS
SELECT
    minute_ts,
    serial_no,
    avg_rpm,
    total_fpm,
    min_rpm,
    min_total_fpm,
    state,
    availability
FROM oee.cagg_availability_minute
ORDER BY minute_ts DESC;

CREATE OR REPLACE VIEW oee.v_current_bands AS
SELECT
    machine_serial_no,
    band_name,
    lower_bound,
    upper_bound,
    effective_date,
    created_by,
    created_at
FROM oee.band_definitions
WHERE is_active = true
ORDER BY machine_serial_no, lower_bound;

CREATE OR REPLACE VIEW oee.v_grade_pct_lane_minute_batch AS
WITH raw AS (
    SELECT
        time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
        m.serial_no,
        m.batch_record_id,
        lane_idx.ordinality - 1 AS lane_no,
        kv.key AS grade_key,
        kv.value::double precision AS qty
    FROM public.metrics m
    CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane_idx(lane_json, ordinality)
    CROSS JOIN LATERAL jsonb_each_text(lane_idx.lane_json) kv(key, value)
    WHERE m.metric = 'lanes_grade_fpm'::text
),
tot AS (
    SELECT
        raw.minute_ts,
        raw.batch_record_id,
        sum(raw.qty) AS total_qty
    FROM raw
    GROUP BY raw.minute_ts, raw.batch_record_id
)
SELECT
    r.minute_ts,
    r.serial_no,
    r.batch_record_id,
    b.grower_code AS lot,
    b.comments AS variety,
    r.lane_no,
    r.grade_key,
    r.qty,
    round((r.qty * 100.0::double precision / NULLIF(t.total_qty, 0::double precision))::numeric, 2)::double precision AS pct
FROM raw r
JOIN tot t USING (minute_ts, batch_record_id)
LEFT JOIN public.batches b ON b.id = r.batch_record_id;

CREATE OR REPLACE VIEW oee.v_grade_pct_per_minute_batch AS
WITH raw AS (
    SELECT
        time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
        m.serial_no,
        m.batch_record_id,
        g.key AS grade_key,
        g.value::double precision AS qty
    FROM public.metrics m
    CROSS JOIN LATERAL jsonb_array_elements(m.value_json) lane(j)
    CROSS JOIN LATERAL jsonb_each_text(lane.j) g(key, value)
    WHERE m.metric = 'lanes_grade_fpm'::text
),
tot AS (
    SELECT
        raw.minute_ts,
        raw.batch_record_id,
        sum(raw.qty) AS total_qty
    FROM raw
    GROUP BY raw.minute_ts, raw.batch_record_id
)
SELECT
    r.minute_ts,
    r.serial_no,
    r.batch_record_id,
    b.grower_code AS lot,
    b.comments AS variety,
    r.grade_key,
    r.qty,
    round((r.qty * 100.0::double precision / NULLIF(t.total_qty, 0::double precision))::numeric, 2)::double precision AS pct
FROM raw r
JOIN tot t USING (minute_ts, batch_record_id)
LEFT JOIN public.batches b ON b.id = r.batch_record_id;

CREATE OR REPLACE VIEW oee.v_grade_pct_suffix_minute_batch AS
WITH base AS (
    SELECT
        time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
        m.serial_no,
        m.batch_record_id,
        split_part(kv.key, '_'::text, array_length(string_to_array(kv.key, '_'::text), 1)) AS suffix,
        kv.value::double precision AS qty
    FROM public.metrics m
    CROSS JOIN LATERAL jsonb_array_elements(m.value_json) l(j)
    CROSS JOIN LATERAL jsonb_each_text(l.j) kv(key, value)
    WHERE m.metric = 'lanes_grade_fpm'::text
),
sub AS (
    SELECT
        base.minute_ts,
        base.serial_no,
        base.batch_record_id,
        base.suffix,
        sum(base.qty) AS qty
    FROM base
    GROUP BY base.minute_ts, base.serial_no, base.batch_record_id, base.suffix
),
agg AS (
    SELECT
        sub.minute_ts,
        sub.serial_no,
        sub.batch_record_id,
        sub.suffix,
        sub.qty,
        sum(sub.qty) OVER (PARTITION BY sub.minute_ts, sub.batch_record_id) AS total_qty
    FROM sub
)
SELECT
    a.minute_ts,
    a.serial_no,
    a.batch_record_id,
    b.grower_code AS lot,
    b.comments AS variety,
    a.suffix,
    a.qty,
    round((a.qty * 100.0::double precision / NULLIF(a.total_qty, 0::double precision))::numeric, 2)::double precision AS pct
FROM agg a
LEFT JOIN public.batches b ON b.id = a.batch_record_id;

-- FIX 1: ensure aliases match downstream v_oee_daily_batch expectations
CREATE OR REPLACE VIEW oee.v_oee_minute_batch AS
SELECT
    a.minute_ts,
    a.serial_no,
    a.batch_record_id,
    a.lot,
    a.variety,
    a.availability AS availability_ratio,
    t.throughput_ratio AS performance_ratio,
    g.quality_ratio AS quality_ratio,
    a.availability * t.throughput_ratio * g.quality_ratio AS oee_ratio
FROM oee.cagg_availability_minute_batch a
JOIN oee.v_throughput_minute_batch t USING (minute_ts, serial_no, batch_record_id)
JOIN oee.cagg_grade_minute_batch g USING (minute_ts, serial_no, batch_record_id);

CREATE OR REPLACE VIEW oee.v_oee_daily_batch AS
SELECT
    time_bucket('1 day'::interval, minute_ts) AS day,
    serial_no,
    batch_record_id,
    min(lot) AS lot,
    min(variety) AS variety,
    avg(availability_ratio) AS avg_availability,
    avg(performance_ratio) AS avg_performance,
    avg(quality_ratio) AS avg_quality,
    avg(availability_ratio) * avg(performance_ratio) * avg(quality_ratio) AS oee_product,
    avg(oee_ratio) AS oee_average
FROM oee.v_oee_minute_batch
GROUP BY time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id;

CREATE OR REPLACE VIEW oee.v_quality_minute_batch AS
SELECT
    minute_ts,
    serial_no,
    batch_record_id,
    lot,
    variety,
    good_qty,
    peddler_qty,
    bad_qty,
    recycle_qty,
    quality_ratio
FROM oee.cagg_grade_minute_batch;

CREATE OR REPLACE VIEW oee.v_quality_daily_batch AS
SELECT
    day,
    serial_no,
    batch_record_id,
    lot,
    variety,
    good_qty,
    peddler_qty,
    bad_qty,
    recycle_qty,
    quality_ratio
FROM oee.cagg_grade_daily_batch;

CREATE OR REPLACE VIEW oee.v_throughput_daily_batch AS
SELECT
    day,
    serial_no,
    batch_record_id,
    lot,
    variety,
    total_fpm,
    missed_fpm,
    recycle_fpm,
    outlet_recycle_fpm,
    combined_recycle_fpm,
    cupfill_pct,
    tph,
    oee.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, oee.get_target_throughput()) AS throughput_ratio
FROM oee.cagg_throughput_daily_batch;

-- -------------------------
-- public views
-- -------------------------

CREATE OR REPLACE VIEW public.batch_grade_components_qv1 AS
SELECT
    b.id AS batch_id,
    b.grower_code AS lot,
    b.comments AS variety,
    b.start_ts,
    b.end_ts,
    sum(q.good_qty) AS good_qty,
    sum(q.peddler_qty) AS peddler_qty,
    sum(q.bad_qty) AS bad_qty,
    sum(q.recycle_qty) AS recycle_qty,
    public.calc_quality_ratio_qv1(
        sum(q.good_qty),
        sum(q.peddler_qty),
        sum(q.bad_qty),
        sum(q.recycle_qty)
    ) AS quality_ratio
FROM public.batches b
LEFT JOIN public.cagg_lane_grade_minute q
  ON q.minute_ts >= b.start_ts
 AND (b.end_ts IS NULL OR q.minute_ts <= b.end_ts)
GROUP BY b.id, b.grower_code, b.comments, b.start_ts, b.end_ts
ORDER BY b.start_ts DESC;

-- FIX 2: schema-qualify calc_quality_ratio_qv1 + dependency is created in V017
CREATE OR REPLACE VIEW public.daily_grade_components_qv1 AS
SELECT
    ts::date AS day,
    sum(good_qty) AS good_qty,
    sum(peddler_qty) AS peddler_qty,
    sum(bad_qty) AS bad_qty,
    sum(recycle_qty) AS recycle_qty,
    public.calc_quality_ratio_qv1(
        sum(good_qty),
        sum(peddler_qty),
        sum(bad_qty),
        sum(recycle_qty)
    ) AS quality_ratio
FROM public.minute_quality_view_qv1_old
GROUP BY ts::date;

CREATE OR REPLACE VIEW public.daily_throughput_components AS
WITH t AS (
    SELECT
        public.metrics.ts::date AS day,
        avg(CASE WHEN public.metrics.metric = 'machine_total_fpm'::text   THEN public.metrics.value_json::double precision END) AS total_fpm,
        avg(CASE WHEN public.metrics.metric = 'machine_missed_fpm'::text  THEN public.metrics.value_json::double precision END) AS missed_fpm,
        avg(CASE WHEN public.metrics.metric = 'machine_recycle_fpm'::text THEN public.metrics.value_json::double precision END) AS recycle_fpm,
        avg(CASE WHEN public.metrics.metric = 'machine_cupfill'::text     THEN public.metrics.value_json::double precision END) AS cupfill_pct,
        avg(CASE WHEN public.metrics.metric = 'machine_tph'::text         THEN public.metrics.value_json::double precision END) AS tph
    FROM public.metrics
    WHERE public.metrics.metric = ANY (ARRAY[
        'machine_total_fpm'::text,
        'machine_missed_fpm'::text,
        'machine_recycle_fpm'::text,
        'machine_cupfill'::text,
        'machine_tph'::text
    ])
    GROUP BY public.metrics.ts::date
),
o AS (
    SELECT
        public.metrics.ts::date AS day,
        avg((elem.value ->> 'DeliveredFruitPerMinute'::text)::double precision) AS outlet_recycle_fpm
    FROM public.metrics
    CROSS JOIN LATERAL jsonb_array_elements(public.metrics.value_json) elem(value)
    WHERE public.metrics.metric = 'outlets_details'::text
      AND ((elem.value ->> 'Id'::text)::integer) = (
            SELECT public.machine_settings.recycle_outlet
            FROM public.machine_settings
            LIMIT 1
      )
    GROUP BY public.metrics.ts::date
)
SELECT
    t.day,
    t.total_fpm,
    t.missed_fpm,
    t.recycle_fpm,
    COALESCE(o.outlet_recycle_fpm, 0::double precision) AS outlet_recycle_fpm,
    t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, 0::double precision) AS combined_recycle_fpm,
    t.cupfill_pct,
    t.tph,
    public.calc_perf_ratio(
        t.total_fpm,
        t.missed_fpm,
        t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, 0::double precision),
        (SELECT public.machine_settings.target_machine_speed
              * public.machine_settings.lane_count::double precision
              * public.machine_settings.target_percentage / 100::double precision
         FROM public.machine_settings
         LIMIT 1)
    ) AS throughput_ratio
FROM t
LEFT JOIN o USING (day);

CREATE OR REPLACE VIEW public.lane_size_anomaly AS
SELECT
    minute_ts,
    lane_idx,
    avg_size,
    avg(avg_size) OVER (PARTITION BY minute_ts) AS mean_size,
    stddev_pop(avg_size) OVER (PARTITION BY minute_ts) AS sd_size,
    (avg_size - avg(avg_size) OVER (PARTITION BY minute_ts))
      / NULLIF(stddev_pop(avg_size) OVER (PARTITION BY minute_ts), 0::double precision) AS z_score
FROM public.cagg_lane_size_minute;

CREATE OR REPLACE VIEW public.lane_size_health_24h AS
WITH windowed AS (
    SELECT
        public.lane_size_anomaly.lane_idx,
        abs(public.lane_size_anomaly.z_score) >= 2::double precision AS out_spec,
        public.lane_size_anomaly.z_score > 2::double precision AS oversize,
        public.lane_size_anomaly.z_score < (-2)::double precision AS undersize
    FROM public.lane_size_anomaly
    WHERE public.lane_size_anomaly.minute_ts >= (now() - '24:00:00'::interval)
)
SELECT
    lane_idx + 1 AS lane,
    count(*) AS total_min,
    count(*) FILTER (WHERE out_spec) AS out_min,
    count(*) FILTER (WHERE oversize) AS over_min,
    count(*) FILTER (WHERE undersize) AS under_min,
    round(100.0 * count(*) FILTER (WHERE oversize)::numeric / count(*)::numeric, 1) AS pct_over,
    round(100.0 * count(*) FILTER (WHERE undersize)::numeric / count(*)::numeric, 1) AS pct_under
FROM windowed
GROUP BY lane_idx
ORDER BY lane_idx + 1;

-- NOTE: this is reference-accurate but hardcodes a serial_no ('140578')
-- If you want it generalized, say so and we’ll refactor it.
CREATE OR REPLACE VIEW public.lane_size_health_season AS
WITH unpack AS (
    SELECT
        time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
        lane.ord - 1 AS lane_idx,
        v.key AS label,
        v.value::integer AS fruit_cnt
    FROM public.metrics m
    CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane(lane_json, ord)
    CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) v(key, value)
    WHERE m.metric = 'lanes_size_fpm'::text
      AND m.serial_no = '140578'::text
),
lane_minute AS (
    SELECT
        unpack.minute_ts,
        unpack.lane_idx,
        sum(unpack.fruit_cnt::double precision * public.size_group_value(unpack.label))
          / NULLIF(sum(unpack.fruit_cnt), 0)::double precision AS avg_size
    FROM unpack
    GROUP BY unpack.minute_ts, unpack.lane_idx
),
stats AS (
    SELECT
        lane_minute.minute_ts,
        lane_minute.lane_idx,
        lane_minute.avg_size,
        avg(lane_minute.avg_size) OVER (PARTITION BY lane_minute.minute_ts) AS "æ",
        stddev_pop(lane_minute.avg_size) OVER (PARTITION BY lane_minute.minute_ts) AS "å"
    FROM lane_minute
),
flags AS (
    SELECT
        stats.lane_idx,
        CASE
            WHEN stats."å" = 0::double precision OR stats.avg_size IS NULL THEN 0::double precision
            ELSE (stats.avg_size - stats."æ") / stats."å"
        END AS z
    FROM stats
)
SELECT
    lane_idx + 1 AS lane,
    count(*) AS total_min,
    count(*) FILTER (WHERE abs(z) >= 2::double precision) AS out_min,
    round(100.0 * count(*) FILTER (WHERE z >= 2::double precision)::numeric / count(*)::numeric, 2) AS pct_over,
    round(100.0 * count(*) FILTER (WHERE z <= (-2)::double precision)::numeric / count(*)::numeric, 2) AS pct_under
FROM flags
GROUP BY lane_idx
ORDER BY lane_idx + 1;

CREATE OR REPLACE VIEW public.v_quality_minute_filled AS
SELECT
    t.minute_ts,
    COALESCE(q.quality_ratio, 0.0::double precision) AS quality_ratio
FROM public.cagg_throughput_minute t
LEFT JOIN public.cagg_lane_grade_minute q ON q.minute_ts = t.minute_ts;

CREATE OR REPLACE VIEW public.v_throughput_daily AS
SELECT
    day,
    total_fpm,
    missed_fpm,
    recycle_fpm,
    outlet_recycle_fpm,
    combined_recycle_fpm,
    cupfill_pct,
    tph,
    public.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, public.get_target_throughput()) AS throughput_ratio
FROM public.cagg_throughput_daily;

CREATE OR REPLACE VIEW public.v_throughput_minute AS
SELECT
    minute_ts,
    total_fpm,
    missed_fpm,
    machine_recycle_fpm,
    outlet_recycle_fpm,
    combined_recycle_fpm,
    cupfill_pct,
    tph,
    public.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, public.get_target_throughput()) AS throughput_ratio
FROM public.cagg_throughput_minute;

COMMIT;
