-- ============================================================================
-- OPTI-FRESH Sizer Data Collector: Function Definitions
-- This file is the authoritative source for all PostgreSQL functions.
-- Edit this file and run 'db apply-functions' to hot-patch the database.
-- Resolution: shared data dir overrides this default.
--
-- All functions use CREATE OR REPLACE and are therefore idempotent.
-- ============================================================================

CREATE SCHEMA IF NOT EXISTS oee;

-- ============================================================================
-- oee schema functions
-- ============================================================================


-- oee.availability_ratio(state) — V002
CREATE OR REPLACE FUNCTION oee.availability_ratio(state smallint) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE state WHEN 2 THEN 1 WHEN 1 THEN 0.5 ELSE 0 END$$;


-- oee.availability_state(avg_rpm, total_fpm, min_rpm, min_total_fpm) — V002
CREATE OR REPLACE FUNCTION oee.availability_state(avg_rpm numeric, total_fpm numeric, min_rpm numeric, min_total_fpm numeric) RETURNS smallint
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE
                WHEN (avg_rpm IS NULL OR avg_rpm = 0)
                     THEN CASE WHEN total_fpm >= min_total_fpm THEN 2 ELSE 0 END
                WHEN avg_rpm <  min_rpm                 THEN 0
                WHEN total_fpm < min_total_fpm          THEN 1
                ELSE 2
              END$$;


-- oee.avg_int_array(j) — V002
CREATE OR REPLACE FUNCTION oee.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE
                WHEN j IS NULL OR jsonb_typeof(j) <> 'array' OR jsonb_array_length(j)=0
                THEN 0
                ELSE (SELECT avg(value::numeric)
                      FROM   jsonb_array_elements_text(j) AS t(value))
              END$$;


-- oee.calc_perf_ratio(total_fpm, missed_fpm, recycle_fpm, target_fpm) — V002 (numeric)
CREATE OR REPLACE FUNCTION oee.calc_perf_ratio(total_fpm numeric, missed_fpm numeric, recycle_fpm numeric, target_fpm numeric) RETURNS numeric
    LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE
AS $$
DECLARE
    effective numeric := GREATEST(0, total_fpm - missed_fpm - recycle_fpm);
    raw_ratio numeric;
    ratio     numeric;
BEGIN
    IF target_fpm <= 0 OR effective < 3 THEN
        RETURN 0;
    END IF;

    raw_ratio := effective / target_fpm;

    IF raw_ratio <= 0.5  THEN ratio := raw_ratio;
    ELSIF raw_ratio <= 1 THEN ratio := 0.5 + (raw_ratio - 0.5);
    ELSE                       ratio := 1 - (0.2 / ((raw_ratio - 1) + 0.2));
    END IF;

    RETURN LEAST(1, GREATEST(0, ratio));
END;
$$;


-- oee.calc_perf_ratio (serial-aware) — reads from oee.perf_params, falls back to defaults
CREATE OR REPLACE FUNCTION oee.calc_perf_ratio(p_serial_no text, total_fpm numeric, missed_fpm numeric, recycle_fpm numeric, target_fpm numeric) RETURNS numeric
    LANGUAGE plpgsql STABLE
AS $$
DECLARE
    v_min_eff   numeric;
    v_low_thr   numeric;
    v_cap_asym  numeric;
    effective   numeric;
    raw_ratio   numeric;
    ratio       numeric;
BEGIN
    SELECT min_effective_fpm, low_ratio_threshold, cap_asymptote
      INTO v_min_eff, v_low_thr, v_cap_asym
      FROM oee.perf_params
     WHERE serial_no = p_serial_no;

    IF v_min_eff IS NULL THEN
        v_min_eff  := 3;
        v_low_thr  := 0.5;
        v_cap_asym := 0.2;
    END IF;

    effective := GREATEST(0, total_fpm - missed_fpm - recycle_fpm);

    IF target_fpm <= 0 OR effective < v_min_eff THEN
        RETURN 0;
    END IF;

    raw_ratio := effective / target_fpm;

    IF raw_ratio <= v_low_thr THEN ratio := raw_ratio;
    ELSIF raw_ratio <= 1      THEN ratio := v_low_thr + (raw_ratio - v_low_thr);
    ELSE                           ratio := 1 - (v_cap_asym / ((raw_ratio - 1) + v_cap_asym));
    END IF;

    RETURN LEAST(1, GREATEST(0, ratio));
