/*******************************************************************************
 * continuous_aggregates.sql
 *
 * Authoritative definition file for all TimescaleDB continuous aggregates,
 * their refresh policies, and custom jobs used by the OEE / Sizer system.
 *
 * Matches production database: sizer_metrics_staging (dumped 2026-02-26).
 *
 * Order:
 *   1. Availability CAGGs  (minute -> daily, non-batch then batch)
 *   2. Throughput CAGGs     (minute -> daily, public then oee/batch)
 *   3. Grade / Quality CAGGs
 *   4. Refresh policies
 *   5. Custom jobs
 ******************************************************************************/

-- ============================================================================
-- 1. AVAILABILITY
-- ============================================================================

-- 1a. oee.cagg_availability_minute  (_direct_view_19)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_availability_minute'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_availability_minute
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
           m.serial_no,
           (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric AS avg_rpm,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb) AS total_fpm,
           min(mt.min_rpm) AS min_rpm,
           min(mt.min_total_fpm) AS min_total_fpm,
           oee.availability_state(
               (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric,
               oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb),
               min(mt.min_rpm),
               min(mt.min_total_fpm)
           ) AS state,
           oee.availability_ratio(
               oee.availability_state(
                   (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric,
                   oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb),
                   min(mt.min_rpm),
                   min(mt.min_total_fpm)
               )
           ) AS availability
    FROM public.metrics m
    JOIN oee.machine_thresholds mt USING (serial_no)
    WHERE m.metric = ANY (ARRAY['machine_rods_pm'::text, 'machine_total_fpm'::text])
    GROUP BY public.time_bucket('00:01:00'::interval, m.ts), m.serial_no
    WITH NO DATA;
  END IF;
END $$;

-- 1b. oee.cagg_availability_minute_batch  (_direct_view_20)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_availability_minute_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_availability_minute_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
           m.serial_no,
           m.batch_record_id,
           min(b.grower_code) AS lot,
           min(b.comments) AS variety,
           (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric AS avg_rpm,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb) AS total_fpm,
           min(mt.min_rpm) AS min_rpm,
           min(mt.min_total_fpm) AS min_total_fpm,
           oee.availability_state(
               (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric,
               oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb),
               min(mt.min_rpm),
               min(mt.min_total_fpm)
           ) AS state,
           oee.availability_ratio(
               oee.availability_state(
                   (oee.avg_int_array((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_rods_pm'::text)))::jsonb))::numeric,
                   oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb),
                   min(mt.min_rpm),
                   min(mt.min_total_fpm)
               )
           ) AS availability
    FROM public.metrics m
    JOIN oee.machine_thresholds mt USING (serial_no)
    LEFT JOIN public.batches b ON (b.id = m.batch_record_id)
    WHERE m.metric = ANY (ARRAY['machine_rods_pm'::text, 'machine_total_fpm'::text])
    GROUP BY public.time_bucket('00:01:00'::interval, m.ts), m.serial_no, m.batch_record_id
    WITH NO DATA;
  END IF;
END $$;

-- 1c. oee.cagg_availability_daily  (_direct_view_21, aggregates from cagg_availability_minute)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_availability_daily'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_availability_daily
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
           serial_no,
           count(*) FILTER (WHERE (state = 2)) AS minutes_run,
           count(*) FILTER (WHERE (state = 1)) AS minutes_idle,
           count(*) FILTER (WHERE (state = 0)) AS minutes_down,
           avg(availability) AS avg_availability
    FROM oee.cagg_availability_minute
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no
    WITH NO DATA;
  END IF;
END $$;

-- 1d. oee.cagg_availability_daily_batch  (_direct_view_22, aggregates from cagg_availability_minute_batch)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_availability_daily_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_availability_daily_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
           serial_no,
           batch_record_id,
           min(lot) AS lot,
           min(variety) AS variety,
           count(*) FILTER (WHERE (state = 2)) AS minutes_run,
           count(*) FILTER (WHERE (state = 1)) AS minutes_idle,
           count(*) FILTER (WHERE (state = 0)) AS minutes_down,
           avg(availability) AS avg_availability
    FROM oee.cagg_availability_minute_batch
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id
    WITH NO DATA;
  END IF;
END $$;

-- ============================================================================
-- 2. THROUGHPUT
-- ============================================================================

-- 2a. public.cagg_throughput_minute  (_direct_view_40)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_throughput_minute'
  ) THEN
    CREATE MATERIALIZED VIEW public.cagg_throughput_minute
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
           m.serial_no,
           max((m.value_json)::double precision) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)) AS total_fpm,
           COALESCE(max((m.value_json)::double precision) FILTER (WHERE (m.metric = 'machine_missed_fpm'::text)), (0)::double precision) AS missed_fpm,
           COALESCE(max((m.value_json)::double precision) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)), (0)::double precision) AS machine_recycle_fpm,
           max((m.value_json)::double precision) FILTER (WHERE (m.metric = 'machine_cupfill'::text)) AS cupfill_pct,
           max((m.value_json)::double precision) FILTER (WHERE (m.metric = 'machine_tph'::text)) AS tph,
           max(public.outlet_recycle_fpm(m.value_json, ms.recycle_outlet)) FILTER (WHERE (m.metric = 'outlets_details'::text)) AS outlet_recycle_fpm,
           (COALESCE(max((m.value_json)::double precision) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)), (0)::double precision)
            + COALESCE(max(public.outlet_recycle_fpm(m.value_json, ms.recycle_outlet)) FILTER (WHERE (m.metric = 'outlets_details'::text)), (0)::double precision)) AS combined_recycle_fpm,
           (((ms.target_machine_speed * (ms.lane_count)::double precision) * ms.target_percentage) / (100.0)::double precision) AS target_throughput
    FROM public.metrics m
    LEFT JOIN public.machine_settings ms ON (ms.serial_no = m.serial_no)
    WHERE m.metric = ANY (ARRAY['machine_total_fpm'::text, 'machine_missed_fpm'::text, 'machine_recycle_fpm'::text, 'machine_cupfill'::text, 'machine_tph'::text, 'outlets_details'::text])
    GROUP BY public.time_bucket('00:01:00'::interval, m.ts), m.serial_no, ms.target_machine_speed, ms.lane_count, ms.target_percentage, ms.recycle_outlet
    WITH NO DATA;
  END IF;
