/*******************************************************************************
 * views.sql
 *
 * Authoritative definition file for all regular (non-CAGG) views in the
 * oee and public schemas.
 *
 * Matches production database: sizer_metrics_staging (dumped 2026-02-26).
 *
 * Order: oee schema views first, then public schema views.
 ******************************************************************************/

-- ============================================================================
-- OEE SCHEMA VIEWS
-- ============================================================================

CREATE OR REPLACE VIEW oee.v_quality_minute_batch AS
SELECT c.minute_ts,
       c.serial_no,
       (c.batch_record_id)::bigint AS batch_record_id,
       sum(
           CASE
               WHEN (c.cat = 0) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision)
               ELSE (0)::double precision
           END) AS good_qty,
       sum(
           CASE
               WHEN (c.cat = 1) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision)
               ELSE (0)::double precision
           END) AS peddler_qty,
       sum(
           CASE
               WHEN (c.cat = 2) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision)
               ELSE (0)::double precision
           END) AS bad_qty,
       sum(
           CASE
               WHEN (c.cat = 3) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision)
               ELSE (0)::double precision
           END) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
           c.serial_no,
           COALESCE(sum(CASE WHEN (c.cat = 0) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 1) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 2) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 3) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision)
       ))::double precision AS quality_ratio,
       min(b.grower_code) AS lot,
       min(b.comments) AS variety
FROM oee.cagg_grade_minute_batch c
LEFT JOIN public.batches b ON (b.id = c.batch_record_id)
GROUP BY c.minute_ts, c.serial_no, c.batch_record_id;

-- Define serial-aware throughput views before OEE rollup views that depend on them.
CREATE OR REPLACE VIEW oee.v_throughput_daily_batch AS
SELECT day,
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
       target_throughput,
       oee.calc_perf_ratio(serial_no, total_fpm, missed_fpm, combined_recycle_fpm, target_throughput) AS throughput_ratio
FROM oee.cagg_throughput_daily_batch;

CREATE OR REPLACE VIEW oee.v_throughput_minute_batch AS
SELECT minute_ts,
       serial_no,
       batch_record_id,
       lot,
       variety,
       total_fpm,
       missed_fpm,
       machine_recycle_fpm,
       outlet_recycle_fpm,
       combined_recycle_fpm,
       cupfill_pct,
       tph,
       target_throughput,
       oee.calc_perf_ratio(serial_no, total_fpm, missed_fpm, combined_recycle_fpm, target_throughput) AS throughput_ratio
FROM oee.cagg_throughput_minute_batch;

CREATE OR REPLACE VIEW oee.cagg_oee_minute_batch AS
SELECT a.minute_ts,
       a.serial_no,
       a.batch_record_id,
       a.availability AS availability_ratio,
       t.throughput_ratio,
       (q.quality_ratio)::numeric AS quality_ratio,
       ((a.availability * t.throughput_ratio) * (q.quality_ratio)::numeric) AS oee_score,
       COALESCE(a.lot, t.lot, q.lot) AS lot,
       COALESCE(a.variety, t.variety, q.variety) AS variety
FROM oee.cagg_availability_minute_batch a
JOIN oee.v_throughput_minute_batch t
     ON (t.minute_ts = a.minute_ts AND t.serial_no = a.serial_no AND t.batch_record_id = a.batch_record_id)
JOIN oee.v_quality_minute_batch q
     ON (q.minute_ts = a.minute_ts AND q.serial_no = a.serial_no AND q.batch_record_id = a.batch_record_id);

CREATE OR REPLACE VIEW oee.cagg_oee_daily_batch AS
SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
       serial_no,
       batch_record_id,
       avg(availability_ratio) AS availability_ratio,
       avg(throughput_ratio) AS throughput_ratio,
       avg(quality_ratio) AS quality_ratio,
       avg(oee_score) AS oee_score,
       min(lot) AS lot,
       min(variety) AS variety
FROM oee.cagg_oee_minute_batch
GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id;

CREATE OR REPLACE VIEW oee.oee_minute_batch AS
SELECT a.minute_ts,
       a.serial_no,
       a.batch_record_id,
       COALESCE(a.lot, t.lot, q.lot) AS lot,
       COALESCE(a.variety, t.variety, q.variety) AS variety,
       a.availability AS availability_ratio,
       t.throughput_ratio,
       (q.quality_ratio)::numeric AS quality_ratio,
       ((a.availability * t.throughput_ratio) * (q.quality_ratio)::numeric) AS oee_score
FROM oee.cagg_availability_minute_batch a
JOIN oee.v_throughput_minute_batch t
     ON (t.minute_ts = a.minute_ts AND t.serial_no = a.serial_no AND t.batch_record_id = a.batch_record_id)
JOIN oee.v_quality_minute_batch q
     ON (q.minute_ts = a.minute_ts AND q.serial_no = a.serial_no AND q.batch_record_id = a.batch_record_id);

CREATE OR REPLACE VIEW oee.v_availability_daily_batch AS
SELECT day,
       serial_no,
       batch_record_id,
       minutes_run,
       minutes_idle,
       minutes_down,
       avg_availability,
       (avg_availability)::double precision AS availability_ratio,
       lot,
       variety
FROM oee.cagg_availability_daily_batch c;

CREATE OR REPLACE VIEW oee.v_availability_minute AS
SELECT cm.minute_ts,
       cm.serial_no,
       cm.avg_rpm,
       cm.total_fpm,
       cm.min_rpm,
       cm.min_total_fpm,
       cm.state,
       cm.availability,
       CASE
           WHEN (sc.break_start IS NOT NULL) THEN (0)::numeric
           ELSE cm.availability
       END AS availability_adj
FROM oee.cagg_availability_minute cm
LEFT JOIN oee.shift_calendar sc
     ON (cm.minute_ts >= sc.break_start AND cm.minute_ts < sc.break_end);

CREATE OR REPLACE VIEW oee.v_availability_minute_batch AS
SELECT minute_ts,
       serial_no,
       batch_record_id,
       avg_rpm,
       total_fpm,
       min_rpm,
       min_total_fpm,
       state,
       availability,
       (oee.availability_ratio(state))::double precision AS availability_ratio,
       lot,
       variety
FROM oee.cagg_availability_minute_batch c;

CREATE OR REPLACE VIEW oee.v_availability_minute_raw AS
SELECT minute_ts,
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
SELECT machine_serial_no,
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
SELECT c.minute_ts,
       c.serial_no,
       (c.batch_record_id)::bigint AS batch_record_id,
       c.lane_no,
       c.grade_key,
       c.qty,
       (round((((c.qty * (100.0)::double precision) / NULLIF(sum(c.qty) OVER (PARTITION BY c.minute_ts, c.serial_no, c.batch_record_id, c.lane_no), (0)::double precision)))::numeric, 2))::double precision AS pct,
       b.grower_code AS lot,
       b.comments AS variety,
       c.grade_name
FROM oee.cagg_lane_grade_qty_minute_batch c
LEFT JOIN public.batches b ON (b.id = c.batch_record_id);

CREATE OR REPLACE VIEW oee.v_grade_pct_per_minute_batch AS
SELECT c.minute_ts,
       c.serial_no,
       (c.batch_record_id)::bigint AS batch_record_id,
       b.grower_code AS lot,
       b.comments AS variety,
       c.grade_key,
       c.qty,
       (round((((c.qty * (100.0)::double precision) / NULLIF(sum(c.qty) OVER (PARTITION BY c.minute_ts, c.serial_no, c.batch_record_id), (0)::double precision)))::numeric, 2))::double precision AS pct,
       c.grade_name
FROM oee.cagg_grade_qty_minute_batch c
LEFT JOIN public.batches b ON (b.id = c.batch_record_id);