END;
$$;


-- oee.calc_quality_ratio_qv1(good_qty, peddler_qty, bad_qty, recycle_qty) — V002 (numeric)
CREATE OR REPLACE FUNCTION oee.calc_quality_ratio_qv1(good_qty numeric, peddler_qty numeric, bad_qty numeric, recycle_qty numeric) RETURNS numeric
    LANGUAGE plpgsql IMMUTABLE PARALLEL SAFE
AS $$
DECLARE
    tgt_good     constant numeric := 0.75;
    tgt_peddler  constant numeric := 0.15;
    tgt_bad      constant numeric := 0.05;
    tgt_recycle  constant numeric := 0.05;

    w_good       constant numeric := 0.40;
    w_peddler    constant numeric := 0.20;
    w_bad        constant numeric := 0.20;
    w_recycle    constant numeric := 0.20;

    sig_k        constant numeric := 4.0;
    clamp_min    constant numeric := -2.0;
    clamp_max    constant numeric :=  2.0;

    total        numeric;
    pct_good     numeric;
    pct_peddler  numeric;
    pct_bad      numeric;
    pct_recycle  numeric;

    raw_good     numeric;
    raw_peddler  numeric;
    raw_bad      numeric;
    raw_recycle  numeric;

    part_good    numeric;
    part_peddler numeric;
    part_bad     numeric;
    part_recycle numeric;
BEGIN
    total := good_qty + peddler_qty + bad_qty + recycle_qty;
    IF total <= 0 THEN
        RETURN 0;
    END IF;

    pct_good     := good_qty     / total;
    pct_peddler  := peddler_qty  / total;
    pct_bad      := bad_qty      / total;
    pct_recycle  := recycle_qty  / total;

    raw_good    := GREATEST(clamp_min, LEAST(clamp_max, 1 + (pct_good    - tgt_good)    / tgt_good));
    raw_peddler := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_peddler - tgt_peddler) / tgt_peddler));
    raw_bad     := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_bad     - tgt_bad)     / tgt_bad));
    raw_recycle := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_recycle - tgt_recycle) / tgt_recycle));

    part_good    := 1 / (1 + exp(-sig_k * (raw_good    - 1)));
    part_peddler := 1 / (1 + exp(-sig_k * (raw_peddler - 1)));
    part_bad     := 1 / (1 + exp(-sig_k * (raw_bad     - 1)));
    part_recycle := 1 / (1 + exp(-sig_k * (raw_recycle - 1)));

    RETURN part_good    * w_good
         + part_peddler * w_peddler
         + part_bad     * w_bad
         + part_recycle * w_recycle;
END;
$$;


-- oee.calc_quality_ratio_qv1 (serial-aware) — reads from oee.quality_params, falls back to defaults
CREATE OR REPLACE FUNCTION oee.calc_quality_ratio_qv1(p_serial_no text, good_qty numeric, peddler_qty numeric, bad_qty numeric, recycle_qty numeric) RETURNS numeric
    LANGUAGE plpgsql STABLE
AS $$
DECLARE
    v_tgt_good     numeric;
    v_tgt_peddler  numeric;
    v_tgt_bad      numeric;
    v_tgt_recycle  numeric;
    v_w_good       numeric;
    v_w_peddler    numeric;
    v_w_bad        numeric;
    v_w_recycle    numeric;
    v_sig_k        numeric;

    total          numeric;
    pct_good       numeric;
    pct_peddler    numeric;
    pct_bad        numeric;
    pct_recycle    numeric;

    raw_good       numeric;
    raw_peddler    numeric;
    raw_bad        numeric;
    raw_recycle    numeric;

    clamp_min      constant numeric := -2.0;
    clamp_max      constant numeric :=  2.0;

    part_good      numeric;
    part_peddler   numeric;
    part_bad       numeric;
    part_recycle   numeric;
