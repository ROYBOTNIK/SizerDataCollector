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
       oee.calc_quality_ratio_qv1(
           COALESCE(sum(CASE WHEN (c.cat = 0) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 1) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 2) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 3) THEN (c.sum_qty / (NULLIF(c.sample_ct, 0))::double precision) ELSE (0)::double precision END), (0)::double precision)
       ) AS quality_ratio,
       min(b.grower_code) AS lot,
       min(b.comments) AS variety
FROM oee.cagg_grade_minute_batch c
LEFT JOIN public.batches b ON (b.id = c.batch_record_id)
GROUP BY c.minute_ts, c.serial_no, c.batch_record_id;

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
JOIN oee.cagg_throughput_minute_batch t
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
JOIN oee.cagg_throughput_minute_batch t
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
       oee.calc_quality_ratio_qv1(
           sum(COALESCE(good_qty, (0)::double precision)),
           sum(COALESCE(peddler_qty, (0)::double precision)),
           sum(COALESCE(bad_qty, (0)::double precision)),
           sum(COALESCE(recycle_qty, (0)::double precision))
       ) AS quality_ratio,
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
       oee.calc_quality_ratio_qv1(
           COALESCE(sum(CASE WHEN (c.cat = 0) THEN c.qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 1) THEN c.qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 2) THEN c.qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (c.cat = 3) THEN c.qty ELSE (0)::double precision END), (0)::double precision)
       ) AS quality_ratio,
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
       oee.calc_quality_ratio_qv1(
           COALESCE(sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END), (0)::double precision)
       ) AS quality_ratio,
       serial_no
FROM oee.cagg_quality_cat_daily_batch c
GROUP BY (day)::date, serial_no;

CREATE OR REPLACE VIEW oee.v_quality_minute_components AS
SELECT minute_ts AS ts,
       sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END) AS good_qty,
       sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END) AS peddler_qty,
       sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END) AS bad_qty,
       sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END) AS recycle_qty,
       oee.calc_quality_ratio_qv1(
           COALESCE(sum(CASE WHEN (cat = 0) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 1) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 2) THEN qty ELSE (0)::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 3) THEN qty ELSE (0)::double precision END), (0)::double precision)
       ) AS quality_ratio,
       serial_no
FROM oee.cagg_quality_cat_minute_batch c
GROUP BY minute_ts, serial_no;

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
       throughput_ratio
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
       throughput_ratio
FROM oee.cagg_throughput_minute_batch;

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
       oee.calc_quality_ratio_qv1(
           COALESCE(sum(CASE WHEN (cat = 0) THEN qty ELSE NULL::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 1) THEN qty ELSE NULL::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 2) THEN qty ELSE NULL::double precision END), (0)::double precision),
           COALESCE(sum(CASE WHEN (cat = 3) THEN qty ELSE NULL::double precision END), (0)::double precision)
       ) AS quality_ratio,
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
       oee.calc_quality_ratio_qv1(sum(mv.good_qty), sum(mv.peddler_qty), sum(mv.bad_qty), sum(mv.recycle_qty)) AS quality_ratio,
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
       oee.calc_quality_ratio_qv1(sum(good_qty), sum(peddler_qty), sum(bad_qty), sum(recycle_qty)) AS quality_ratio,
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
       oee.calc_perf_ratio(t.total_fpm, t.missed_fpm, (t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, (0)::double precision)), (oee.get_target_throughput(t.serial_no))::double precision) AS throughput_ratio,
       t.serial_no
FROM t
LEFT JOIN o ON (o.day = t.day AND o.serial_no = t.serial_no);

CREATE OR REPLACE VIEW public.lane_size_health_season AS
WITH unpack AS (
        SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
               (lane.ord - 1) AS lane_idx,
               v.key AS label,
               (v.value)::integer AS fruit_cnt
        FROM public.metrics m
        CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane(lane_json, ord)
        CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) v(key, value)
        WHERE m.metric = 'lanes_size_fpm'::text
          AND m.serial_no = '140578'::text
), lane_minute AS (
        SELECT unpack.minute_ts,
               unpack.lane_idx,
               (sum(((unpack.fruit_cnt)::double precision * public.size_group_value(unpack.label))) / (NULLIF(sum(unpack.fruit_cnt), 0))::double precision) AS avg_size
        FROM unpack
        GROUP BY unpack.minute_ts, unpack.lane_idx
), stats AS (
        SELECT lane_minute.minute_ts,
               lane_minute.lane_idx,
               lane_minute.avg_size,
               avg(lane_minute.avg_size) OVER (PARTITION BY lane_minute.minute_ts) AS "æ",
               stddev_pop(lane_minute.avg_size) OVER (PARTITION BY lane_minute.minute_ts) AS "å"
        FROM lane_minute
), flags AS (
        SELECT stats.lane_idx,
               CASE
                   WHEN (stats."å" = (0)::double precision OR stats.avg_size IS NULL) THEN (0)::double precision
                   ELSE ((stats.avg_size - stats."æ") / stats."å")
               END AS z
        FROM stats
)
SELECT (lane_idx + 1) AS lane,
       count(*) AS total_min,
       count(*) FILTER (WHERE (abs(z) >= (2)::double precision)) AS out_min,
       round(((100.0 * (count(*) FILTER (WHERE (z >= (2)::double precision)))::numeric) / (count(*))::numeric), 2) AS pct_over,
       round(((100.0 * (count(*) FILTER (WHERE (z <= ('-2'::integer)::double precision)))::numeric) / (count(*))::numeric), 2) AS pct_under
FROM flags
GROUP BY lane_idx
ORDER BY (lane_idx + 1);

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