CREATE OR REPLACE VIEW oee.v_grade_pct_suffix_lane_minute_batch AS
WITH samples AS (
        SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
               m.serial_no,
               m.batch_record_id,
               (lane_idx.ordinality - 1) AS lane_no,
               lane_idx.lane_json
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane_idx(lane_json, ordinality)
        WHERE m.metric = 'lanes_grade_fpm'::text
          AND jsonb_typeof(m.value_json) = 'array'::text
          AND lane_idx.lane_json IS NOT NULL
          AND jsonb_typeof(lane_idx.lane_json) = 'object'::text
), exploded AS (
        SELECT s.minute_ts,
               s.serial_no,
               s.batch_record_id,
               s.lane_no,
               split_part(kv.key, '_'::text, array_length(string_to_array(kv.key, '_'::text), 1)) AS suffix,
               (kv.value)::double precision AS qty
        FROM samples s
        CROSS JOIN LATERAL jsonb_each_text(s.lane_json) kv(key, value)
        WHERE kv.value IS NOT NULL AND kv.value <> ''::text
), avg_per_lane_suffix AS (
        SELECT exploded.minute_ts,
               exploded.serial_no,
               exploded.batch_record_id,
               exploded.lane_no,
               exploded.suffix,
               avg(exploded.qty) AS qty
        FROM exploded
        GROUP BY exploded.minute_ts, exploded.serial_no, exploded.batch_record_id, exploded.lane_no, exploded.suffix
), lane_totals AS (
        SELECT avg_per_lane_suffix.minute_ts,
               avg_per_lane_suffix.serial_no,
               avg_per_lane_suffix.batch_record_id,
               avg_per_lane_suffix.lane_no,
               sum(avg_per_lane_suffix.qty) AS lane_total_qty
        FROM avg_per_lane_suffix
        GROUP BY avg_per_lane_suffix.minute_ts, avg_per_lane_suffix.serial_no, avg_per_lane_suffix.batch_record_id, avg_per_lane_suffix.lane_no
), minute_totals AS (
        SELECT avg_per_lane_suffix.minute_ts,
               avg_per_lane_suffix.serial_no,
               avg_per_lane_suffix.batch_record_id,
               sum(avg_per_lane_suffix.qty) AS minute_total_qty
        FROM avg_per_lane_suffix
        GROUP BY avg_per_lane_suffix.minute_ts, avg_per_lane_suffix.serial_no, avg_per_lane_suffix.batch_record_id
)
SELECT a.minute_ts,
       a.serial_no,
       a.batch_record_id,
       b.grower_code AS lot,
       b.comments AS variety,
       a.lane_no,
       a.suffix,
       a.qty,
       lt.lane_total_qty,
       mt.minute_total_qty,
       (round((((a.qty * (100.0)::double precision) / NULLIF(lt.lane_total_qty, (0)::double precision)))::numeric, 2))::double precision AS pct_of_lane,
       (round((((a.qty * (100.0)::double precision) / NULLIF(mt.minute_total_qty, (0)::double precision)))::numeric, 2))::double precision AS pct_of_minute
FROM avg_per_lane_suffix a
JOIN lane_totals lt USING (minute_ts, serial_no, batch_record_id, lane_no)
JOIN minute_totals mt USING (minute_ts, serial_no, batch_record_id)
LEFT JOIN public.batches b ON (b.id = a.batch_record_id);

CREATE OR REPLACE VIEW oee.v_grade_pct_suffix_minute_batch AS
WITH lanes AS (
        SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
               m.serial_no,
               m.batch_record_id,
               (x.ord - 1) AS lane_no,
               x.lane_json
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY x(lane_json, ord)
        WHERE m.metric = 'lanes_grade_fpm'::text
          AND jsonb_typeof(m.value_json) = 'array'::text
          AND x.lane_json IS NOT NULL
          AND jsonb_typeof(x.lane_json) = 'object'::text
          AND (x.ord - 1) < oee.get_lane_count(m.serial_no)
), kv AS (
        SELECT l.minute_ts,
               l.serial_no,
               l.batch_record_id,
               g.key AS grade_key,
               (NULLIF(g.value, ''::text))::double precision AS qty
        FROM lanes l
        CROSS JOIN LATERAL jsonb_each_text(l.lane_json) g(key, value)
        WHERE g.value IS NOT NULL AND g.value <> ''::text
), suffix_avg AS (
        SELECT kv.minute_ts,
               kv.serial_no,
               kv.batch_record_id,
               regexp_replace(kv.grade_key, '^.*?(_.*)$'::text, '\1'::text) AS suffix,
               avg(kv.qty) AS qty
        FROM kv
        GROUP BY kv.minute_ts, kv.serial_no, kv.batch_record_id, regexp_replace(kv.grade_key, '^.*?(_.*)$'::text, '\1'::text)
), tot AS (
        SELECT suffix_avg.minute_ts,
               suffix_avg.serial_no,
               suffix_avg.batch_record_id,
               sum(suffix_avg.qty) AS total_qty
        FROM suffix_avg
        GROUP BY suffix_avg.minute_ts, suffix_avg.serial_no, suffix_avg.batch_record_id
)
SELECT s.minute_ts,
       s.serial_no,
       s.batch_record_id,
       b.grower_code AS lot,
       b.comments AS variety,
       s.suffix,
       s.qty,
       (round((((s.qty * (100.0)::double precision) / NULLIF(t.total_qty, (0)::double precision)))::numeric, 2))::double precision AS pct
FROM suffix_avg s
JOIN tot t ON (t.minute_ts = s.minute_ts AND t.serial_no = s.serial_no AND t.batch_record_id = s.batch_record_id)
LEFT JOIN public.batches b ON (b.id = s.batch_record_id);

CREATE OR REPLACE VIEW oee.v_lane_grade_sample_minute AS
WITH base AS (
        SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
               m.serial_no,
               m.ts AS metric_ts,
               s.sample_json,
               b_lateral.id AS batch_id,
               b_lateral.grower_code AS lot,
               b_lateral.comments AS variety
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) s(sample_json)
        LEFT JOIN LATERAL (
            SELECT b.batch_id,
                   b.serial_no,
                   b.grower_code,
                   b.start_ts,
                   b.end_ts,
                   b.comments,
                   b.id
            FROM public.batches b
            WHERE (b.id = COALESCE((m.batch_id)::bigint, m.batch_record_id))
               OR (b.start_ts <= m.ts AND (b.end_ts IS NULL OR m.ts <= b.end_ts))
            ORDER BY (b.id = COALESCE((m.batch_id)::bigint, m.batch_record_id)) DESC, b.start_ts DESC
            LIMIT 1
        ) b_lateral ON true
        WHERE m.metric = 'lanes_grade_fpm'::text
          AND jsonb_typeof(m.value_json) = 'array'::text
          AND s.sample_json IS NOT NULL
          AND jsonb_typeof(s.sample_json) = 'object'::text
), expanded AS (
        SELECT base.minute_ts,
               base.serial_no,
               base.batch_id,
               base.lot,
               base.variety,
               kv.key AS grade_key,
               (NULLIF(kv.value, ''::text))::double precision AS qty
        FROM base
        CROSS JOIN LATERAL jsonb_each_text(base.sample_json) kv(key, value)
        WHERE kv.value IS NOT NULL AND kv.value <> ''::text
), with_lane AS (
        SELECT e.minute_ts,
               e.serial_no,
               e.batch_id,
               e.lot,
               e.variety,
               e.grade_key,
               e.qty,
               CASE
                   WHEN (e.grade_key ~ '^[0-9]+\s+'::text) THEN ((regexp_match(e.grade_key, '^([0-9]+)\s+'::text))[1])::bigint
                   ELSE NULL::bigint
               END AS lane_no,
               regexp_replace(e.grade_key, '^[0-9]+\s+'::text, ''::text) AS grade_name
        FROM expanded e
), filtered AS (
        SELECT wl.minute_ts,
               wl.serial_no,
               wl.batch_id,
               wl.lot,
               wl.variety,
               wl.grade_key,
               wl.qty,
               wl.lane_no,
               wl.grade_name
        FROM with_lane wl
        CROSS JOIN LATERAL (SELECT (oee.get_lane_count(wl.serial_no))::bigint AS lane_count) lc
        WHERE wl.lane_no IS NULL OR wl.lane_no < lc.lane_count
)
SELECT minute_ts,
       serial_no,
       batch_id,
       lane_no,
       grade_name,
       qty,
       lot,
       variety