BEGIN
    SELECT tgt_good, tgt_peddler, tgt_bad, tgt_recycle,
           w_good, w_peddler, w_bad, w_recycle, sig_k
      INTO v_tgt_good, v_tgt_peddler, v_tgt_bad, v_tgt_recycle,
           v_w_good, v_w_peddler, v_w_bad, v_w_recycle, v_sig_k
      FROM oee.quality_params
     WHERE serial_no = p_serial_no;

    IF v_tgt_good IS NULL THEN
        v_tgt_good    := 0.75;
        v_tgt_peddler := 0.15;
        v_tgt_bad     := 0.05;
        v_tgt_recycle := 0.05;
        v_w_good      := 0.40;
        v_w_peddler   := 0.20;
        v_w_bad       := 0.20;
        v_w_recycle   := 0.20;
        v_sig_k       := 4.0;
    END IF;

    total := good_qty + peddler_qty + bad_qty + recycle_qty;
    IF total <= 0 THEN
        RETURN 0;
    END IF;

    pct_good    := good_qty     / total;
    pct_peddler := peddler_qty  / total;
    pct_bad     := bad_qty      / total;
    pct_recycle := recycle_qty  / total;

    raw_good    := GREATEST(clamp_min, LEAST(clamp_max, 1 + (pct_good    - v_tgt_good)    / v_tgt_good));
    raw_peddler := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_peddler - v_tgt_peddler) / v_tgt_peddler));
    raw_bad     := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_bad     - v_tgt_bad)     / v_tgt_bad));
    raw_recycle := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_recycle - v_tgt_recycle)  / v_tgt_recycle));

    part_good    := 1 / (1 + exp(-v_sig_k * (raw_good    - 1)));
    part_peddler := 1 / (1 + exp(-v_sig_k * (raw_peddler - 1)));
    part_bad     := 1 / (1 + exp(-v_sig_k * (raw_bad     - 1)));
    part_recycle := 1 / (1 + exp(-v_sig_k * (raw_recycle - 1)));

    RETURN part_good    * v_w_good
         + part_peddler * v_w_peddler
         + part_bad     * v_w_bad
         + part_recycle * v_w_recycle;
END;
$$;


-- oee.classify_oee_value(p_machine_serial_no, p_oee_value) — V002
CREATE OR REPLACE FUNCTION oee.classify_oee_value(p_machine_serial_no text, p_oee_value numeric) RETURNS text
    LANGUAGE plpgsql IMMUTABLE
AS $$
DECLARE
    v_band_name TEXT;
BEGIN
    SELECT band_name INTO v_band_name
    FROM oee.band_definitions
    WHERE machine_serial_no = p_machine_serial_no
      AND is_active = TRUE
      AND p_oee_value >= lower_bound
      AND p_oee_value < upper_bound
    LIMIT 1;

    IF v_band_name IS NULL THEN
        CASE
            WHEN p_oee_value >= 0.85 THEN v_band_name := 'Excellent';
            WHEN p_oee_value >= 0.70 THEN v_band_name := 'Good';
            WHEN p_oee_value >= 0.55 THEN v_band_name := 'Average';
            WHEN p_oee_value >= 0.40 THEN v_band_name := 'Below Average';
            ELSE v_band_name := 'Poor';
        END CASE;
    END IF;

    RETURN v_band_name;
END;
$$;


-- oee.get_lane_count(p_serial_no) — returns lane count from machine_settings
CREATE OR REPLACE FUNCTION oee.get_lane_count(p_serial_no text) RETURNS integer
    LANGUAGE sql STABLE
AS $$
  SELECT ms.lane_count
  FROM public.machine_settings ms
  WHERE ms.serial_no = p_serial_no
  ORDER BY ms.id DESC
  LIMIT 1
$$;


-- oee.num(j, fallback) — V002
CREATE OR REPLACE FUNCTION oee.num(j jsonb, fallback numeric DEFAULT 0) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE jsonb_typeof(j)
                WHEN 'number' THEN j::numeric
                WHEN 'string' THEN (NULLIF(j::text,'"')::text)::numeric
                ELSE fallback
              END$$;


-- oee.outlet_recycle_fpm(details, outlet_id) — V002
CREATE OR REPLACE FUNCTION oee.outlet_recycle_fpm(details jsonb, outlet_id integer) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$
SELECT COALESCE((
         SELECT (elem ->> 'DeliveredFruitPerMinute')::numeric
         FROM   jsonb_array_elements(details) elem
         WHERE  (elem ->> 'Id')::int = outlet_id
         LIMIT  1), 0);
$$;


-- oee.get_recycle_outlet(p_serial_no) — V010 (serial-aware)
CREATE OR REPLACE FUNCTION oee.get_recycle_outlet(p_serial_no text) RETURNS integer
    LANGUAGE sql STABLE
