CREATE MATERIALIZED VIEW oee.cagg_availability_daily WITH (timescaledb.continuous) AS  SELECT time_bucket('1 day'::interval, minute_ts) AS day,
    serial_no,
    count(*) FILTER (WHERE (state = 2)) AS minutes_run,
    count(*) FILTER (WHERE (state = 1)) AS minutes_idle,
    count(*) FILTER (WHERE (state = 0)) AS minutes_down,
    avg(availability) AS avg_availability
   FROM oee.cagg_availability_minute
  GROUP BY (time_bucket('1 day'::interval, minute_ts)), serial_no; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_availability_daily_batch WITH (timescaledb.continuous) AS  SELECT time_bucket('1 day'::interval, minute_ts) AS day,
    serial_no,
    batch_record_id,
    min(lot) AS lot,
    min(variety) AS variety,
    count(*) FILTER (WHERE (state = 2)) AS minutes_run,
    count(*) FILTER (WHERE (state = 1)) AS minutes_idle,
    count(*) FILTER (WHERE (state = 0)) AS minutes_down,
    avg(availability) AS avg_availability
   FROM oee.cagg_availability_minute_batch
  GROUP BY (time_bucket('1 day'::interval, minute_ts)), serial_no, batch_record_id; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_availability_minute WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    m.serial_no,
    (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric AS avg_rpm,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb) AS total_fpm,
    min(mt.min_rpm) AS min_rpm,
    min(mt.min_total_fpm) AS min_total_fpm,
    oee.availability_state((oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric, oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb), min(mt.min_rpm), min(mt.min_total_fpm)) AS state,
    oee.availability_ratio(oee.availability_state((oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric, oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb), min(mt.min_rpm), min(mt.min_total_fpm))) AS availability
   FROM (metrics m
     JOIN oee.machine_thresholds mt USING (serial_no))
  WHERE (m.metric = ANY (ARRAY['machine_rods_pm'::text, 'machine_total_fpm'::text]))
  GROUP BY (time_bucket('00:01:00'::interval, m.ts)), m.serial_no; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_availability_minute_batch WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    m.serial_no,
    m.batch_record_id,
    min(b.grower_code) AS lot,
    min(b.comments) AS variety,
    (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric AS avg_rpm,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb) AS total_fpm,
    min(mt.min_rpm) AS min_rpm,
    min(mt.min_total_fpm) AS min_total_fpm,
    oee.availability_state((oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric, oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb), min(mt.min_rpm), min(mt.min_total_fpm)) AS state,
    oee.availability_ratio(oee.availability_state((oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric, oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb), min(mt.min_rpm), min(mt.min_total_fpm))) AS availability
   FROM ((metrics m
     JOIN oee.machine_thresholds mt USING (serial_no))
     LEFT JOIN batches b ON ((b.id = m.batch_record_id)))
  WHERE (m.metric = ANY (ARRAY['machine_rods_pm'::text, 'machine_total_fpm'::text]))
  GROUP BY (time_bucket('00:01:00'::interval, m.ts)), m.serial_no, m.batch_record_id; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_grade_daily_batch WITH (timescaledb.continuous) AS  SELECT time_bucket('1 day'::interval, minute_ts) AS day,
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
  GROUP BY (time_bucket('1 day'::interval, minute_ts)), serial_no, batch_record_id; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_grade_minute_batch WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    m.serial_no,
    m.batch_record_id,
    min(b.grower_code) AS lot,
    min(b.comments) AS variety,
    oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 0) AS good_qty,
    oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 1) AS peddler_qty,
    oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 2) AS bad_qty,
    oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 3) AS recycle_qty,
    oee.calc_quality_ratio_qv1(oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 0), oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 1), oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 2), oee.grade_qty((max((m.value_json)::text) FILTER (WHERE (m.metric = 'lanes_grade_fpm'::text)))::jsonb, 3)) AS quality_ratio
   FROM (metrics m
     LEFT JOIN batches b ON ((b.id = m.batch_record_id)))
  WHERE (m.metric = 'lanes_grade_fpm'::text)
  GROUP BY (time_bucket('00:01:00'::interval, m.ts)), m.serial_no, m.batch_record_id; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_throughput_daily_batch WITH (timescaledb.continuous) AS  SELECT time_bucket('1 day'::interval, minute_ts) AS day,
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
  GROUP BY (time_bucket('1 day'::interval, minute_ts)), serial_no, batch_record_id; WITH NO DATA;
CREATE MATERIALIZED VIEW oee.cagg_throughput_minute_batch WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    m.serial_no,
    m.batch_record_id,
    min(b.grower_code) AS lot,
    min(b.comments) AS variety,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb) AS total_fpm,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_missed_fpm'::text)))::jsonb) AS missed_fpm,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)))::jsonb) AS machine_recycle_fpm,
    oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE (m.metric = 'outlets_details'::text)))::jsonb, oee.get_recycle_outlet()) AS outlet_recycle_fpm,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_cupfill'::text)))::jsonb) AS cupfill_pct,
    oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_tph'::text)))::jsonb) AS tph,
    (oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)))::jsonb) + oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE (m.metric = 'outlets_details'::text)))::jsonb, oee.get_recycle_outlet())) AS combined_recycle_fpm
   FROM (metrics m
     LEFT JOIN batches b ON ((b.id = m.batch_record_id)))
  WHERE (m.metric = ANY (ARRAY['machine_total_fpm'::text, 'machine_missed_fpm'::text, 'machine_recycle_fpm'::text, 'outlets_details'::text, 'machine_cupfill'::text, 'machine_tph'::text]))
  GROUP BY (time_bucket('00:01:00'::interval, m.ts)), m.serial_no, m.batch_record_id; WITH NO DATA;