END $$;

-- 2b. public.cagg_throughput_daily  (_direct_view_41, aggregates from cagg_throughput_minute)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_throughput_daily'
  ) THEN
    CREATE MATERIALIZED VIEW public.cagg_throughput_daily
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
           serial_no,
           avg(total_fpm) AS total_fpm,
           avg(missed_fpm) AS missed_fpm,
           avg(machine_recycle_fpm) AS recycle_fpm,
           avg(outlet_recycle_fpm) AS outlet_recycle_fpm,
           avg(combined_recycle_fpm) AS combined_recycle_fpm,
           avg(cupfill_pct) AS cupfill_pct,
           avg(tph) AS tph
    FROM public.cagg_throughput_minute
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no
    WITH NO DATA;
  END IF;
END $$;

-- 2c. oee.cagg_throughput_minute_batch  (_direct_view_47)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_throughput_minute_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_throughput_minute_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
           m.serial_no,
           m.batch_record_id,
           min(b.grower_code) AS lot,
           min(b.comments) AS variety,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb) AS total_fpm,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_missed_fpm'::text)))::jsonb) AS missed_fpm,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)))::jsonb) AS machine_recycle_fpm,
           oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE (m.metric = 'outlets_details'::text)))::jsonb, ms.recycle_outlet) AS outlet_recycle_fpm,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_cupfill'::text)))::jsonb) AS cupfill_pct,
           oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_tph'::text)))::jsonb) AS tph,
           (oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)))::jsonb)
            + oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE (m.metric = 'outlets_details'::text)))::jsonb, ms.recycle_outlet)) AS combined_recycle_fpm,
           ((((ms.target_machine_speed * (ms.lane_count)::double precision) * ms.target_percentage) / (100.0)::double precision))::numeric AS target_throughput,
           oee.calc_perf_ratio(
               oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_total_fpm'::text)))::jsonb),
               oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_missed_fpm'::text)))::jsonb),
               (oee.num((max((m.value_json)::text) FILTER (WHERE (m.metric = 'machine_recycle_fpm'::text)))::jsonb)
                + oee.outlet_recycle_fpm((max((m.value_json)::text) FILTER (WHERE (m.metric = 'outlets_details'::text)))::jsonb, ms.recycle_outlet)),
               ((((ms.target_machine_speed * (ms.lane_count)::double precision) * ms.target_percentage) / (100.0)::double precision))::numeric
           ) AS throughput_ratio
    FROM public.metrics m
    LEFT JOIN public.batches b ON (b.id = m.batch_record_id)
    LEFT JOIN public.machine_settings ms ON (ms.serial_no = m.serial_no)
    WHERE m.metric = ANY (ARRAY['machine_total_fpm'::text, 'machine_missed_fpm'::text, 'machine_recycle_fpm'::text, 'outlets_details'::text, 'machine_cupfill'::text, 'machine_tph'::text])
    GROUP BY public.time_bucket('00:01:00'::interval, m.ts), m.serial_no, m.batch_record_id, ms.target_machine_speed, ms.lane_count, ms.target_percentage, ms.recycle_outlet
    WITH NO DATA;
  END IF;
