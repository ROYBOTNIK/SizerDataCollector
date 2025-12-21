--
-- PostgreSQL database dump
--

-- Dumped from database version 17.4
-- Dumped by pg_dump version 17.4

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA public;


--
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON SCHEMA public IS 'standard public schema';


--
-- Name: oee; Type: SCHEMA; Schema: -; Owner: -
--

CREATE SCHEMA oee;


--
-- Name: availability_ratio(smallint); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.availability_ratio(state smallint) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$SELECT CASE state WHEN 2 THEN 1   -- RUN
                         WHEN 1 THEN 0.5 -- IDLE
                         ELSE 0 END$$;


--
-- Name: availability_state(numeric, numeric, numeric, numeric); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.availability_state(avg_rpm numeric, total_fpm numeric, min_rpm numeric, min_total_fpm numeric) RETURNS smallint
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$SELECT CASE
                WHEN (avg_rpm IS NULL OR avg_rpm = 0)
                     THEN CASE WHEN total_fpm >= min_total_fpm THEN 2 ELSE 0 END
                WHEN avg_rpm <  min_rpm                 THEN 0
                WHEN total_fpm < min_total_fpm          THEN 1
                ELSE 2
              END$$;


--
-- Name: avg_int_array(jsonb); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$SELECT CASE
                WHEN j IS NULL OR jsonb_typeof(j) <> 'array'
                     OR jsonb_array_length(j)=0
                THEN 0
                ELSE (SELECT avg(value::numeric)
                      FROM   jsonb_array_elements_text(j) AS t(value))
              END$$;


--
-- Name: calc_perf_ratio(numeric, numeric, numeric, numeric); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.calc_perf_ratio(total_fpm numeric, missed_fpm numeric, recycle_fpm numeric, target_fpm numeric) RETURNS numeric
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


--
-- Name: calc_quality_ratio_qv1(numeric, numeric, numeric, numeric); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.calc_quality_ratio_qv1(good_qty numeric, peddler_qty numeric, bad_qty numeric, recycle_qty numeric) RETURNS numeric
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


--
-- Name: classify_oee_value(text, numeric); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.classify_oee_value(p_machine_serial_no text, p_oee_value numeric) RETURNS text
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
    
    -- Fallback to fixed bands if no custom bands defined
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


--
-- Name: get_recycle_outlet(); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.get_recycle_outlet() RETURNS integer
    LANGUAGE sql IMMUTABLE
    AS $$
SELECT recycle_outlet
FROM   public.machine_settings
ORDER  BY id
LIMIT  1;
$$;


--
-- Name: get_target_throughput(); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.get_target_throughput() RETURNS numeric
    LANGUAGE sql STABLE
    AS $$
SELECT target_machine_speed * lane_count * target_percentage/100.0
FROM   public.machine_settings
ORDER  BY id
LIMIT  1;
$$;


--
-- Name: grade_qty(jsonb, integer); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.grade_qty(j jsonb, desired_cat integer) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$
SELECT COALESCE(
         SUM( (kv.value)::numeric ), 0)
FROM   jsonb_array_elements(j)          AS lane(lj)
       CROSS JOIN LATERAL jsonb_each_text(lj) AS kv(key,value)
WHERE  oee.grade_to_cat(kv.key) = desired_cat;
$$;


--
-- Name: grade_to_cat(text); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.grade_to_cat(p_grade text) RETURNS integer
    LANGUAGE sql IMMUTABLE
    AS $$
SELECT CASE
    WHEN p_grade LIKE '%\_Recycle' ESCAPE '\' OR p_grade LIKE '%\_NAF'       THEN 3
    WHEN p_grade LIKE '%\_Test'    ESCAPE '\' OR p_grade LIKE '%\_D/S'
         OR   p_grade LIKE '%\_Peddler'                                     THEN 1
    WHEN p_grade LIKE '%\_E%' ESCAPE '\'  OR p_grade LIKE '%\_D%'           THEN 0
    WHEN p_grade LIKE '%\_Green'  ESCAPE '\' OR p_grade LIKE '%\_Cull'      THEN 2
    ELSE 2 END;
$$;


--
-- Name: num(jsonb, numeric); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.num(j jsonb, fallback numeric DEFAULT 0) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$SELECT CASE jsonb_typeof(j)
                WHEN 'number' THEN j::numeric
                WHEN 'string' THEN (NULLIF(j::text,'"')::text)::numeric
                ELSE fallback
              END$$;


--
-- Name: outlet_recycle_fpm(jsonb, integer); Type: FUNCTION; Schema: oee; Owner: -
--

CREATE FUNCTION oee.outlet_recycle_fpm(details jsonb, outlet_id integer) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
    AS $$
SELECT COALESCE((
         SELECT (elem ->> 'DeliveredFruitPerMinute')::numeric
         FROM   jsonb_array_elements(details) elem
         WHERE  (elem ->> 'Id')::int = outlet_id
         LIMIT  1), 0);
$$;