FROM filtered;

CREATE OR REPLACE VIEW oee.v_lane_grade_minute_avg AS
SELECT minute_ts,
       serial_no,
       batch_id,
       lane_no,
       grade_name,
       avg(qty) AS avg_qty,
       lot,
       variety
FROM oee.v_lane_grade_sample_minute
GROUP BY minute_ts, serial_no, batch_id, lane_no, grade_name, lot, variety;

CREATE OR REPLACE VIEW oee.v_oee_daily_batch AS
SELECT day,
       serial_no,
       batch_record_id,
       availability_ratio,
       throughput_ratio,
       quality_ratio,
       oee_score,
       lot,
       variety
FROM oee.cagg_oee_daily_batch;

CREATE OR REPLACE VIEW oee.v_oee_minute_batch AS
SELECT minute_ts,
       serial_no,
       batch_record_id,
       availability_ratio,
       throughput_ratio,
       quality_ratio,
       oee_score,
       lot,
       variety
FROM oee.cagg_oee_minute_batch;

CREATE OR REPLACE VIEW oee.v_quality_batch_components AS
SELECT batch_record_id AS batch_id,
       min(minute_ts) AS start_ts,
       max(minute_ts) AS end_ts,
       serial_no,
       sum(COALESCE(good_qty, (0)::double precision)) AS good_qty,
       sum(COALESCE(peddler_qty, (0)::double precision)) AS peddler_qty,
       sum(COALESCE(bad_qty, (0)::double precision)) AS bad_qty,
       sum(COALESCE(recycle_qty, (0)::double precision)) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
           serial_no,
           sum(COALESCE(good_qty, (0)::double precision)),
           sum(COALESCE(peddler_qty, (0)::double precision)),
           sum(COALESCE(bad_qty, (0)::double precision)),
           sum(COALESCE(recycle_qty, (0)::double precision))
       ))::double precision AS quality_ratio,
       min(lot) AS lot,
       min(variety) AS variety
FROM oee.v_quality_minute_batch
GROUP BY batch_record_id, serial_no
ORDER BY min(minute_ts) DESC;

CREATE OR REPLACE VIEW oee.v_quality_daily_batch AS
SELECT c.day,
       c.serial_no,
       (c.batch_record_id)::bigint AS batch_record_id,
       sum(CASE WHEN (c.cat = 0) THEN c.qty ELSE (0)::double precision END) AS good_qty,
       sum(CASE WHEN (c.cat = 1) THEN c.qty ELSE (0)::double precision END) AS peddler_qty,
       sum(CASE WHEN (c.cat = 2) THEN c.qty ELSE (0)::double precision END) AS bad_qty,
       sum(CASE WHEN (c.cat = 3) THEN c.qty ELSE (0)::double precision END) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
           c.serial_no,
           COALESCE(sum(CASE WHEN (c.cat = 0) THEN c.qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 1) THEN c.qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 2) THEN c.qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 3) THEN c.qty ELSE (0)::double precision END), (0)::double precision)
       ))::double precision AS quality_ratio,
       min(b.grower_code) AS lot,
       min(b.comments) AS variety
FROM oee.cagg_quality_cat_daily_batch c
LEFT JOIN public.batches b ON (b.id = c.batch_record_id)
GROUP BY c.day, c.serial_no, c.batch_record_id;

CREATE OR REPLACE VIEW oee.v_quality_daily_components AS
SELECT (day)::date AS day,
       sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END) AS good_qty,
       sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END) AS peddler_qty,
       sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END) AS bad_qty,
       sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
           serial_no,
           COALESCE(sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END), (0)::double precision)
       ))::double precision AS quality_ratio,
       serial_no
FROM oee.cagg_quality_cat_daily_batch c
GROUP BY (day)::date, serial_no;

CREATE OR REPLACE VIEW oee.v_quality_minute_components AS
SELECT minute_ts AS ts,
       sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END) AS good_qty,
       sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END) AS peddler_qty,
       sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END) AS bad_qty,
       sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
           serial_no,
           COALESCE(sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END), (0)::double precision)
       ))::double precision AS quality_ratio,
       serial_no
FROM oee.cagg_quality_cat_minute_batch c
GROUP BY minute_ts, serial_no;

CREATE OR REPLACE VIEW oee.v_grade_anomaly_event_detail AS
SELECT a.event_ts,
       public.time_bucket('00:01:00'::interval, a.event_ts) AS minute_ts,
       date_trunc('day'::text, a.event_ts) AS event_day,
       a.serial_no,
       (a.batch_record_id)::bigint AS batch_record_id,
       a.lane_no,
       a.grade_key,
       NULL::integer AS window_hours,
       a.qty,
       a.pct AS score_pct,
       a.anomaly_score AS score_z,
       a.severity,
       CASE
           WHEN (a.severity = 'High'::text) THEN 3
           WHEN (a.severity = 'Medium'::text) THEN 2
           WHEN (a.severity = 'Low'::text) THEN 1
           ELSE 0
       END AS severity_rank,
       a.model_version,
       a.delivered_to,
       a.explanation
FROM oee.grade_lane_anomalies a;

CREATE OR REPLACE VIEW oee.v_size_anomaly_event_detail AS
SELECT a.event_ts,
       public.time_bucket('00:01:00'::interval, a.event_ts) AS minute_ts,
       date_trunc('day'::text, a.event_ts) AS event_day,
       a.serial_no,
       b.id AS batch_record_id,
       a.lane_no,
       NULL::text AS grade_key,
       a.window_hours,
       NULL::double precision AS qty,
       a.pct_deviation AS score_pct,
       a.z_score AS score_z,
       a.severity,
       CASE
           WHEN (a.severity = 'High'::text) THEN 3
           WHEN (a.severity = 'Medium'::text) THEN 2
           WHEN (a.severity = 'Low'::text) THEN 1
           ELSE 0
       END AS severity_rank,
       a.model_version,
       a.delivered_to,
       NULL::jsonb AS explanation
FROM oee.lane_size_anomalies a
LEFT JOIN LATERAL (
    SELECT b1.id
    FROM public.batches b1
    WHERE b1.serial_no = a.serial_no
      AND b1.start_ts <= a.event_ts
      AND (b1.end_ts IS NULL OR b1.end_ts >= a.event_ts)
    ORDER BY b1.start_ts DESC
    LIMIT 1
) b ON true;


CREATE OR REPLACE VIEW oee.v_downtime_event_detail AS
SELECT 'downtime'::text AS event_type,
       d.start_ts,
       d.end_ts,
       public.time_bucket('00:01:00'::interval, d.start_ts) AS minute_ts,
       date_trunc('day'::text, d.start_ts) AS event_day,
       d.duration_minutes,
       d.serial_no,
       d.batch_record_id,
       d.lot,
       d.variety,
       d.avg_availability_ratio,
       d.min_availability_ratio,
       d.avg_throughput_ratio,
       d.min_throughput_ratio,
       d.avg_total_fpm,
       d.min_total_fpm,
       d.avg_oee_score,
       d.reason,
       d.overlaps_lot_transition,
       d.explanation,
       d.model_version,
       d.delivered_to,
       d.detected_at
FROM oee.downtime_events d;