END $$;

-- 2d. oee.cagg_throughput_daily_batch  (_direct_view_48, aggregates from cagg_throughput_minute_batch)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_throughput_daily_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_throughput_daily_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
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
           avg(tph) AS tph,
           avg(target_throughput) AS target_throughput,
           oee.calc_perf_ratio(avg(total_fpm), avg(missed_fpm), avg(combined_recycle_fpm), avg(target_throughput)) AS throughput_ratio
    FROM oee.cagg_throughput_minute_batch
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id
    WITH NO DATA;
  END IF;
END $$;

-- ============================================================================
-- 3. GRADE / QUALITY
-- ============================================================================

-- 3a. oee.cagg_lane_grade_qty_minute_batch  (_direct_view_50)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_lane_grade_qty_minute_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_lane_grade_qty_minute_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, minute_ts) AS minute_ts,
           serial_no,
           batch_record_id,
           lane_no,
           grade_key,
           grade_name,
           sum(qty) AS qty
    FROM oee.lane_grade_minute
    GROUP BY public.time_bucket('00:01:00'::interval, minute_ts), serial_no, batch_record_id, lane_no, grade_key, grade_name
    WITH NO DATA;
  END IF;
END $$;

-- 3b. oee.cagg_lane_grade_qty_daily_batch  (_direct_view_51)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_lane_grade_qty_daily_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_lane_grade_qty_daily_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
           serial_no,
           batch_record_id,
           lane_no,
           grade_key,
           grade_name,
           sum(qty) AS qty
    FROM oee.lane_grade_minute
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id, lane_no, grade_key, grade_name
    WITH NO DATA;
  END IF;
END $$;

-- 3c. oee.cagg_grade_qty_minute_batch  (_direct_view_52)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_grade_qty_minute_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_grade_qty_minute_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, minute_ts) AS minute_ts,
           serial_no,
           batch_record_id,
           grade_key,
           grade_name,
           sum(qty) AS qty
    FROM oee.lane_grade_minute
    GROUP BY public.time_bucket('00:01:00'::interval, minute_ts), serial_no, batch_record_id, grade_key, grade_name
    WITH NO DATA;
  END IF;
END $$;

-- 3d. oee.cagg_grade_qty_daily_batch  (_direct_view_53)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_grade_qty_daily_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_grade_qty_daily_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
           serial_no,
           batch_record_id,
           grade_key,
           grade_name,
           sum(qty) AS qty
    FROM oee.lane_grade_minute
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id, grade_key, grade_name
    WITH NO DATA;
  END IF;
END $$;

-- 3e. oee.cagg_quality_cat_minute_batch  (_direct_view_54)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_quality_cat_minute_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_quality_cat_minute_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, minute_ts) AS minute_ts,
           serial_no,
           batch_record_id,
           oee.grade_to_cat(serial_no, grade_key) AS cat,
           sum(qty) AS qty
    FROM oee.lane_grade_minute
    WHERE batch_record_id IS NOT NULL
    GROUP BY public.time_bucket('00:01:00'::interval, minute_ts), serial_no, batch_record_id, oee.grade_to_cat(serial_no, grade_key)
    WITH NO DATA;
  END IF;