--
-- Name: avg_int_array(jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE
    AS $$
  SELECT CASE
           WHEN j IS NULL OR jsonb_typeof(j) <> 'array' THEN 0
           ELSE (SELECT AVG((elem)::int)::double precision
                 FROM   jsonb_array_elements_text(j) t(elem))
         END
$$;


--
-- Name: calc_perf_ratio(double precision, double precision, double precision, double precision); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.calc_perf_ratio(total_fpm double precision, missed_fpm double precision, recycle_fpm double precision, target_fpm double precision) RETURNS double precision
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


--
-- Name: calc_quality_ratio_qv1(double precision, double precision, double precision, double precision); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.calc_quality_ratio_qv1(good_qty double precision, peddler_qty double precision, bad_qty double precision, recycle_qty double precision) RETURNS double precision
    LANGUAGE plpgsql IMMUTABLE
    AS $$
DECLARE
    -- targets
    tgt_good     constant double precision := 0.75;
    tgt_peddler  constant double precision := 0.15;
    tgt_bad      constant double precision := 0.05;
    tgt_recycle  constant double precision := 0.05;

    -- weights
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

    /* deviation from target, clamp */
    raw_good     := GREATEST(clamp_min, LEAST(clamp_max,
                   1 + (pct_good     - tgt_good)    / tgt_good));

    raw_peddler  := GREATEST(clamp_min, LEAST(clamp_max,
                   1 - (pct_peddler  - tgt_peddler) / tgt_peddler));

    raw_bad      := GREATEST(clamp_min, LEAST(clamp_max,
                   1 - (pct_bad      - tgt_bad)     / tgt_bad));

    raw_recycle  := GREATEST(clamp_min, LEAST(clamp_max,
                   1 - (pct_recycle  - tgt_recycle) / tgt_recycle));

    -- sigmoid to 0-1
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


--
-- Name: get_recycle_outlet(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.get_recycle_outlet() RETURNS integer
    LANGUAGE sql IMMUTABLE
    AS $$ SELECT recycle_outlet
      FROM   public.machine_settings
      LIMIT  1 $$;


--
-- Name: get_target_throughput(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.get_target_throughput() RETURNS double precision
    LANGUAGE sql STABLE
    AS $$
  SELECT target_machine_speed * lane_count * target_percentage / 100.0
  FROM   public.machine_settings
  LIMIT  1
$$;


--
-- Name: grade_to_cat(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.grade_to_cat(p_grade text) RETURNS integer
    LANGUAGE sql IMMUTABLE
    AS $$
    SELECT CASE
        WHEN p_grade LIKE '%\_Recycle' ESCAPE '\' OR p_grade LIKE '%\_NAF'     THEN 3
        WHEN p_grade LIKE '%\_Test'    ESCAPE '\' OR p_grade LIKE '%\_D/S'
             OR   p_grade LIKE '%\_Peddler'                                    THEN 1
        WHEN p_grade LIKE '%\_E%' ESCAPE '\'  OR p_grade LIKE '%\_D%'          THEN 0   --  relaxed
        WHEN p_grade LIKE '%\_Green'  ESCAPE '\' OR p_grade LIKE '%\_Cull'     THEN 2
        ELSE 2
    END;
$$;


--
-- Name: outlet_recycle_fpm(jsonb, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.outlet_recycle_fpm(j jsonb, rid integer) RETURNS double precision
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


--
-- Name: size_group_value(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.size_group_value(p_group text) RETURNS double precision
    LANGUAGE sql IMMUTABLE
    AS $_$
    /* Match the number after the LAST dot, up to the real end of the text */
    SELECT
        (regexp_match(
            p_group,
            '\.(\d+(?:\.\d+)?)\s*$'   -- dot-digits[.digits] until EOL
        ))[1]::DOUBLE PRECISION
    ;
$_$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: machine_thresholds; Type: TABLE; Schema: oee; Owner: -
--

CREATE TABLE oee.machine_thresholds (
    serial_no text NOT NULL,
    min_rpm numeric NOT NULL,
    min_total_fpm numeric NOT NULL,
    updated_at timestamp with time zone DEFAULT now()
);


--
-- Name: metrics; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.metrics (
    ts timestamp with time zone NOT NULL,
    serial_no text NOT NULL,
    batch_id integer,
    metric text NOT NULL,
    value_json jsonb NOT NULL,
    batch_record_id bigint NOT NULL
);


--
-- Name: cagg_availability_minute; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_availability_minute AS
 SELECT minute_ts,
    serial_no,
    avg_rpm,
    total_fpm,
    min_rpm,
    min_total_fpm,
    state,
    availability
   FROM _timescaledb_internal._materialized_hypertable_10;


--
-- Name: batches; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.batches (
    batch_id integer NOT NULL,
    serial_no text,
    grower_code text,
    start_ts timestamp with time zone NOT NULL,
    end_ts timestamp with time zone,
    comments text,
    id bigint NOT NULL
);


--
-- Name: cagg_availability_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_availability_minute_batch AS
 SELECT minute_ts,
    serial_no,
    batch_record_id,
    lot,
    variety,
    avg_rpm,
    total_fpm,
    min_rpm,
    min_total_fpm,
    state,
    availability
   FROM _timescaledb_internal._materialized_hypertable_13;


--
-- Name: cagg_throughput_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_throughput_minute_batch AS
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
    combined_recycle_fpm
   FROM _timescaledb_internal._materialized_hypertable_15;


--
-- Name: cagg_grade_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_grade_minute_batch AS
 SELECT minute_ts,
    serial_no,
    batch_record_id,
    lot,
    variety,
    good_qty,
    peddler_qty,
    bad_qty,
    recycle_qty,
    quality_ratio
   FROM _timescaledb_internal._materialized_hypertable_17;


--
-- Name: cagg_throughput_minute; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.cagg_throughput_minute AS
 SELECT minute_ts,
    total_fpm,
    missed_fpm,
    machine_recycle_fpm,
    cupfill_pct,
    tph,
    outlet_recycle_fpm,
    combined_recycle_fpm
   FROM _timescaledb_internal._materialized_hypertable_8;


--
-- Name: calculated_metrics; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.calculated_metrics (
    ts timestamp with time zone NOT NULL,
    serial_no text NOT NULL,
    batch_id integer,
    shift_id text,
    metric text NOT NULL,
    value double precision NOT NULL,
    batch_record_id bigint NOT NULL
);


--
-- Name: band_definitions; Type: TABLE; Schema: oee; Owner: -
--

CREATE TABLE oee.band_definitions (
    machine_serial_no text NOT NULL,
    effective_date date NOT NULL,
    band_name text NOT NULL,
    lower_bound numeric(5,4) NOT NULL,
    upper_bound numeric(5,4) NOT NULL,
    created_at timestamp with time zone DEFAULT now(),
    created_by text DEFAULT 'system'::text,
    is_active boolean DEFAULT true,
    CONSTRAINT band_definitions_check CHECK (((upper_bound >= (0)::numeric) AND (upper_bound <= (1)::numeric) AND (upper_bound > lower_bound))),
    CONSTRAINT band_definitions_lower_bound_check CHECK (((lower_bound >= (0)::numeric) AND (lower_bound <= (1)::numeric)))
);


--
-- Name: band_statistics; Type: TABLE; Schema: oee; Owner: -
--

CREATE TABLE oee.band_statistics (
    machine_serial_no text NOT NULL,
    calculation_date date NOT NULL,
    band_name text NOT NULL,
    avg_availability numeric(5,4),
    avg_performance numeric(5,4),
    avg_quality numeric(5,4),
    avg_oee numeric(5,4),
    minute_count integer,
    created_at timestamp with time zone DEFAULT now()
);


--
-- Name: cagg_availability_daily; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_availability_daily AS
 SELECT day,
    serial_no,
    minutes_run,
    minutes_idle,
    minutes_down,
    avg_availability
   FROM _timescaledb_internal._materialized_hypertable_11;


--
-- Name: cagg_availability_daily_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_availability_daily_batch AS
 SELECT day,
    serial_no,
    batch_record_id,
    lot,
    variety,
    minutes_run,
    minutes_idle,
    minutes_down,
    avg_availability
   FROM _timescaledb_internal._materialized_hypertable_14;


--
-- Name: cagg_grade_daily_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_grade_daily_batch AS
 SELECT day,
    serial_no,
    batch_record_id,
    lot,
    variety,
    good_qty,
    peddler_qty,
    bad_qty,
    recycle_qty,
    quality_ratio
   FROM _timescaledb_internal._materialized_hypertable_18;


--
-- Name: cagg_throughput_daily_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.cagg_throughput_daily_batch AS
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
    tph
   FROM _timescaledb_internal._materialized_hypertable_16;


--
-- Name: grade_lane_anomalies; Type: TABLE; Schema: oee; Owner: -
--

CREATE TABLE oee.grade_lane_anomalies (
    event_ts timestamp with time zone NOT NULL,
    serial_no text NOT NULL,
    batch_record_id integer NOT NULL,
    lane_no smallint NOT NULL,
    grade_key text NOT NULL,
    qty double precision NOT NULL,
    pct double precision NOT NULL,
    anomaly_score double precision NOT NULL,
    severity text NOT NULL,
    explanation jsonb,
    model_version text NOT NULL,
    delivered_to text NOT NULL
);


--
-- Name: shift_calendar; Type: TABLE; Schema: oee; Owner: -
--

CREATE TABLE oee.shift_calendar (
    break_start timestamp with time zone NOT NULL,
    break_end timestamp with time zone NOT NULL
);


--
-- Name: v_availability_minute; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_availability_minute AS
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
   FROM (oee.cagg_availability_minute cm
     LEFT JOIN oee.shift_calendar sc ON (((cm.minute_ts >= sc.break_start) AND (cm.minute_ts < sc.break_end))));


--
-- Name: v_availability_minute_raw; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_availability_minute_raw AS
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


--
-- Name: v_current_bands; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_current_bands AS
 SELECT machine_serial_no,
    band_name,
    lower_bound,
    upper_bound,
    effective_date,
    created_by,
    created_at
   FROM oee.band_definitions
  WHERE (is_active = true)
  ORDER BY machine_serial_no, lower_bound;


--
-- Name: v_grade_pct_lane_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_grade_pct_lane_minute_batch AS
 WITH raw AS (
         SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
            m.serial_no,
            m.batch_record_id,
            (lane_idx.ordinality - 1) AS lane_no,
            kv.key AS grade_key,
            (kv.value)::double precision AS qty
           FROM ((public.metrics m
             CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane_idx(lane_json, ordinality))
             CROSS JOIN LATERAL jsonb_each_text(lane_idx.lane_json) kv(key, value))
          WHERE (m.metric = 'lanes_grade_fpm'::text)
        ), tot AS (
         SELECT raw.minute_ts,
            raw.batch_record_id,
            sum(raw.qty) AS total_qty
           FROM raw
          GROUP BY raw.minute_ts, raw.batch_record_id
        )
 SELECT r.minute_ts,
    r.serial_no,
    r.batch_record_id,
    b.grower_code AS lot,
    b.comments AS variety,
    r.lane_no,
    r.grade_key,
    r.qty,
    (round((((r.qty * (100.0)::double precision) / NULLIF(t.total_qty, (0)::double precision)))::numeric, 2))::double precision AS pct
   FROM ((raw r
     JOIN tot t USING (minute_ts, batch_record_id))
     LEFT JOIN public.batches b ON ((b.id = r.batch_record_id)));


--
-- Name: v_grade_pct_per_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_grade_pct_per_minute_batch AS
 WITH raw AS (
         SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
            m.serial_no,
            m.batch_record_id,
            g.key AS grade_key,
            (g.value)::double precision AS qty
           FROM ((public.metrics m
             CROSS JOIN LATERAL jsonb_array_elements(m.value_json) lane(j))
             CROSS JOIN LATERAL jsonb_each_text(lane.j) g(key, value))
          WHERE (m.metric = 'lanes_grade_fpm'::text)
        ), tot AS (
         SELECT raw.minute_ts,
            raw.batch_record_id,
            sum(raw.qty) AS total_qty
           FROM raw
          GROUP BY raw.minute_ts, raw.batch_record_id
        )
 SELECT r.minute_ts,
    r.serial_no,
    r.batch_record_id,
    b.grower_code AS lot,
    b.comments AS variety,
    r.grade_key,
    r.qty,
    (round((((r.qty * (100.0)::double precision) / NULLIF(t.total_qty, (0)::double precision)))::numeric, 2))::double precision AS pct
   FROM ((raw r
     JOIN tot t USING (minute_ts, batch_record_id))
     LEFT JOIN public.batches b ON ((b.id = r.batch_record_id)));


--
-- Name: v_grade_pct_suffix_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_grade_pct_suffix_minute_batch AS
 WITH base AS (
         SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
            m.serial_no,
            m.batch_record_id,
            split_part(kv.key, '_'::text, array_length(string_to_array(kv.key, '_'::text), 1)) AS suffix,
            (kv.value)::double precision AS qty
           FROM ((public.metrics m
             CROSS JOIN LATERAL jsonb_array_elements(m.value_json) l(j))
             CROSS JOIN LATERAL jsonb_each_text(l.j) kv(key, value))
          WHERE (m.metric = 'lanes_grade_fpm'::text)
        ), sub AS (
         SELECT base.minute_ts,
            base.serial_no,
            base.batch_record_id,
            base.suffix,
            sum(base.qty) AS qty
           FROM base
          GROUP BY base.minute_ts, base.serial_no, base.batch_record_id, base.suffix
        ), agg AS (
         SELECT sub.minute_ts,
            sub.serial_no,
            sub.batch_record_id,
            sub.suffix,
            sub.qty,
            sum(sub.qty) OVER (PARTITION BY sub.minute_ts, sub.batch_record_id) AS total_qty
           FROM sub
        )
 SELECT a.minute_ts,
    a.serial_no,
    a.batch_record_id,
    b.grower_code AS lot,
    b.comments AS variety,
    a.suffix,
    a.qty,
    (round((((a.qty * (100.0)::double precision) / NULLIF(a.total_qty, (0)::double precision)))::numeric, 2))::double precision AS pct
   FROM (agg a
     LEFT JOIN public.batches b ON ((b.id = a.batch_record_id)));


--
-- Name: v_throughput_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

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
    oee.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, oee.get_target_throughput()) AS throughput_ratio
   FROM oee.cagg_throughput_minute_batch;


--
-- Name: v_oee_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_oee_minute_batch AS
 SELECT a.minute_ts,
    a.serial_no,
    a.batch_record_id,
    a.lot,
    a.variety,
    a.availability AS availability_ratio,
    t.throughput_ratio AS performance_ratio,
    g.quality_ratio,
    ((a.availability * t.throughput_ratio) * g.quality_ratio) AS oee_ratio
   FROM ((oee.cagg_availability_minute_batch a
     JOIN oee.v_throughput_minute_batch t USING (minute_ts, serial_no, batch_record_id))
     JOIN oee.cagg_grade_minute_batch g USING (minute_ts, serial_no, batch_record_id));


--
-- Name: v_oee_daily_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_oee_daily_batch AS
 SELECT public.time_bucket('1 day'::interval, minute_ts) AS day,
    serial_no,
    batch_record_id,
    min(lot) AS lot,
    min(variety) AS variety,
    avg(availability_ratio) AS avg_availability,
    avg(performance_ratio) AS avg_performance,
    avg(quality_ratio) AS avg_quality,
    ((avg(availability_ratio) * avg(performance_ratio)) * avg(quality_ratio)) AS oee_product,
    avg(oee_ratio) AS oee_average
   FROM oee.v_oee_minute_batch
  GROUP BY (public.time_bucket('1 day'::interval, minute_ts)), serial_no, batch_record_id;


--
-- Name: v_quality_daily_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_quality_daily_batch AS
 SELECT day,
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


--
-- Name: v_quality_minute_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_quality_minute_batch AS
 SELECT minute_ts,
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


--
-- Name: v_throughput_daily_batch; Type: VIEW; Schema: oee; Owner: -
--

CREATE VIEW oee.v_throughput_daily_batch AS
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
    oee.calc_perf_ratio(total_fpm, missed_fpm, combined_recycle_fpm, oee.get_target_throughput()) AS throughput_ratio
   FROM oee.cagg_throughput_daily_batch;


--
-- Name: cagg_lane_grade_minute; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.cagg_lane_grade_minute AS
 SELECT minute_ts,
    good_qty,
    peddler_qty,
    bad_qty,
    recycle_qty,
    quality_ratio
   FROM _timescaledb_internal._materialized_hypertable_3;


--
-- Name: batch_grade_components_qv1; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.batch_grade_components_qv1 AS
 SELECT b.id AS batch_id,
    b.grower_code AS lot,
    b.comments AS variety,
    b.start_ts,
    b.end_ts,
    sum(q.good_qty) AS good_qty,
    sum(q.peddler_qty) AS peddler_qty,
    sum(q.bad_qty) AS bad_qty,
    sum(q.recycle_qty) AS recycle_qty,
    public.calc_quality_ratio_qv1(sum(q.good_qty), sum(q.peddler_qty), sum(q.bad_qty), sum(q.recycle_qty)) AS quality_ratio
   FROM (public.batches b
     LEFT JOIN public.cagg_lane_grade_minute q ON (((q.minute_ts >= b.start_ts) AND ((b.end_ts IS NULL) OR (q.minute_ts <= b.end_ts)))))
  GROUP BY b.id, b.grower_code, b.comments, b.start_ts, b.end_ts
  ORDER BY b.start_ts DESC;


--
-- Name: batches_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.batches_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: batches_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.batches_id_seq OWNED BY public.batches.id;


--
-- Name: cagg_lane_size_minute; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.cagg_lane_size_minute AS
 SELECT minute_ts,
    lane_idx,
    fruit_cnt,
    avg_size
   FROM _timescaledb_internal._materialized_hypertable_5;


--
-- Name: cagg_throughput_daily; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.cagg_throughput_daily AS
 SELECT day,
    total_fpm,
    missed_fpm,
    recycle_fpm,
    outlet_recycle_fpm,
    combined_recycle_fpm,
    cupfill_pct,
    tph
   FROM _timescaledb_internal._materialized_hypertable_9;


--
-- Name: minute_quality_view_qv1_old; Type: MATERIALIZED VIEW; Schema: public; Owner: -
--

CREATE MATERIALIZED VIEW public.minute_quality_view_qv1_old AS
 WITH lane_grade AS (
         SELECT m.ts,
            g.key AS grade_name,
            (g.value)::double precision AS qty
           FROM ((public.metrics m
             CROSS JOIN LATERAL jsonb_array_elements(m.value_json) lane(lane_json))
             CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) g(key, value))
          WHERE (m.metric = 'lanes_grade_fpm'::text)
        ), cats AS (
         SELECT lane_grade.ts,
                CASE
                    WHEN ((lane_grade.grade_name ~~ '%_Recycle'::text) OR (lane_grade.grade_name ~~ '%_NAF'::text)) THEN 3
                    WHEN ((lane_grade.grade_name ~~ '%_Test'::text) OR (lane_grade.grade_name ~~ '%_D/S'::text) OR (lane_grade.grade_name ~~ '%_Peddler'::text)) THEN 1
                    WHEN ((lane_grade.grade_name ~~ '%_E_'::text) OR (lane_grade.grade_name ~~ '%_D_'::text)) THEN 0
                    WHEN ((lane_grade.grade_name ~~ '%_Green'::text) OR (lane_grade.grade_name ~~ '%_Cull'::text)) THEN 2
                    ELSE 2
                END AS cat,
            lane_grade.qty
           FROM lane_grade
        )
 SELECT ts,
    sum(
        CASE
            WHEN (cat = 0) THEN qty
            ELSE NULL::double precision
        END) AS good_qty,
    sum(
        CASE
            WHEN (cat = 1) THEN qty
            ELSE NULL::double precision
        END) AS peddler_qty,
    sum(
        CASE
            WHEN (cat = 2) THEN qty
            ELSE NULL::double precision
        END) AS bad_qty,
    sum(
        CASE
            WHEN (cat = 3) THEN qty
            ELSE NULL::double precision
        END) AS recycle_qty,
    public.calc_quality_ratio_qv1(COALESCE(sum(
        CASE
            WHEN (cat = 0) THEN qty
            ELSE NULL::double precision
        END), (0)::double precision), COALESCE(sum(
        CASE
            WHEN (cat = 1) THEN qty
            ELSE NULL::double precision
        END), (0)::double precision), COALESCE(sum(
        CASE
            WHEN (cat = 2) THEN qty
            ELSE NULL::double precision
        END), (0)::double precision), COALESCE(sum(
        CASE
            WHEN (cat = 3) THEN qty
            ELSE NULL::double precision
        END), (0)::double precision)) AS quality_ratio
   FROM cats
  GROUP BY ts
  WITH NO DATA;


--
-- Name: daily_grade_components_qv1; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.daily_grade_components_qv1 AS
 SELECT (ts)::date AS day,
    sum(good_qty) AS good_qty,
    sum(peddler_qty) AS peddler_qty,
    sum(bad_qty) AS bad_qty,
    sum(recycle_qty) AS recycle_qty,
    public.calc_quality_ratio_qv1(sum(good_qty), sum(peddler_qty), sum(bad_qty), sum(recycle_qty)) AS quality_ratio
   FROM public.minute_quality_view_qv1_old
  GROUP BY ((ts)::date);


--
-- Name: machine_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.machine_settings (
    id integer NOT NULL,
    target_machine_speed double precision NOT NULL,
    lane_count integer NOT NULL,
    target_percentage double precision NOT NULL,
    recycle_outlet integer NOT NULL
);


--
-- Name: daily_throughput_components; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.daily_throughput_components AS
 WITH t AS (
         SELECT (metrics.ts)::date AS day,
            avg(
                CASE
                    WHEN (metrics.metric = 'machine_total_fpm'::text) THEN (metrics.value_json)::double precision
                    ELSE NULL::double precision
                END) AS total_fpm,
            avg(
                CASE
                    WHEN (metrics.metric = 'machine_missed_fpm'::text) THEN (metrics.value_json)::double precision
                    ELSE NULL::double precision
                END) AS missed_fpm,
            avg(
                CASE
                    WHEN (metrics.metric = 'machine_recycle_fpm'::text) THEN (metrics.value_json)::double precision
                    ELSE NULL::double precision
                END) AS recycle_fpm,
            avg(
                CASE
                    WHEN (metrics.metric = 'machine_cupfill'::text) THEN (metrics.value_json)::double precision
                    ELSE NULL::double precision
                END) AS cupfill_pct,
            avg(
                CASE
                    WHEN (metrics.metric = 'machine_tph'::text) THEN (metrics.value_json)::double precision
                    ELSE NULL::double precision
                END) AS tph
           FROM public.metrics
          WHERE (metrics.metric = ANY (ARRAY['machine_total_fpm'::text, 'machine_missed_fpm'::text, 'machine_recycle_fpm'::text, 'machine_cupfill'::text, 'machine_tph'::text]))
          GROUP BY ((metrics.ts)::date)
        ), o AS (
         SELECT (metrics.ts)::date AS day,
            avg(((elem.value ->> 'DeliveredFruitPerMinute'::text))::double precision) AS outlet_recycle_fpm
           FROM (public.metrics
             CROSS JOIN LATERAL jsonb_array_elements(metrics.value_json) elem(value))
          WHERE ((metrics.metric = 'outlets_details'::text) AND (((elem.value ->> 'Id'::text))::integer = ( SELECT machine_settings.recycle_outlet
                   FROM public.machine_settings
                 LIMIT 1)))
          GROUP BY ((metrics.ts)::date)
        )
 SELECT t.day,
    t.total_fpm,
    t.missed_fpm,
    t.recycle_fpm,
    COALESCE(o.outlet_recycle_fpm, (0)::double precision) AS outlet_recycle_fpm,
    (t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, (0)::double precision)) AS combined_recycle_fpm,
    t.cupfill_pct,
    t.tph,
    public.calc_perf_ratio(t.total_fpm, t.missed_fpm, (t.recycle_fpm + COALESCE(o.outlet_recycle_fpm, (0)::double precision)), ( SELECT (((machine_settings.target_machine_speed * (machine_settings.lane_count)::double precision) * machine_settings.target_percentage) / (100)::double precision)
           FROM public.machine_settings
         LIMIT 1)) AS throughput_ratio
   FROM (t
     LEFT JOIN o USING (day));


--
-- Name: lane_size_anomaly; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.lane_size_anomaly AS
 SELECT minute_ts,
    lane_idx,
    avg_size,
    avg(avg_size) OVER (PARTITION BY minute_ts) AS mean_size,
    stddev_pop(avg_size) OVER (PARTITION BY minute_ts) AS sd_size,
    ((avg_size - avg(avg_size) OVER (PARTITION BY minute_ts)) / NULLIF(stddev_pop(avg_size) OVER (PARTITION BY minute_ts), (0)::double precision)) AS z_score
   FROM public.cagg_lane_size_minute;


--
-- Name: lane_size_health_24h; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.lane_size_health_24h AS
 WITH windowed AS (
         SELECT lane_size_anomaly.lane_idx,
            (abs(lane_size_anomaly.z_score) >= (2)::double precision) AS out_spec,
            (lane_size_anomaly.z_score > (2)::double precision) AS oversize,
            (lane_size_anomaly.z_score < ('-2'::integer)::double precision) AS undersize
           FROM public.lane_size_anomaly
          WHERE (lane_size_anomaly.minute_ts >= (now() - '24:00:00'::interval))
        )
 SELECT (lane_idx + 1) AS lane,
    count(*) AS total_min,
    count(*) FILTER (WHERE out_spec) AS out_min,
    count(*) FILTER (WHERE oversize) AS over_min,
    count(*) FILTER (WHERE undersize) AS under_min,
    round(((100.0 * (count(*) FILTER (WHERE oversize))::numeric) / (count(*))::numeric), 1) AS pct_over,
    round(((100.0 * (count(*) FILTER (WHERE undersize))::numeric) / (count(*))::numeric), 1) AS pct_under
   FROM windowed
  GROUP BY lane_idx
  ORDER BY (lane_idx + 1);


--
-- Name: lane_size_health_season; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.lane_size_health_season AS
 WITH unpack AS (
         SELECT public.time_bucket('00:01:00'::interval, m.ts) AS minute_ts,
            (lane.ord - 1) AS lane_idx,
            v.key AS label,
            (v.value)::integer AS fruit_cnt
           FROM ((public.metrics m
             CROSS JOIN LATERAL jsonb_array_elements(m.value_json) WITH ORDINALITY lane(lane_json, ord))
             CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) v(key, value))
          WHERE ((m.metric = 'lanes_size_fpm'::text) AND (m.serial_no = '140578'::text))
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
                    WHEN ((stats."å" = (0)::double precision) OR (stats.avg_size IS NULL)) THEN (0)::double precision
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


--
-- Name: machine_settings_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.machine_settings_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: machine_settings_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.machine_settings_id_seq OWNED BY public.machine_settings.id;


--
-- Name: machines; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.machines (
    serial_no text NOT NULL,
    name text NOT NULL,
    inserted_at timestamp with time zone DEFAULT now()
);


--
-- Name: minute_throughput_view; Type: MATERIALIZED VIEW; Schema: public; Owner: -
--

CREATE MATERIALIZED VIEW public.minute_throughput_view AS
 SELECT m_total.ts,
    (m_total.value_json)::double precision AS total_fpm,
    COALESCE((m_missed.value_json)::double precision, (0)::double precision) AS missed_fpm,
    COALESCE((m_recycle.value_json)::double precision, (0)::double precision) AS machine_recycle_fpm,
    (m_cupfill.value_json)::double precision AS cupfill_pct,
    (m_tph.value_json)::double precision AS tph,
    COALESCE(( SELECT ((elem.value ->> 'DeliveredFruitPerMinute'::text))::double precision AS float8
           FROM jsonb_array_elements(m_outlet.value_json) elem(value)
          WHERE (((elem.value ->> 'Id'::text))::integer = ( SELECT machine_settings.recycle_outlet
                   FROM public.machine_settings
                 LIMIT 1))
         LIMIT 1), (0)::double precision) AS outlet_recycle_fpm,
    public.calc_perf_ratio((m_total.value_json)::double precision, COALESCE((m_missed.value_json)::double precision, (0)::double precision), (COALESCE((m_recycle.value_json)::double precision, (0)::double precision) + COALESCE(( SELECT ((elem.value ->> 'DeliveredFruitPerMinute'::text))::double precision AS float8
           FROM jsonb_array_elements(m_outlet.value_json) elem(value)
          WHERE (((elem.value ->> 'Id'::text))::integer = ( SELECT machine_settings.recycle_outlet
                   FROM public.machine_settings
                 LIMIT 1))
         LIMIT 1), (0)::double precision)), ( SELECT (((machine_settings.target_machine_speed * (machine_settings.lane_count)::double precision) * machine_settings.target_percentage) / (100)::double precision)
           FROM public.machine_settings
         LIMIT 1)) AS throughput_ratio
   FROM (((((public.metrics m_total
     LEFT JOIN LATERAL ( SELECT metrics.value_json
           FROM public.metrics
          WHERE ((metrics.ts = m_total.ts) AND (metrics.metric = 'machine_missed_fpm'::text))
         LIMIT 1) m_missed(value_json) ON (true))
     LEFT JOIN LATERAL ( SELECT metrics.value_json
           FROM public.metrics
          WHERE ((metrics.ts = m_total.ts) AND (metrics.metric = 'machine_recycle_fpm'::text))
         LIMIT 1) m_recycle(value_json) ON (true))
     LEFT JOIN LATERAL ( SELECT metrics.value_json
           FROM public.metrics
          WHERE ((metrics.ts = m_total.ts) AND (metrics.metric = 'machine_cupfill'::text))
         LIMIT 1) m_cupfill(value_json) ON (true))
     LEFT JOIN LATERAL ( SELECT metrics.value_json
           FROM public.metrics
          WHERE ((metrics.ts = m_total.ts) AND (metrics.metric = 'machine_tph'::text))
         LIMIT 1) m_tph(value_json) ON (true))
     LEFT JOIN LATERAL ( SELECT metrics.value_json
           FROM public.metrics
          WHERE ((metrics.ts = m_total.ts) AND (metrics.metric = 'outlets_details'::text))
         LIMIT 1) m_outlet(value_json) ON (true))
  WHERE (m_total.metric = 'machine_total_fpm'::text)
  WITH NO DATA;


--
-- Name: mv_lane_size_health_season; Type: MATERIALIZED VIEW; Schema: public; Owner: -
--

CREATE MATERIALIZED VIEW public.mv_lane_size_health_season AS
 SELECT lane,
    total_min,
    out_min,
    pct_over,
    pct_under
   FROM public.lane_size_health_season
  WITH NO DATA;


--
-- Name: v_quality_minute_filled; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.v_quality_minute_filled AS
 SELECT t.minute_ts,
    COALESCE(q.quality_ratio, (0.0)::double precision) AS quality_ratio
   FROM (public.cagg_throughput_minute t
     LEFT JOIN public.cagg_lane_grade_minute q ON ((q.minute_ts = t.minute_ts)));


--
-- Name: v_throughput_daily; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.v_throughput_daily AS
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


--
-- Name: v_throughput_minute; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.v_throughput_minute AS
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


--
-- Name: batches id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches ALTER COLUMN id SET DEFAULT nextval('public.batches_id_seq'::regclass);


--
-- Name: machine_settings id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.machine_settings ALTER COLUMN id SET DEFAULT nextval('public.machine_settings_id_seq'::regclass);


--
-- Name: band_definitions band_definitions_pkey; Type: CONSTRAINT; Schema: oee; Owner: -
--

ALTER TABLE ONLY oee.band_definitions
    ADD CONSTRAINT band_definitions_pkey PRIMARY KEY (machine_serial_no, effective_date, band_name);


--
-- Name: band_statistics band_statistics_pkey; Type: CONSTRAINT; Schema: oee; Owner: -
--

ALTER TABLE ONLY oee.band_statistics
    ADD CONSTRAINT band_statistics_pkey PRIMARY KEY (machine_serial_no, calculation_date, band_name);


--
-- Name: machine_thresholds machine_thresholds_pkey; Type: CONSTRAINT; Schema: oee; Owner: -
--

ALTER TABLE ONLY oee.machine_thresholds
    ADD CONSTRAINT machine_thresholds_pkey PRIMARY KEY (serial_no);


--
-- Name: shift_calendar shift_calendar_pkey; Type: CONSTRAINT; Schema: oee; Owner: -
--

ALTER TABLE ONLY oee.shift_calendar
    ADD CONSTRAINT shift_calendar_pkey PRIMARY KEY (break_start, break_end);


--
-- Name: batches batches_id_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches
    ADD CONSTRAINT batches_id_pkey PRIMARY KEY (id);


--
-- Name: batches batches_run_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches
    ADD CONSTRAINT batches_run_key UNIQUE (serial_no, batch_id, comments, start_ts);


--
-- Name: machine_settings machine_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.machine_settings
    ADD CONSTRAINT machine_settings_pkey PRIMARY KEY (id);


--
-- Name: machines machines_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.machines
    ADD CONSTRAINT machines_pkey PRIMARY KEY (serial_no);


--
-- Name: metrics metrics_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.metrics
    ADD CONSTRAINT metrics_pkey PRIMARY KEY (ts, serial_no, metric);


--
-- Name: grade_lane_anomalies_batch_record_id_idx; Type: INDEX; Schema: oee; Owner: -
--

CREATE INDEX grade_lane_anomalies_batch_record_id_idx ON oee.grade_lane_anomalies USING btree (batch_record_id);


--
-- Name: grade_lane_anomalies_event_ts_idx; Type: INDEX; Schema: oee; Owner: -
--

CREATE INDEX grade_lane_anomalies_event_ts_idx ON oee.grade_lane_anomalies USING btree (event_ts DESC);


--
-- Name: grade_lane_anomalies_event_ts_severity_idx; Type: INDEX; Schema: oee; Owner: -
--

CREATE INDEX grade_lane_anomalies_event_ts_severity_idx ON oee.grade_lane_anomalies USING btree (event_ts DESC, severity);


--
-- Name: idx_band_definitions_active; Type: INDEX; Schema: oee; Owner: -
--

CREATE INDEX idx_band_definitions_active ON oee.band_definitions USING btree (machine_serial_no, is_active, effective_date DESC);


--
-- Name: idx_band_statistics_date; Type: INDEX; Schema: oee; Owner: -
--

CREATE INDEX idx_band_statistics_date ON oee.band_statistics USING btree (calculation_date DESC);


--
-- Name: calculated_metrics_ts_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX calculated_metrics_ts_idx ON public.calculated_metrics USING btree (ts DESC);


--
-- Name: idx_batches_batch_comment_ts; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batches_batch_comment_ts ON public.batches USING btree (batch_id, comments, start_ts);


--
-- Name: idx_batches_start_ts; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_batches_start_ts ON public.batches USING btree (start_ts);


--
-- Name: idx_calcmetrics_batchrec_ts; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_calcmetrics_batchrec_ts ON public.calculated_metrics USING btree (batch_record_id, ts);


--
-- Name: idx_cm_metric_batch; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_cm_metric_batch ON public.calculated_metrics USING btree (metric, batch_record_id);


--
-- Name: idx_metrics_batchrec_ts; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_metrics_batchrec_ts ON public.metrics USING btree (batch_record_id, ts);


--
-- Name: metrics_batch_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX metrics_batch_idx ON public.metrics USING btree (batch_id);


--
-- Name: metrics_batch_record_id_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX metrics_batch_record_id_idx ON public.metrics USING btree (batch_record_id);


--
-- Name: metrics_ts_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX metrics_ts_idx ON public.metrics USING btree (ts DESC);


--
-- Name: minute_quality_view_qv1_ts_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX minute_quality_view_qv1_ts_idx ON public.minute_quality_view_qv1_old USING btree (ts);


--
-- Name: minute_throughput_view_ts_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX minute_throughput_view_ts_idx ON public.minute_throughput_view USING btree (ts);


--
-- Name: grade_lane_anomalies ts_insert_blocker; Type: TRIGGER; Schema: oee; Owner: -
--

CREATE TRIGGER ts_insert_blocker BEFORE INSERT ON oee.grade_lane_anomalies FOR EACH ROW EXECUTE FUNCTION _timescaledb_functions.insert_blocker();


--
-- Name: metrics ts_cagg_invalidation_trigger; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ts_cagg_invalidation_trigger AFTER INSERT OR DELETE OR UPDATE ON public.metrics FOR EACH ROW EXECUTE FUNCTION _timescaledb_functions.continuous_agg_invalidation_trigger('1');


--
-- Name: calculated_metrics ts_insert_blocker; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ts_insert_blocker BEFORE INSERT ON public.calculated_metrics FOR EACH ROW EXECUTE FUNCTION _timescaledb_functions.insert_blocker();


--
-- Name: metrics ts_insert_blocker; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER ts_insert_blocker BEFORE INSERT ON public.metrics FOR EACH ROW EXECUTE FUNCTION _timescaledb_functions.insert_blocker();


--
-- Name: batches batches_serial_no_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.batches
    ADD CONSTRAINT batches_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);


--
-- Name: calculated_metrics calculated_metrics_batch_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.calculated_metrics
    ADD CONSTRAINT calculated_metrics_batch_record_id_fkey FOREIGN KEY (batch_record_id) REFERENCES public.batches(id);


--
-- Name: metrics metrics_batch_record_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.metrics
    ADD CONSTRAINT metrics_batch_record_id_fkey FOREIGN KEY (batch_record_id) REFERENCES public.batches(id);


--
-- Name: metrics metrics_serial_no_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.metrics
    ADD CONSTRAINT metrics_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);


--
-- PostgreSQL database dump complete
--