AS $$
SELECT recycle_outlet
FROM public.machine_settings
WHERE serial_no = p_serial_no
ORDER BY id
LIMIT 1;
$$;


-- oee.get_target_throughput(p_serial_no) — V010 (serial-aware)
CREATE OR REPLACE FUNCTION oee.get_target_throughput(p_serial_no text) RETURNS numeric
    LANGUAGE sql STABLE
AS $$
SELECT target_machine_speed * lane_count * target_percentage / 100.0
FROM public.machine_settings
WHERE serial_no = p_serial_no
ORDER BY id
LIMIT 1;
$$;


-- oee.get_recycle_outlet() — V010 (backward-compat wrapper)
CREATE OR REPLACE FUNCTION oee.get_recycle_outlet() RETURNS integer
    LANGUAGE sql STABLE
AS $$ SELECT oee.get_recycle_outlet(NULL); $$;


-- oee.get_target_throughput() — V010 (backward-compat wrapper)
CREATE OR REPLACE FUNCTION oee.get_target_throughput() RETURNS numeric
    LANGUAGE sql STABLE
AS $$ SELECT oee.get_target_throughput(NULL); $$;


-- oee.grade_to_cat(p_serial_no, p_grade) — V016 FINAL (with suffix overrides)
CREATE OR REPLACE FUNCTION oee.grade_to_cat(p_serial_no text, p_grade text)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
    v_grade text := p_grade;
    v_base  text := p_grade;
    v_cat   integer;
BEGIN
    IF v_grade IS NULL THEN
        RETURN NULL;
    END IF;

    -- 1) Strip lane prefix if first token is numeric
    IF v_grade ~ '^[0-9]+\s+' THEN
        v_base := regexp_replace(v_grade, '^[0-9]+\s+', '');
    ELSE
        v_base := v_grade;
    END IF;

    -- 2) Exact override on full key
    SELECT desired_cat
      INTO v_cat
      FROM oee.grade_map
     WHERE serial_no = p_serial_no
       AND grade_key = v_grade
       AND is_active = TRUE
     LIMIT 1;
    IF v_cat IS NOT NULL THEN
        RETURN v_cat;
    END IF;

    -- 3) Exact override on stripped base (if different)
    IF v_base <> v_grade THEN
        SELECT desired_cat
          INTO v_cat
          FROM oee.grade_map
         WHERE serial_no = p_serial_no
           AND grade_key = v_base
           AND is_active = TRUE
         LIMIT 1;
        IF v_cat IS NOT NULL THEN
            RETURN v_cat;
        END IF;
    END IF;

    -- 4) Suffix override: allow short keys like "_EXP DARK" to match end of v_base
    SELECT desired_cat
      INTO v_cat
      FROM oee.grade_map
     WHERE serial_no = p_serial_no
       AND is_active = TRUE
       AND v_base ILIKE '%' || grade_key
     ORDER BY length(grade_key) DESC
     LIMIT 1;
    IF v_cat IS NOT NULL THEN
        RETURN v_cat;
    END IF;

    -- 5) Pattern fallback
    RETURN CASE
        WHEN v_base ILIKE '%\_RCY%'      THEN 3
        WHEN v_base ILIKE '%\_RECYCLE%'  THEN 3
        WHEN v_base ILIKE '%\_GATE%'     THEN 1
        WHEN v_base ILIKE '%\_REJ%'      THEN 1
        WHEN v_base ILIKE '%\_TEST%'     THEN 1
        WHEN v_base ILIKE '%\_D/S%'      THEN 1
        WHEN v_base ILIKE '%\_PEDDLER%'  THEN 1
        WHEN v_base ILIKE '%\_EXP%'      THEN 0
        WHEN v_base ILIKE '%\_DOM%'      THEN 0
        WHEN v_base ILIKE '%\_CULL%'     THEN 2
        WHEN v_base ILIKE '%\_GREEN%'    THEN 1
        WHEN v_base ILIKE '%\_NAF%'      THEN 3
        ELSE NULL
    END;
END;
$$;


-- oee.grade_to_cat(p_grade) — V016 wrapper
CREATE OR REPLACE FUNCTION oee.grade_to_cat(p_grade text)
RETURNS integer
LANGUAGE sql
IMMUTABLE
AS $$ SELECT oee.grade_to_cat(NULL, p_grade); $$;