CREATE OR REPLACE VIEW oee.v_slowdown_event_detail AS
SELECT 'slowdown'::text AS event_type,
       s.start_ts,
       s.end_ts,
       public.time_bucket('00:01:00'::interval, s.start_ts) AS minute_ts,
       date_trunc('day'::text, s.start_ts) AS event_day,
       s.duration_minutes,
       s.serial_no,
       s.batch_record_id,
       s.lot,
       s.variety,
       s.avg_availability_ratio,
       s.min_availability_ratio,
       s.avg_throughput_ratio,
       s.min_throughput_ratio,
       s.avg_total_fpm,
       s.min_total_fpm,
       s.avg_oee_score,
       s.reason,
       s.overlaps_lot_transition,
       s.explanation,
       s.model_version,
       s.delivered_to,
       s.detected_at
FROM oee.slowdown_events s;

CREATE OR REPLACE VIEW oee.v_machine_event_detail AS
SELECT * FROM oee.v_downtime_event_detail
UNION ALL
SELECT * FROM oee.v_slowdown_event_detail;

CREATE OR REPLACE VIEW oee.v_lot_transition_throughput_event_detail AS
SELECT e.transition_ts,
       public.time_bucket('00:01:00'::interval, e.transition_ts) AS minute_ts,
       date_trunc('day'::text, e.transition_ts) AS event_day,
       e.serial_no,
       e.outgoing_batch_record_id,
       e.incoming_batch_record_id,
       e.outgoing_grower_code,
       e.incoming_grower_code,
       e.outgoing_label,
       e.incoming_label,
       concat_ws(' -> '::text, NULLIF(e.outgoing_label, ''::text), NULLIF(e.incoming_label, ''::text)) AS transition_label,
       e.disruption_start_ts,
       e.trough_ts,
       e.stable_recovery_ts,
       e.disruption_duration_minutes,
       e.pre_stable_fpm,
       e.trough_fpm,
       e.post_stable_fpm,
       e.pre_peak_fpm,
       e.post_peak_fpm,
       e.opportunity_window_start_ts,
       e.opportunity_window_end_ts,
       e.opportunity_window_minutes,
       e.integrated_fpm_minutes,
       e.counterfactual_fpm_minutes,
       e.fruit_opportunity_shortfall,
       e.availability_avg_during_disruption,
       e.availability_avg_opportunity_window,
       e.explanation,
       e.model_version,
       e.delivered_to,
       e.inserted_at
FROM oee.lot_transition_throughput_events e;

CREATE OR REPLACE VIEW oee.v_anomaly_event_detail AS
SELECT 'grade'::text AS anomaly_type,
       event_ts,
       minute_ts,
       event_day,
       serial_no,
       batch_record_id,
       lane_no,
       grade_key,
       window_hours,
       qty,
       score_pct,
       score_z,
       severity,
       severity_rank,
       model_version,
       delivered_to,
       explanation
FROM oee.v_grade_anomaly_event_detail
UNION ALL
SELECT 'size'::text AS anomaly_type,
       event_ts,
       minute_ts,
       event_day,
       serial_no,
       batch_record_id,
       lane_no,
       grade_key,
       window_hours,
       qty,
       score_pct,
       score_z,
       severity,
       severity_rank,
       model_version,
       delivered_to,
       explanation
FROM oee.v_size_anomaly_event_detail;

CREATE OR REPLACE VIEW oee.v_anomaly_offender_scorecard_daily AS
SELECT anomaly_type,
       event_day,
       serial_no,
       batch_record_id,
       lane_no,
       grade_key,
       window_hours,
       count(*) AS repeat_count,
       min(event_ts) AS first_event_ts,
       max(event_ts) AS last_event_ts,
       count(*) FILTER (WHERE (severity = 'Low'::text)) AS low_count,
       count(*) FILTER (WHERE (severity = 'Medium'::text)) AS medium_count,
       count(*) FILTER (WHERE (severity = 'High'::text)) AS high_count,
       avg(abs(score_pct)) AS avg_abs_pct,
       max(abs(score_pct)) AS max_abs_pct,
       avg(abs(score_z)) AS avg_abs_z,
       max(abs(score_z)) AS max_abs_z
FROM oee.v_anomaly_event_detail
GROUP BY anomaly_type, event_day, serial_no, batch_record_id, lane_no, grade_key, window_hours;

CREATE OR REPLACE VIEW oee.v_operational_minute_batch AS
SELECT o.minute_ts,
       o.serial_no,
       o.batch_record_id,
       o.lot,
       o.variety,
       (o.availability_ratio)::double precision AS availability_ratio,
       (o.throughput_ratio)::double precision AS throughput_ratio,
       (o.quality_ratio)::double precision AS quality_ratio,
       (o.oee_score)::double precision AS oee_score,
       t.total_fpm,
       t.missed_fpm,
       t.machine_recycle_fpm,
       t.outlet_recycle_fpm,
       t.combined_recycle_fpm,
       t.cupfill_pct,
       t.tph,
       t.target_throughput,
       q.good_qty,
       q.peddler_qty,
       q.bad_qty,
       q.recycle_qty
FROM oee.oee_minute_batch o
LEFT JOIN oee.v_throughput_minute_batch t
     ON (t.minute_ts = o.minute_ts AND t.serial_no = o.serial_no AND t.batch_record_id = o.batch_record_id)
LEFT JOIN oee.v_quality_minute_batch q
     ON (q.minute_ts = o.minute_ts AND q.serial_no = o.serial_no AND q.batch_record_id = o.batch_record_id);

CREATE OR REPLACE VIEW oee.v_shift_window AS
WITH machine_days AS (
        SELECT DISTINCT o.serial_no,
               s.timezone,
               ((o.minute_ts AT TIME ZONE s.timezone))::date AS day_local
        FROM oee.v_oee_minute_batch o
        JOIN oee.shifts s
          ON s.serial_no = o.serial_no
         AND s.is_active = true
), eligible_days AS (
        SELECT d.serial_no,
               d.timezone,
               d.day_local,
               s.shift_name,
               s.start_local,
               s.end_local,
               s.crosses_midnight,
               s.dow_mask
        FROM machine_days d
        JOIN oee.shifts s
          ON s.serial_no = d.serial_no
         AND s.timezone = d.timezone
         AND s.is_active = true
        WHERE (s.effective_from IS NULL OR d.day_local >= s.effective_from)
          AND (s.effective_to IS NULL OR d.day_local <= s.effective_to)
          AND ((s.dow_mask::integer & (1 << (extract(isodow FROM d.day_local)::integer - 1))) <> 0)
)
SELECT e.serial_no,
       e.day_local,
       e.shift_name,
       e.timezone,
       ((e.day_local::timestamp + e.start_local) AT TIME ZONE e.timezone) AS start_ts,
       (((e.day_local::timestamp + e.end_local)
         + CASE WHEN e.crosses_midnight THEN '1 day'::interval ELSE '0 day'::interval END) AT TIME ZONE e.timezone) AS end_ts
FROM eligible_days e;

CREATE OR REPLACE VIEW oee.v_availability_shift_batch AS
SELECT w.day_local,
       w.shift_name,
       w.start_ts AS shift_start_ts,
       w.end_ts AS shift_end_ts,
       a.serial_no,
       a.batch_record_id,
       count(*) AS minute_count,
       count(*) FILTER (WHERE a.state = 2) AS minutes_run,
       count(*) FILTER (WHERE a.state = 1) AS minutes_idle,
       count(*) FILTER (WHERE a.state = 0) AS minutes_down,
       avg(a.availability_ratio) AS availability_ratio,
       min(a.lot) AS lot,
       min(a.variety) AS variety
FROM oee.v_availability_minute_batch a
JOIN oee.v_shift_window w
  ON w.serial_no = a.serial_no
 AND a.minute_ts >= w.start_ts
 AND a.minute_ts < w.end_ts