END $$;

-- 3f. oee.cagg_quality_cat_daily_batch  (_direct_view_55)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_quality_cat_daily_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_quality_cat_daily_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
           serial_no,
           batch_record_id,
           oee.grade_to_cat(serial_no, grade_key) AS cat,
           sum(qty) AS qty
    FROM oee.lane_grade_minute
    WHERE batch_record_id IS NOT NULL
    GROUP BY public.time_bucket('1 day'::interval, minute_ts), serial_no, batch_record_id, oee.grade_to_cat(serial_no, grade_key)
    WITH NO DATA;
  END IF;
END $$;

-- 3g. oee.cagg_grade_minute_batch  (_direct_view_63)
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM timescaledb_information.continuous_aggregates
    WHERE materialization_hypertable_schema = '_timescaledb_internal'
      AND view_name = 'cagg_grade_minute_batch'
  ) THEN
    CREATE MATERIALIZED VIEW oee.cagg_grade_minute_batch
    WITH (timescaledb.continuous) AS
    SELECT public.time_bucket('00:01:00'::interval, minute_ts) AS minute_ts,
           serial_no,
           batch_record_id,
           grade_key,
           oee.grade_to_cat(serial_no, grade_key) AS cat,
           sum(qty) AS sum_qty,
           count(*) AS sample_ct
    FROM oee.lane_grade_minute
    GROUP BY public.time_bucket('00:01:00'::interval, minute_ts), serial_no, batch_record_id, grade_key, oee.grade_to_cat(serial_no, grade_key)
    WITH NO DATA;
  END IF;
END $$;

-- ============================================================================
-- 4. REFRESH POLICIES
-- ============================================================================

-- Availability (minute)
SELECT add_continuous_aggregate_policy('oee.cagg_availability_minute',
    start_offset => '02:00:00'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_availability_minute_batch',
    start_offset => '02:00:00'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

-- Availability (daily)
SELECT add_continuous_aggregate_policy('oee.cagg_availability_daily',
    start_offset => '14 days'::interval,
    end_offset   => '01:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_availability_daily_batch',
    start_offset => '14 days'::interval,
    end_offset   => '01:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

-- Throughput (minute)
SELECT add_continuous_aggregate_policy('public.cagg_throughput_minute',
    start_offset => '00:02:00'::interval,
    end_offset   => '00:00:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_throughput_minute_batch',
    start_offset => '02:00:00'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

-- Throughput (daily)
SELECT add_continuous_aggregate_policy('public.cagg_throughput_daily',
    start_offset => '2 days'::interval,
    end_offset   => '00:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_throughput_daily_batch',
    start_offset => '14 days'::interval,
    end_offset   => '01:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

-- Grade / Lane grade (minute)
SELECT add_continuous_aggregate_policy('oee.cagg_lane_grade_qty_minute_batch',
    start_offset => '02:00:00'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_grade_qty_minute_batch',
    start_offset => '02:00:00'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_grade_minute_batch',
    start_offset => '02:00:00'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

-- Grade / Lane grade (daily)
SELECT add_continuous_aggregate_policy('oee.cagg_lane_grade_qty_daily_batch',
    start_offset => '14 days'::interval,
    end_offset   => '01:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_grade_qty_daily_batch',
    start_offset => '14 days'::interval,
    end_offset   => '01:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

-- Quality category
SELECT add_continuous_aggregate_policy('oee.cagg_quality_cat_minute_batch',
    start_offset => '14 days'::interval,
    end_offset   => '00:01:00'::interval,
    schedule_interval => '00:01:00'::interval,
    if_not_exists => true);

SELECT add_continuous_aggregate_policy('oee.cagg_quality_cat_daily_batch',
    start_offset => '90 days'::interval,
    end_offset   => '01:00:00'::interval,
    schedule_interval => '01:00:00'::interval,
    if_not_exists => true);

-- ============================================================================
-- 5. CUSTOM JOBS
-- ============================================================================

SELECT add_job('oee.refresh_lane_grade_minute',
    '00:01:00'::interval,
    config => '{"end_offset": "00:01:00", "start_offset": "02:00:00"}'::jsonb);