CREATE MATERIALIZED VIEW public.cagg_lane_grade_minute WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    sum(
        CASE
            WHEN (grade_to_cat(g.key) = 0) THEN (g.value)::double precision
            ELSE NULL::double precision
        END) AS good_qty,
    sum(
        CASE
            WHEN (grade_to_cat(g.key) = 1) THEN (g.value)::double precision
            ELSE NULL::double precision
        END) AS peddler_qty,
    sum(
        CASE
            WHEN (grade_to_cat(g.key) = 2) THEN (g.value)::double precision
            ELSE NULL::double precision
        END) AS bad_qty,
    sum(
        CASE
            WHEN (grade_to_cat(g.key) = 3) THEN (g.value)::double precision
            ELSE NULL::double precision
        END) AS recycle_qty,
    calc_quality_ratio_qv1(COALESCE(sum(
        CASE
            WHEN (grade_to_cat(g.key) = 0) THEN (g.value)::double precision
            ELSE NULL::double precision
        END), (0)::double precision), COALESCE(sum(
        CASE
            WHEN (grade_to_cat(g.key) = 1) THEN (g.value)::double precision
            ELSE NULL::double precision
        END), (0)::double precision), COALESCE(sum(
        CASE
            WHEN (grade_to_cat(g.key) = 2) THEN (g.value)::double precision
            ELSE NULL::double precision
        END), (0)::double precision), COALESCE(sum(
        CASE
            WHEN (grade_to_cat(g.key) = 3) THEN (g.value)::double precision
            ELSE NULL::double precision
        END), (0)::double precision)) AS quality_ratio
   FROM ((metrics m
     CROSS JOIN LATERAL jsonb_array_elements(m.value_json) lane(lane_json))
     CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) g(key, value))
  WHERE (m.metric = 'lanes_grade_fpm'::text)
  GROUP BY (time_bucket('00:01:00'::interval, m.ts)); WITH NO DATA;
CREATE MATERIALIZED VIEW public.cagg_lane_size_minute WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    (lane.ord - 1) AS lane_idx,
    sum((v.value)::integer) AS fruit_cnt,
    (sum((((v.value)::integer)::double precision * COALESCE(size_group_value(v.key), (0)::double precision))) / (NULLIF(sum((v.value)::integer), 0))::double precision) AS avg_size
   FROM ((metrics m
     CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane(lane_json, ord))
     CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) v(key, value))
  WHERE (m.metric = 'lanes_size_fpm'::text)
  GROUP BY (time_bucket('00:01:00'::interval, m.ts)), (lane.ord - 1); WITH NO DATA;
CREATE MATERIALIZED VIEW public.cagg_throughput_daily WITH (timescaledb.continuous) AS  SELECT time_bucket('1 day'::interval, minute_ts) AS day,
    avg(total_fpm) AS total_fpm,
    avg(missed_fpm) AS missed_fpm,
    avg(machine_recycle_fpm) AS recycle_fpm,
    avg(outlet_recycle_fpm) AS outlet_recycle_fpm,
    avg(combined_recycle_fpm) AS combined_recycle_fpm,
    avg(cupfill_pct) AS cupfill_pct,
    avg(tph) AS tph
   FROM cagg_throughput_minute
  GROUP BY (time_bucket('1 day'::interval, minute_ts)); WITH NO DATA;
CREATE MATERIALIZED VIEW public.cagg_throughput_minute WITH (timescaledb.continuous) AS  SELECT time_bucket('00:01:00'::interval, ts) AS minute_ts,
    max((value_json)::double precision) FILTER (WHERE (metric = 'machine_total_fpm'::text)) AS total_fpm,
    COALESCE(max((value_json)::double precision) FILTER (WHERE (metric = 'machine_missed_fpm'::text)), (0)::double precision) AS missed_fpm,
    COALESCE(max((value_json)::double precision) FILTER (WHERE (metric = 'machine_recycle_fpm'::text)), (0)::double precision) AS machine_recycle_fpm,
    max((value_json)::double precision) FILTER (WHERE (metric = 'machine_cupfill'::text)) AS cupfill_pct,
    max((value_json)::double precision) FILTER (WHERE (metric = 'machine_tph'::text)) AS tph,
    max(outlet_recycle_fpm(value_json, get_recycle_outlet())) FILTER (WHERE (metric = 'outlets_details'::text)) AS outlet_recycle_fpm,
    (COALESCE(max((value_json)::double precision) FILTER (WHERE (metric = 'machine_recycle_fpm'::text)), (0)::double precision) + COALESCE(max(outlet_recycle_fpm(value_json, get_recycle_outlet())) FILTER (WHERE (metric = 'outlets_details'::text)), (0)::double precision)) AS combined_recycle_fpm
   FROM metrics m
  WHERE (metric = ANY (ARRAY['machine_total_fpm'::text, 'machine_missed_fpm'::text, 'machine_recycle_fpm'::text, 'machine_cupfill'::text, 'machine_tph'::text, 'outlets_details'::text]))
  GROUP BY (time_bucket('00:01:00'::interval, ts)); WITH NO DATA;