GROUP BY w.day_local, w.shift_name, w.start_ts, w.end_ts, a.serial_no, a.batch_record_id;

CREATE OR REPLACE VIEW oee.v_throughput_shift_batch AS
SELECT w.day_local,
       w.shift_name,
       w.start_ts AS shift_start_ts,
       w.end_ts AS shift_end_ts,
       t.serial_no,
       t.batch_record_id,
       count(*) AS minute_count,
       avg(t.total_fpm) AS total_fpm,
       avg(t.missed_fpm) AS missed_fpm,
       avg(t.machine_recycle_fpm) AS machine_recycle_fpm,
       avg(t.outlet_recycle_fpm) AS outlet_recycle_fpm,
       avg(t.combined_recycle_fpm) AS combined_recycle_fpm,
       avg(t.cupfill_pct) AS cupfill_pct,
       avg(t.tph) AS tph,
       avg(t.target_throughput) AS target_throughput,
       avg(t.throughput_ratio) AS throughput_ratio,
       min(t.lot) AS lot,
       min(t.variety) AS variety
FROM oee.v_throughput_minute_batch t
JOIN oee.v_shift_window w
  ON w.serial_no = t.serial_no
 AND t.minute_ts >= w.start_ts
 AND t.minute_ts < w.end_ts
GROUP BY w.day_local, w.shift_name, w.start_ts, w.end_ts, t.serial_no, t.batch_record_id;

CREATE OR REPLACE VIEW oee.v_quality_shift_batch AS
SELECT w.day_local,
       w.shift_name,
       w.start_ts AS shift_start_ts,
       w.end_ts AS shift_end_ts,
       q.serial_no,
       q.batch_record_id,
       count(*) AS minute_count,
       sum(COALESCE(q.good_qty, (0)::double precision)) AS good_qty,
       sum(COALESCE(q.peddler_qty, (0)::double precision)) AS peddler_qty,
       sum(COALESCE(q.bad_qty, (0)::double precision)) AS bad_qty,
       sum(COALESCE(q.recycle_qty, (0)::double precision)) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
            q.serial_no,
            sum(COALESCE(q.good_qty, (0)::double precision)),
            sum(COALESCE(q.peddler_qty, (0)::double precision)),
            sum(COALESCE(q.bad_qty, (0)::double precision)),
            sum(COALESCE(q.recycle_qty, (0)::double precision))
        ))::double precision AS quality_ratio,
       min(q.lot) AS lot,
       min(q.variety) AS variety
FROM oee.v_quality_minute_batch q
JOIN oee.v_shift_window w
  ON w.serial_no = q.serial_no
 AND q.minute_ts >= w.start_ts
 AND q.minute_ts < w.end_ts
GROUP BY w.day_local, w.shift_name, w.start_ts, w.end_ts, q.serial_no, q.batch_record_id;

CREATE OR REPLACE VIEW oee.v_oee_shift_batch AS
SELECT a.day_local,
       a.shift_name,
       a.shift_start_ts,
       a.shift_end_ts,
       a.serial_no,
       a.batch_record_id,
       a.minute_count,
       a.minutes_run,
       a.minutes_idle,
       a.minutes_down,
       a.availability_ratio,
       t.throughput_ratio,
       q.quality_ratio,
       ((a.availability_ratio * t.throughput_ratio) * q.quality_ratio) AS oee_score,
       t.total_fpm,
       t.missed_fpm,
       t.machine_recycle_fpm,
       t.outlet_recycle_fpm,
       t.combined_recycle_fpm,
       t.cupfill_pct,
       t.tph,
       t.target_throughput,
       q.good_qty,
       q.peddler_qty,
       q.bad_qty,
       q.recycle_qty,
       COALESCE(a.lot, t.lot, q.lot) AS lot,
       COALESCE(a.variety, t.variety, q.variety) AS variety
FROM oee.v_availability_shift_batch a
JOIN oee.v_throughput_shift_batch t
  ON t.day_local = a.day_local
 AND t.shift_name = a.shift_name
 AND t.shift_start_ts = a.shift_start_ts
 AND t.shift_end_ts = a.shift_end_ts
 AND t.serial_no = a.serial_no
 AND t.batch_record_id = a.batch_record_id
JOIN oee.v_quality_shift_batch q
  ON q.day_local = a.day_local
 AND q.shift_name = a.shift_name
 AND q.shift_start_ts = a.shift_start_ts
 AND q.shift_end_ts = a.shift_end_ts
 AND q.serial_no = a.serial_no
 AND q.batch_record_id = a.batch_record_id;

CREATE OR REPLACE VIEW oee.v_oee_shift AS
SELECT w.day_local,
       w.shift_name,
       w.start_ts AS shift_start_ts,
       w.end_ts AS shift_end_ts,
       o.serial_no,
       count(*) AS total_minutes,
       count(*) FILTER (WHERE o.availability_ratio > (0)::double precision) AS run_minutes,
       avg(o.availability_ratio) AS availability_ratio,
       avg(o.throughput_ratio) AS throughput_ratio,
       avg(o.quality_ratio) AS quality_ratio,
       avg(o.oee_score) AS oee_score
FROM oee.v_operational_minute_batch o
JOIN oee.v_shift_window w
  ON w.serial_no = o.serial_no
 AND o.minute_ts >= w.start_ts
 AND o.minute_ts < w.end_ts
GROUP BY w.day_local, w.shift_name, w.start_ts, w.end_ts, o.serial_no;

CREATE OR REPLACE VIEW oee.v_grade_anomaly_impact_summary AS
SELECT 'grade'::text AS anomaly_type,
       d.event_ts,
       d.minute_ts,
       d.event_day,
       d.serial_no,
       d.batch_record_id,
       d.lane_no,
       d.grade_key,
       d.window_hours,
       d.severity,
       d.score_pct,
       d.score_z,
       d.model_version,
       d.delivered_to,
       cur.lot,
       cur.variety,
       pre.availability_ratio AS pre_availability_ratio,
       cur.availability_ratio AS event_availability_ratio,
       post.availability_ratio AS post_availability_ratio,
       pre.throughput_ratio AS pre_throughput_ratio,
       cur.throughput_ratio AS event_throughput_ratio,
       post.throughput_ratio AS post_throughput_ratio,
       pre.quality_ratio AS pre_quality_ratio,
       cur.quality_ratio AS event_quality_ratio,
       post.quality_ratio AS post_quality_ratio,
       pre.oee_score AS pre_oee_score,
       cur.oee_score AS event_oee_score,
       post.oee_score AS post_oee_score,
       cur.total_fpm AS event_total_fpm,
       cur.missed_fpm AS event_missed_fpm,
       cur.combined_recycle_fpm AS event_combined_recycle_fpm,
       cur.cupfill_pct AS event_cupfill_pct,
       cur.tph AS event_tph,
       cur.target_throughput AS event_target_throughput,
       (cur.oee_score - pre.oee_score) AS delta_oee_from_pre,
       (post.oee_score - pre.oee_score) AS delta_oee_post_vs_pre,
       (cur.throughput_ratio - pre.throughput_ratio) AS delta_throughput_from_pre,
       (post.throughput_ratio - pre.throughput_ratio) AS delta_throughput_post_vs_pre,
       (cur.quality_ratio - pre.quality_ratio) AS delta_quality_from_pre,
       (post.quality_ratio - pre.quality_ratio) AS delta_quality_post_vs_pre