-- oee.grade_qty(grade_json, desired_cat) — V002 (single-arg, uses oee.grade_to_cat(key))
DROP FUNCTION IF EXISTS oee.grade_qty(jsonb, integer);

CREATE OR REPLACE FUNCTION oee.grade_qty(grade_json jsonb, desired_cat integer)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
  total integer := 0;
  kv record;
  v_int integer;
BEGIN
  IF grade_json IS NULL THEN
    RETURN 0;
  END IF;

  FOR kv IN SELECT key, value FROM jsonb_each_text(grade_json)
  LOOP
    v_int := COALESCE(NULLIF(kv.value, '')::integer, 0);

    IF oee.grade_to_cat(kv.key) = desired_cat THEN
      total := total + v_int;
    END IF;
  END LOOP;

  RETURN total;
END;
$$;


-- oee.grade_qty(grade_json, desired_cat) — text overload (production)
CREATE OR REPLACE FUNCTION oee.grade_qty(grade_json jsonb, desired_cat text)
RETURNS integer
LANGUAGE plpgsql
IMMUTABLE
AS $$
DECLARE
  total integer := 0;
  kv record;
  v_int integer;
BEGIN
  IF grade_json IS NULL THEN
    RETURN 0;
  END IF;

  FOR kv IN SELECT key, value FROM jsonb_each_text(grade_json)
  LOOP
    v_int := COALESCE(NULLIF(kv.value, '')::integer, 0);

    IF oee.grade_to_cat(kv.key) = desired_cat THEN
      total := total + v_int;
    END IF;
  END LOOP;

  RETURN total;
END;
$$;


-- oee.grade_qty(p_serial_no, j, desired_cat) — V013 (serial-aware)
CREATE OR REPLACE FUNCTION oee.grade_qty(p_serial_no text, j jsonb, desired_cat integer) RETURNS numeric
    LANGUAGE sql STABLE
AS $$
SELECT COALESCE(
         SUM((kv.value)::numeric), 0)
FROM   jsonb_array_elements(j)               AS lane(lj)
       CROSS JOIN LATERAL jsonb_each_text(lj) AS kv(key,value)
WHERE  oee.grade_to_cat(p_serial_no, kv.key) = desired_cat;
$$;


-- oee.ingest_lane_grade_events(p_to) — ETL function for lane grade events
CREATE OR REPLACE FUNCTION oee.ingest_lane_grade_events(p_to timestamp with time zone DEFAULT now()) RETURNS integer
    LANGUAGE plpgsql
AS $$
DECLARE
  v_from timestamptz;
  v_ins  integer := 0;
BEGIN
  SELECT last_ts INTO v_from
  FROM oee.etl_watermarks
  WHERE key = 'lanes_grade_fpm'
  FOR UPDATE;

  WITH src AS (
    SELECT
      m.ts,
      m.serial_no,
      m.batch_record_id,
      m.value_json
    FROM public.metrics m
    WHERE m.metric = 'lanes_grade_fpm'
      AND m.ts > v_from
      AND m.ts <= p_to
      AND jsonb_typeof(m.value_json) = 'array'
  ),
  sample AS (
    SELECT
      s.ts,
      s.serial_no,
      s.batch_record_id,
      lane.ordinality - 1 AS lane_no,
      lane.lane_json
    FROM src s
    CROSS JOIN LATERAL jsonb_array_elements(s.value_json) WITH ORDINALITY AS lane(lane_json, ordinality)
    WHERE lane.lane_json IS NOT NULL
      AND jsonb_typeof(lane.lane_json) = 'object'
      AND (lane.ordinality - 1) < oee.get_lane_count(s.serial_no)
  ),
  exploded AS (
    SELECT
      sample.ts,
      sample.serial_no,
      sample.batch_record_id,
      sample.lane_no,
      kv.key AS grade_key,
      NULLIF(kv.value,'')::double precision AS qty
    FROM sample
    CROSS JOIN LATERAL jsonb_each_text(sample.lane_json) AS kv(key, value)
    WHERE kv.value IS NOT NULL AND kv.value <> ''
  )
  INSERT INTO oee.lane_grade_events (ts, serial_no, batch_record_id, lane_no, grade_key, qty)
  SELECT ts, serial_no, batch_record_id, lane_no, grade_key, qty
  FROM exploded
  ON CONFLICT (ts, serial_no, lane_no, grade_key)
  DO UPDATE SET
    qty = EXCLUDED.qty,
    batch_record_id = EXCLUDED.batch_record_id;

  GET DIAGNOSTICS v_ins = ROW_COUNT;

  UPDATE oee.etl_watermarks
  SET last_ts = p_to
  WHERE key = 'lanes_grade_fpm';

  RETURN v_ins;