FROM oee.v_grade_anomaly_event_detail d
LEFT JOIN LATERAL (
    SELECT o.lot,
           o.variety,
           o.availability_ratio,
           o.throughput_ratio,
           o.quality_ratio,
           o.oee_score,
           o.total_fpm,
           o.missed_fpm,
           o.combined_recycle_fpm,
           o.cupfill_pct,
           o.tph,
           o.target_throughput
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = d.serial_no
      AND o.batch_record_id = d.batch_record_id
      AND o.minute_ts = d.minute_ts
    LIMIT 1
) cur ON true
LEFT JOIN LATERAL (
    SELECT avg(o.availability_ratio) AS availability_ratio,
           avg(o.throughput_ratio) AS throughput_ratio,
           avg(o.quality_ratio) AS quality_ratio,
           avg(o.oee_score) AS oee_score
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = d.serial_no
      AND o.batch_record_id = d.batch_record_id
      AND o.minute_ts >= (d.minute_ts - '00:15:00'::interval)
      AND o.minute_ts < d.minute_ts
) pre ON true
LEFT JOIN LATERAL (
    SELECT avg(o.availability_ratio) AS availability_ratio,
           avg(o.throughput_ratio) AS throughput_ratio,
           avg(o.quality_ratio) AS quality_ratio,
           avg(o.oee_score) AS oee_score
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = d.serial_no
      AND o.batch_record_id = d.batch_record_id
      AND o.minute_ts > d.minute_ts
      AND o.minute_ts <= (d.minute_ts + '00:15:00'::interval)
) post ON true;

CREATE OR REPLACE VIEW oee.v_size_anomaly_impact_summary AS
SELECT 'size'::text AS anomaly_type,
       d.event_ts,
       d.minute_ts,
       d.event_day,
       d.serial_no,
       d.batch_record_id,
       d.lane_no,
       d.grade_key,
       d.window_hours,
       d.severity,
       d.score_pct,
       d.score_z,
       d.model_version,
       d.delivered_to,
       cur.lot,
       cur.variety,
       pre.availability_ratio AS pre_availability_ratio,
       cur.availability_ratio AS event_availability_ratio,
       post.availability_ratio AS post_availability_ratio,
       pre.throughput_ratio AS pre_throughput_ratio,
       cur.throughput_ratio AS event_throughput_ratio,
       post.throughput_ratio AS post_throughput_ratio,
       pre.quality_ratio AS pre_quality_ratio,
       cur.quality_ratio AS event_quality_ratio,
       post.quality_ratio AS post_quality_ratio,
       pre.oee_score AS pre_oee_score,
       cur.oee_score AS event_oee_score,
       post.oee_score AS post_oee_score,
       cur.total_fpm AS event_total_fpm,
       cur.missed_fpm AS event_missed_fpm,
       cur.combined_recycle_fpm AS event_combined_recycle_fpm,
       cur.cupfill_pct AS event_cupfill_pct,
       cur.tph AS event_tph,
       cur.target_throughput AS event_target_throughput,
       (cur.oee_score - pre.oee_score) AS delta_oee_from_pre,
       (post.oee_score - pre.oee_score) AS delta_oee_post_vs_pre,
       (cur.throughput_ratio - pre.throughput_ratio) AS delta_throughput_from_pre,
       (post.throughput_ratio - pre.throughput_ratio) AS delta_throughput_post_vs_pre,
       (cur.quality_ratio - pre.quality_ratio) AS delta_quality_from_pre,
       (post.quality_ratio - pre.quality_ratio) AS delta_quality_post_vs_pre
FROM oee.v_size_anomaly_event_detail d
LEFT JOIN LATERAL (
    SELECT o.lot,
           o.variety,
           o.availability_ratio,
           o.throughput_ratio,
           o.quality_ratio,
           o.oee_score,
           o.total_fpm,
           o.missed_fpm,
           o.combined_recycle_fpm,
           o.cupfill_pct,
           o.tph,
           o.target_throughput
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = d.serial_no
      AND (d.batch_record_id IS NULL OR o.batch_record_id = d.batch_record_id)
      AND o.minute_ts = d.minute_ts
    ORDER BY o.minute_ts DESC
    LIMIT 1
) cur ON true
LEFT JOIN LATERAL (
    SELECT avg(o.availability_ratio) AS availability_ratio,
           avg(o.throughput_ratio) AS throughput_ratio,
           avg(o.quality_ratio) AS quality_ratio,
           avg(o.oee_score) AS oee_score
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = d.serial_no
      AND (d.batch_record_id IS NULL OR o.batch_record_id = d.batch_record_id)
      AND o.minute_ts >= (d.minute_ts - '00:15:00'::interval)
      AND o.minute_ts < d.minute_ts
) pre ON true
LEFT JOIN LATERAL (
    SELECT avg(o.availability_ratio) AS availability_ratio,
           avg(o.throughput_ratio) AS throughput_ratio,
           avg(o.quality_ratio) AS quality_ratio,
           avg(o.oee_score) AS oee_score
    FROM oee.v_operational_minute_batch o
    WHERE o.serial_no = d.serial_no
      AND (d.batch_record_id IS NULL OR o.batch_record_id = d.batch_record_id)
      AND o.minute_ts > d.minute_ts
      AND o.minute_ts <= (d.minute_ts + '00:15:00'::interval)
) post ON true;

CREATE OR REPLACE VIEW oee.v_anomaly_impact_summary AS
SELECT anomaly_type,
       event_ts,
       minute_ts,
       event_day,
       serial_no,
       batch_record_id,
       lane_no,
       grade_key,
       window_hours,
       severity,
       score_pct,
       score_z,
       model_version,
       delivered_to,
       lot,
       variety,
       pre_availability_ratio,
       event_availability_ratio,
       post_availability_ratio,
       pre_throughput_ratio,
       event_throughput_ratio,
       post_throughput_ratio,
       pre_quality_ratio,
       event_quality_ratio,
       post_quality_ratio,
       pre_oee_score,
       event_oee_score,
       post_oee_score,
       event_total_fpm,
       event_missed_fpm,
       event_combined_recycle_fpm,
       event_cupfill_pct,
       event_tph,
       event_target_throughput,
       delta_oee_from_pre,
       delta_oee_post_vs_pre,
       delta_throughput_from_pre,
       delta_throughput_post_vs_pre,
       delta_quality_from_pre,
       delta_quality_post_vs_pre
FROM oee.v_grade_anomaly_impact_summary
UNION ALL
SELECT anomaly_type,
       event_ts,
       minute_ts,
       event_day,
       serial_no,
       batch_record_id,
       lane_no,
       grade_key,
       window_hours,
       severity,
       score_pct,
       score_z,
       model_version,
       delivered_to,
       lot,
       variety,
       pre_availability_ratio,
       event_availability_ratio,
       post_availability_ratio,
       pre_throughput_ratio,
       event_throughput_ratio,
       post_throughput_ratio,
       pre_quality_ratio,
       event_quality_ratio,
       post_quality_ratio,
       pre_oee_score,
       event_oee_score,
       post_oee_score,
       event_total_fpm,
       event_missed_fpm,
       event_combined_recycle_fpm,
       event_cupfill_pct,
       event_tph,
       event_target_throughput,
       delta_oee_from_pre,
       delta_oee_post_vs_pre,
       delta_throughput_from_pre,
       delta_throughput_post_vs_pre,
       delta_quality_from_pre,
       delta_quality_post_vs_pre
FROM oee.v_size_anomaly_impact_summary;

CREATE OR REPLACE VIEW oee.v_anomaly_offender_cluster_daily AS
WITH operational_minutes AS (
        SELECT date_trunc('day'::text, o.minute_ts) AS event_day,
               o.serial_no,
               count(DISTINCT o.minute_ts) AS operational_minutes
        FROM oee.v_operational_minute_batch o
        GROUP BY date_trunc('day'::text, o.minute_ts), o.serial_no
)
SELECT e.anomaly_type,
       e.event_day,
       e.serial_no,
       e.lane_no,
       e.grade_key,
       e.window_hours,
       count(*) AS repeat_count,
       min(e.event_ts) AS first_event_ts,
       max(e.event_ts) AS last_event_ts,
       (extract(epoch FROM (max(e.event_ts) - min(e.event_ts))) / 60::numeric)::double precision AS span_minutes,
       count(DISTINCT e.minute_ts) AS active_minutes,
       count(DISTINCT e.batch_record_id) AS affected_batches,
       count(DISTINCT COALESCE(b.grower_code, b.comments, '(unknown)'::text)) AS affected_lots,
       avg(e.score_pct) AS avg_score_pct,
       avg(e.score_z) AS avg_score_z,
       max(abs(e.score_pct)) AS max_abs_pct,
       max(abs(e.score_z)) AS max_abs_z,
       CASE
           WHEN (avg(e.score_pct) > 0.25::double precision) THEN 'positive_skew'::text
           WHEN (avg(e.score_pct) < '-0.25'::double precision) THEN 'negative_skew'::text
           ELSE 'balanced'::text
       END AS direction_label,
       CASE
           WHEN (COALESCE(op.operational_minutes, 0::bigint) = 0::bigint) THEN NULL::double precision
           ELSE ((100.0::double precision * (count(DISTINCT e.minute_ts))::double precision) / (op.operational_minutes)::double precision)
       END AS runtime_share_pct
FROM oee.v_anomaly_event_detail e
LEFT JOIN public.batches b
     ON (b.id = e.batch_record_id)
LEFT JOIN operational_minutes op
     ON (op.event_day = e.event_day AND op.serial_no = e.serial_no)
GROUP BY e.anomaly_type, e.event_day, e.serial_no, e.lane_no, e.grade_key, e.window_hours, op.operational_minutes;

CREATE OR REPLACE VIEW oee.v_anomaly_impact_family_summary_daily AS
SELECT s.anomaly_type,
       s.event_day,
       s.serial_no,
       s.lane_no,
       s.grade_key,
       s.window_hours,
       count(*) AS event_count,
       count(*) FILTER (WHERE (s.severity = 'High'::text)) AS high_count,
       avg(s.delta_oee_post_vs_pre) AS avg_delta_oee_post_vs_pre,
       avg(s.delta_throughput_post_vs_pre) AS avg_delta_throughput_post_vs_pre,
       avg(s.delta_quality_post_vs_pre) AS avg_delta_quality_post_vs_pre,
       count(*) FILTER (WHERE (COALESCE(s.delta_oee_post_vs_pre, (0)::double precision) < '-0.02'::double precision
                             OR COALESCE(s.delta_throughput_post_vs_pre, (0)::double precision) < '-0.02'::double precision)) AS negative_post_impact_count,
       count(*) FILTER (WHERE (s.severity = 'High'::text
                             AND (COALESCE(s.delta_oee_post_vs_pre, (0)::double precision) < '-0.02'::double precision
                               OR COALESCE(s.delta_throughput_post_vs_pre, (0)::double precision) < '-0.02'::double precision))) AS high_severity_negative_count,
       CASE
           WHEN ((count(*) FILTER (WHERE (s.severity = 'High'::text
                                       AND (COALESCE(s.delta_oee_post_vs_pre, (0)::double precision) < '-0.02'::double precision
                                         OR COALESCE(s.delta_throughput_post_vs_pre, (0)::double precision) < '-0.02'::double precision))) >= 2)
              OR (avg(s.delta_oee_post_vs_pre) <= '-0.03'::double precision)) THEN 'likely_material'::text
           WHEN ((count(*) FILTER (WHERE (COALESCE(s.delta_oee_post_vs_pre, (0)::double precision) < '-0.02'::double precision
                                       OR COALESCE(s.delta_throughput_post_vs_pre, (0)::double precision) < '-0.02'::double precision)) = 0)
              AND (COALESCE(avg(s.delta_oee_post_vs_pre), (0)::double precision) >= (0)::double precision)) THEN 'likely_non_material'::text
           ELSE 'mixed_unclear'::text
       END AS materiality_label
FROM oee.v_anomaly_impact_summary s
GROUP BY s.anomaly_type, s.event_day, s.serial_no, s.lane_no, s.grade_key, s.window_hours;

-- ============================================================================
-- PUBLIC SCHEMA VIEWS
-- ============================================================================

CREATE OR REPLACE VIEW public.minute_quality_view_qv1_old AS
WITH samples AS (
        SELECT public.time_bucket('00:01:00'::interval, m.ts) AS ts,
               m.serial_no,
               s.sample_json
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) s(sample_json)
        WHERE m.metric = 'lanes_grade_fpm'::text
          AND jsonb_typeof(m.value_json) = 'array'::text
          AND s.sample_json IS NOT NULL
          AND jsonb_typeof(s.sample_json) = 'object'::text
), grade_sample AS (
        SELECT samples.ts,
               samples.serial_no,
               g.key AS grade_name,
               (NULLIF(g.value, ''::text))::double precision AS qty
        FROM samples
        CROSS JOIN LATERAL jsonb_each_text(samples.sample_json) g(key, value)
        WHERE g.value IS NOT NULL AND g.value <> ''::text
), grade_avg AS (
        SELECT grade_sample.ts,
               grade_sample.serial_no,
               grade_sample.grade_name,
               avg(grade_sample.qty) AS avg_qty
        FROM grade_sample
        GROUP BY grade_sample.ts, grade_sample.serial_no, grade_sample.grade_name
), cats AS (
        SELECT grade_avg.ts,
               grade_avg.serial_no,
               oee.grade_to_cat(grade_avg.serial_no, grade_avg.grade_name) AS cat,
               grade_avg.avg_qty AS qty
        FROM grade_avg
)
SELECT ts,
       sum(CASE WHEN (cat = 0) THEN qty ELSE NULL::double precision END) AS good_qty,
       sum(CASE WHEN (cat = 1) THEN qty ELSE NULL::double precision END) AS peddler_qty,
       sum(CASE WHEN (cat = 2) THEN qty ELSE NULL::double precision END) AS bad_qty,
       sum(CASE WHEN (cat = 3) THEN qty ELSE NULL::double precision END) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(
           serial_no,
           COALESCE(sum(CASE WHEN (cat = 0) THEN qty ELSE NULL::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 1) THEN qty ELSE NULL::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 2) THEN qty ELSE NULL::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 3) THEN qty ELSE NULL::double precision END), (0)::double precision)
       ))::double precision AS quality_ratio,
       serial_no
FROM cats
GROUP BY ts, serial_no;

CREATE OR REPLACE VIEW public.batch_grade_components_qv1 AS
SELECT b.id AS batch_id,
       b.grower_code AS lot,
       b.comments AS variety,
       b.start_ts,
       b.end_ts,
       sum(mv.good_qty) AS good_qty,
       sum(mv.peddler_qty) AS peddler_qty,
       sum(mv.bad_qty) AS bad_qty,
       sum(mv.recycle_qty) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(mv.serial_no, sum(mv.good_qty), sum(mv.peddler_qty), sum(mv.bad_qty), sum(mv.recycle_qty)))::double precision AS quality_ratio,
       mv.serial_no
FROM public.batches b
LEFT JOIN public.minute_quality_view_qv1_old mv
     ON (mv.serial_no = b.serial_no AND mv.ts >= b.start_ts AND (b.end_ts IS NULL OR mv.ts <= b.end_ts))
GROUP BY b.id, b.grower_code, b.comments, b.start_ts, b.end_ts, mv.serial_no
ORDER BY b.start_ts DESC;

CREATE OR REPLACE VIEW public.daily_grade_components_qv1 AS
SELECT (ts)::date AS day,
       sum(good_qty) AS good_qty,
       sum(peddler_qty) AS peddler_qty,
       sum(bad_qty) AS bad_qty,
       sum(recycle_qty) AS recycle_qty,
       (oee.calc_quality_ratio_qv1(serial_no, sum(good_qty), sum(peddler_qty), sum(bad_qty), sum(recycle_qty)))::double precision AS quality_ratio,
       serial_no