END;
$$;


-- oee.refresh_lane_grade_minute(job_id, config) — TimescaleDB custom job
CREATE OR REPLACE FUNCTION oee.refresh_lane_grade_minute(job_id integer, config jsonb) RETURNS void
    LANGUAGE plpgsql
AS $$
DECLARE
  v_start_offset interval := COALESCE((config->>'start_offset')::interval, interval '02:00:00');
  v_end_offset   interval := COALESCE((config->>'end_offset')::interval,   interval '00:01:00');
BEGIN
  INSERT INTO oee.lane_grade_minute (
    minute_ts, serial_no, batch_record_id, lane_no, grade_key, grade_name, qty
  )
  SELECT
    time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
    m.serial_no,
    m.batch_record_id::int AS batch_record_id,

    -- array idx 0 is lane 1 -> ordinality is already 1..N
    lane_idx.ordinality::bigint AS lane_no,

    kv.key AS grade_key,

    -- suffix after last underscore (e.g. EXP LIGHT, CULLS)
    regexp_replace(kv.key, '^.*_', '') AS grade_name,

    NULLIF(kv.value, '')::double precision AS qty
  FROM public.metrics m
  CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane_idx(lane_json, ordinality)
  CROSS JOIN LATERAL jsonb_each_text(lane_idx.lane_json) kv(key, value)
  WHERE m.metric = 'lanes_grade_fpm'
    AND m.batch_record_id IS NOT NULL
    AND jsonb_typeof(m.value_json) = 'array'
    AND lane_idx.lane_json IS NOT NULL
    AND jsonb_typeof(lane_idx.lane_json) = 'object'
    AND lane_idx.ordinality <= oee.get_lane_count(m.serial_no)
    AND kv.value IS NOT NULL
    AND kv.value <> ''
    AND m.ts >= now() - v_start_offset
    AND m.ts <  now() - v_end_offset
  ON CONFLICT (minute_ts, serial_no, batch_record_id, lane_no, grade_key)
  DO UPDATE SET
    grade_name = EXCLUDED.grade_name,
    qty        = EXCLUDED.qty;
END;
$$;


-- ============================================================================
-- public schema functions
-- ============================================================================


-- public.avg_int_array(j) — V002
CREATE OR REPLACE FUNCTION public.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE
AS $$
  SELECT CASE
           WHEN j IS NULL OR jsonb_typeof(j) <> 'array' THEN 0
           ELSE (SELECT AVG((elem)::int)::double precision
                 FROM   jsonb_array_elements_text(j) t(elem))
         END
$$;


-- public.calc_perf_ratio(total_fpm, missed_fpm, recycle_fpm, target_fpm) — V002 (double precision)
CREATE OR REPLACE FUNCTION public.calc_perf_ratio(total_fpm double precision, missed_fpm double precision, recycle_fpm double precision, target_fpm double precision) RETURNS double precision
    LANGUAGE plpgsql IMMUTABLE
AS $$
DECLARE
    effective double precision := GREATEST(0, total_fpm - missed_fpm - recycle_fpm);
    raw_ratio  double precision;
    ratio      double precision;
BEGIN
    IF target_fpm <= 0 OR effective < 3 THEN
        RETURN 0;
    END IF;

    raw_ratio := effective / target_fpm;

    IF raw_ratio <= 0.5  THEN ratio := raw_ratio;
    ELSIF raw_ratio <= 1 THEN ratio := 0.5 + (raw_ratio - 0.5);
    ELSE                       ratio := 1 - (0.2 / ((raw_ratio - 1) + 0.2));
    END IF;

    RETURN LEAST(1, GREATEST(0, ratio));
END;
$$;


-- public.calc_quality_ratio_qv1(good_qty, peddler_qty, bad_qty, recycle_qty) — V002 (double precision)
CREATE OR REPLACE FUNCTION public.calc_quality_ratio_qv1(good_qty double precision, peddler_qty double precision, bad_qty double precision, recycle_qty double precision) RETURNS double precision
    LANGUAGE plpgsql IMMUTABLE