FROM public.minute_quality_view_qv1_old mv
GROUP BY (ts)::date, serial_no;

CREATE OR REPLACE VIEW public.daily_throughput_components AS
WITH t AS (
        SELECT (m.ts)::date AS day,
               m.serial_no,
               avg(CASE WHEN (m.metric = 'machine_total_fpm'::text)   THEN (m.value_json)::double precision ELSE NULL::double precision END) AS total_fpm,
               avg(CASE WHEN (m.metric = 'machine_missed_fpm'::text)  THEN (m.value_json)::double precision ELSE NULL::double precision END) AS missed_fpm,
               avg(CASE WHEN (m.metric = 'machine_recycle_fpm'::text) THEN (m.value_json)::double precision ELSE NULL::double precision END) AS recycle_fpm,
               avg(CASE WHEN (m.metric = 'machine_cupfill'::text)     THEN (m.value_json)::double precision ELSE NULL::double precision END) AS cupfill_pct,
               avg(CASE WHEN (m.metric = 'machine_tph'::text)         THEN (m.value_json)::double precision ELSE NULL::double precision END) AS tph
        FROM public.metrics m
        WHERE m.metric = ANY (ARRAY['machine_total_fpm'::text, 'machine_missed_fpm'::text, 'machine_recycle_fpm'::text, 'machine_cupfill'::text, 'machine_tph'::text])
        GROUP BY (m.ts)::date, m.serial_no
), o AS (
        SELECT (m.ts)::date AS day,
               m.serial_no,
               avg(((elem.value ->> 'DeliveredFruitPerMinute'::text))::double precision) AS outlet_recycle_fpm
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) elem(value)
        WHERE m.metric = 'outlets_details'::text
          AND ((elem.value ->> 'Id'::text))::integer = oee.get_recycle_outlet(m.serial_no)
        GROUP BY (m.ts)::date, m.serial_no
)
SELECT t.day,
       t.total_fpm,
       t.missed_fpm,
       t.recycle_fpm,
       COALESCE(o.outlet_recycle_fpm, (0)::double precision) AS outlet_recycle_fpm,
       (t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, (0)::double precision)) AS combined_recycle_fpm,
       t.cupfill_pct,
       t.tph,
       (oee.calc_perf_ratio(t.serial_no, t.total_fpm, t.missed_fpm, (t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, (0)::double precision)), (oee.get_target_throughput(t.serial_no))::double precision))::double precision AS throughput_ratio,
       t.serial_no
FROM t
LEFT JOIN o ON (o.day = t.day AND o.serial_no = t.serial_no);

-- These lane-size views changed shape in serial-aware rollouts.
-- Drop/recreate to avoid CREATE OR REPLACE column-rename failures on older DBs.
DROP VIEW IF EXISTS public.lane_size_health_24h;
DROP VIEW IF EXISTS public.lane_size_health_season;
DROP VIEW IF EXISTS public.lane_size_anomaly;

-- Per-minute z-score vs other lanes on the same sizer (serial_no). Filter in dashboards: WHERE serial_no = '...'
CREATE OR REPLACE VIEW public.lane_size_anomaly AS
SELECT minute_ts,
       serial_no,
       lane_idx,
       avg_size,
       avg(avg_size) OVER (PARTITION BY minute_ts, serial_no) AS mean_size,
       stddev_pop(avg_size) OVER (PARTITION BY minute_ts, serial_no) AS sd_size,
       ((avg_size - avg(avg_size) OVER (PARTITION BY minute_ts, serial_no))
         / NULLIF(stddev_pop(avg_size) OVER (PARTITION BY minute_ts, serial_no), (0)::double precision)) AS z_score
FROM public.cagg_lane_size_minute;

CREATE OR REPLACE VIEW public.lane_size_health_24h AS
WITH windowed AS (
        SELECT lane_size_anomaly.serial_no,
               lane_size_anomaly.lane_idx,
               (abs(lane_size_anomaly.z_score) >= (2)::double precision) AS out_spec,
               (lane_size_anomaly.z_score > (2)::double precision) AS oversize,
               (lane_size_anomaly.z_score < ('-2'::integer)::double precision) AS undersize
        FROM public.lane_size_anomaly
        WHERE lane_size_anomaly.minute_ts >= (now() - '24:00:00'::interval)
)
SELECT serial_no,
       (lane_idx + 1) AS lane,
       count(*) AS total_min,
       count(*) FILTER (WHERE out_spec) AS out_min,
       count(*) FILTER (WHERE oversize) AS over_min,
       count(*) FILTER (WHERE undersize) AS under_min,
       round(((100.0 * (count(*) FILTER (WHERE oversize))::numeric) / (count(*))::numeric), 1) AS pct_over,
       round(((100.0 * (count(*) FILTER (WHERE undersize))::numeric) / (count(*))::numeric), 1) AS pct_under
FROM windowed
GROUP BY serial_no, lane_idx
ORDER BY serial_no, (lane_idx + 1);

CREATE OR REPLACE VIEW public.lane_size_health_season AS
WITH unpack AS (
        SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
               m.serial_no,
               (lane.ord - 1) AS lane_idx,
               v.key AS label,
               (v.value)::integer AS fruit_cnt
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane(lane_json, ord)
        CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) v(key, value)
        WHERE m.metric = 'lanes_size_fpm'::text
          AND jsonb_typeof(lane.lane_json) = 'object'
), lane_minute AS (
        SELECT unpack.minute_ts,
               unpack.serial_no,
               unpack.lane_idx,
               (sum(((unpack.fruit_cnt)::double precision * public.size_group_value(unpack.label)))
                 / (NULLIF(sum(unpack.fruit_cnt), 0))::double precision) AS avg_size
        FROM unpack
        GROUP BY unpack.minute_ts, unpack.serial_no, unpack.lane_idx
), stats AS (
        SELECT lane_minute.minute_ts,
               lane_minute.serial_no,
               lane_minute.lane_idx,
               lane_minute.avg_size,
               avg(lane_minute.avg_size) OVER (PARTITION BY lane_minute.minute_ts, lane_minute.serial_no) AS cross_lane_mean,
               stddev_pop(lane_minute.avg_size) OVER (PARTITION BY lane_minute.minute_ts, lane_minute.serial_no) AS cross_lane_sd
        FROM lane_minute
), flags AS (
        SELECT stats.lane_idx,
               stats.serial_no,
               CASE
                   WHEN (stats.cross_lane_sd = (0)::double precision OR stats.avg_size IS NULL) THEN (0)::double precision
                   ELSE ((stats.avg_size - stats.cross_lane_mean) / stats.cross_lane_sd)
               END AS z
        FROM stats
)
SELECT serial_no,
       (lane_idx + 1) AS lane,
       count(*) AS total_min,
       count(*) FILTER (WHERE (abs(z) >= (2)::double precision)) AS out_min,
       round(((100.0 * (count(*) FILTER (WHERE (z >= (2)::double precision)))::numeric) / (count(*))::numeric), 2) AS pct_over,
       round(((100.0 * (count(*) FILTER (WHERE (z <= ('-2'::integer)::double precision)))::numeric) / (count(*))::numeric), 2) AS pct_under
FROM flags
GROUP BY serial_no, lane_idx
ORDER BY serial_no, (lane_idx + 1);

CREATE OR REPLACE VIEW public.v_throughput_daily AS
SELECT day,
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
SELECT minute_ts,
       total_fpm,
       missed_fpm,
       machine_recycle_fpm,
       outlet_recycle_fpm,
       combined_recycle_fpm,
       cupfill_pct,
       tph,
       public.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, public.get_target_throughput()) AS throughput_ratio
FROM public.cagg_throughput_minute;