AS $$
DECLARE
    tgt_good     constant double precision := 0.75;
    tgt_peddler  constant double precision := 0.15;
    tgt_bad      constant double precision := 0.05;
    tgt_recycle  constant double precision := 0.05;

    w_good       constant double precision := 0.40;
    w_peddler    constant double precision := 0.20;
    w_bad        constant double precision := 0.20;
    w_recycle    constant double precision := 0.20;

    sig_k        constant double precision := 4.0;
    clamp_min    constant double precision := -2.0;
    clamp_max    constant double precision :=  2.0;

    total        double precision;
    pct_good     double precision;
    pct_peddler  double precision;
    pct_bad      double precision;
    pct_recycle  double precision;

    raw_good     double precision;
    raw_peddler  double precision;
    raw_bad      double precision;
    raw_recycle  double precision;

    part_good    double precision;
    part_peddler double precision;
    part_bad     double precision;
    part_recycle double precision;
BEGIN
    total := good_qty + peddler_qty + bad_qty + recycle_qty;
    IF total <= 0 THEN
        RETURN 0;
    END IF;

    pct_good     := good_qty     / total;
    pct_peddler  := peddler_qty  / total;
    pct_bad      := bad_qty      / total;
    pct_recycle  := recycle_qty  / total;

    raw_good     := GREATEST(clamp_min, LEAST(clamp_max, 1 + (pct_good     - tgt_good)    / tgt_good));
    raw_peddler  := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_peddler  - tgt_peddler) / tgt_peddler));
    raw_bad      := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_bad      - tgt_bad)     / tgt_bad));
    raw_recycle  := GREATEST(clamp_min, LEAST(clamp_max, 1 - (pct_recycle  - tgt_recycle) / tgt_recycle));

    part_good    := 1 / (1 + exp(-sig_k * (raw_good    - 1)));
    part_peddler := 1 / (1 + exp(-sig_k * (raw_peddler - 1)));
    part_bad     := 1 / (1 + exp(-sig_k * (raw_bad     - 1)));
    part_recycle := 1 / (1 + exp(-sig_k * (raw_recycle - 1)));

    RETURN  part_good    * w_good
          + part_peddler * w_peddler
          + part_bad     * w_bad
          + part_recycle * w_recycle;
END;
$$;


-- public.get_recycle_outlet() — V002
CREATE OR REPLACE FUNCTION public.get_recycle_outlet() RETURNS integer
    LANGUAGE sql IMMUTABLE
AS $$ SELECT recycle_outlet
      FROM   public.machine_settings
      LIMIT  1 $$;


-- public.get_target_throughput() — V002
CREATE OR REPLACE FUNCTION public.get_target_throughput() RETURNS double precision
    LANGUAGE sql STABLE
AS $$
  SELECT target_machine_speed * lane_count * target_percentage / 100.0
  FROM   public.machine_settings
  LIMIT  1
$$;


-- public.grade_to_cat(p_grade) — V016 wrapper (delegates to oee.grade_to_cat)
CREATE OR REPLACE FUNCTION public.grade_to_cat(p_grade text)
RETURNS integer
LANGUAGE sql
IMMUTABLE
AS $$ SELECT oee.grade_to_cat(p_grade); $$;


-- public.outlet_recycle_fpm(j, rid) — V002
CREATE OR REPLACE FUNCTION public.outlet_recycle_fpm(j jsonb, rid integer) RETURNS double precision
    LANGUAGE plpgsql IMMUTABLE
AS $$
DECLARE
    v double precision;
BEGIN
    IF j IS NULL OR jsonb_typeof(j) <> 'array' THEN
        RETURN NULL;
    END IF;

    SELECT (e.value ->> 'DeliveredFruitPerMinute')::double precision
    INTO   v
    FROM   jsonb_array_elements(j) e(value)
    WHERE  (e.value ->> 'Id')::int = rid
    LIMIT  1;

    RETURN v;
END;
$$;


-- public.size_group_value(p_group) — V002
CREATE OR REPLACE FUNCTION public.size_group_value(p_group text) RETURNS double precision
    LANGUAGE sql IMMUTABLE
AS $_$
    SELECT
        (regexp_match(
            p_group,
            '\.(\d+(?:\.\d+)?)\s*$'
        ))[1]::DOUBLE PRECISION;
$_$;
