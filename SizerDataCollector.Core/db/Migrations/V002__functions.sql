-- Ensure schema exists
-- External dependencies (must already exist before applying this migration):
--   - public.machine_settings  (referenced by oee/public get_recycle_outlet, get_target_throughput)
--   - oee.band_definitions     (referenced by oee.classify_oee_value)
-- These are created in earlier migrations (e.g., V001). Running V002 alone on an empty DB will fail if these tables are absent.
CREATE SCHEMA IF NOT EXISTS oee;

-- User-defined functions (public, oee)
-- Extracted from authoritative reference; internal Timescale functions are excluded.

-- OEE schema
CREATE OR REPLACE FUNCTION oee.availability_ratio(state smallint) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE state WHEN 2 THEN 1 WHEN 1 THEN 0.5 ELSE 0 END$$;

CREATE OR REPLACE FUNCTION oee.availability_state(avg_rpm numeric, total_fpm numeric, min_rpm numeric, min_total_fpm numeric) RETURNS smallint
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE
                WHEN (avg_rpm IS NULL OR avg_rpm = 0)
                     THEN CASE WHEN total_fpm >= min_total_fpm THEN 2 ELSE 0 END
                WHEN avg_rpm <  min_rpm                 THEN 0
                WHEN total_fpm < min_total_fpm          THEN 1
                ELSE 2
              END$$;

CREATE OR REPLACE FUNCTION oee.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE
                WHEN j IS NULL OR jsonb_typeof(j) <> 'array' OR jsonb_array_length(j)=0
                THEN 0
                ELSE (SELECT avg(value::numeric)
                      FROM   jsonb_array_elements_text(j) AS t(value))
              END$$;

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

CREATE OR REPLACE FUNCTION oee.get_recycle_outlet() RETURNS integer
    LANGUAGE sql IMMUTABLE
AS $$
SELECT recycle_outlet
FROM   public.machine_settings
ORDER  BY id
LIMIT  1;
$$;

CREATE OR REPLACE FUNCTION oee.get_target_throughput() RETURNS numeric
    LANGUAGE sql STABLE
AS $$
SELECT target_machine_speed * lane_count * target_percentage/100.0
FROM   public.machine_settings
ORDER  BY id
LIMIT  1;
$$;

CREATE OR REPLACE FUNCTION oee.grade_to_cat(p_grade text) RETURNS integer
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

-- Replace/override any older signature
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
    -- jsonb_each_text gives text values; protect against blanks
    v_int := COALESCE(NULLIF(kv.value, '')::integer, 0);

    IF oee.grade_to_cat(kv.key) = desired_cat THEN
      total := total + v_int;
    END IF;
  END LOOP;

  RETURN total;
END;
$$;

CREATE OR REPLACE FUNCTION oee.num(j jsonb, fallback numeric DEFAULT 0) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$SELECT CASE jsonb_typeof(j)
                WHEN 'number' THEN j::numeric
                WHEN 'string' THEN (NULLIF(j::text,'"')::text)::numeric
                ELSE fallback
              END$$;

CREATE OR REPLACE FUNCTION oee.outlet_recycle_fpm(details jsonb, outlet_id integer) RETURNS numeric
    LANGUAGE sql IMMUTABLE PARALLEL SAFE
AS $$
SELECT COALESCE((
         SELECT (elem ->> 'DeliveredFruitPerMinute')::numeric
         FROM   jsonb_array_elements(details) elem
         WHERE  (elem ->> 'Id')::int = outlet_id
         LIMIT  1), 0);
$$;

-- Public schema
CREATE OR REPLACE FUNCTION public.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE
AS $$
  SELECT CASE
           WHEN j IS NULL OR jsonb_typeof(j) <> 'array' THEN 0
           ELSE (SELECT AVG((elem)::int)::double precision
                 FROM   jsonb_array_elements_text(j) t(elem))
         END
$$;

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

CREATE OR REPLACE FUNCTION public.get_recycle_outlet() RETURNS integer
    LANGUAGE sql IMMUTABLE
AS $$ SELECT recycle_outlet
      FROM   public.machine_settings
      LIMIT  1 $$;

CREATE OR REPLACE FUNCTION public.get_target_throughput() RETURNS double precision
    LANGUAGE sql STABLE
AS $$
  SELECT target_machine_speed * lane_count * target_percentage / 100.0
  FROM   public.machine_settings
  LIMIT  1
$$;

CREATE OR REPLACE FUNCTION public.grade_to_cat(p_grade text) RETURNS integer
    LANGUAGE sql IMMUTABLE
AS $$
    SELECT CASE
        WHEN p_grade LIKE '%\_Recycle' ESCAPE '\' OR p_grade LIKE '%\_NAF'     THEN 3
        WHEN p_grade LIKE '%\_Test'    ESCAPE '\' OR p_grade LIKE '%\_D/S'
             OR   p_grade LIKE '%\_Peddler'                                    THEN 1
        WHEN p_grade LIKE '%\_E%' ESCAPE '\'  OR p_grade LIKE '%\_D%'          THEN 0
        WHEN p_grade LIKE '%\_Green'  ESCAPE '\' OR p_grade LIKE '%\_Cull'     THEN 2
        ELSE 2
    END;
$$;

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

CREATE OR REPLACE FUNCTION public.size_group_value(p_group text) RETURNS double precision
    LANGUAGE sql IMMUTABLE
AS $_$
    SELECT
        (regexp_match(
            p_group,
            '\.(\d+(?:\.\d+)?)\s*$'
        ))[1]::DOUBLE PRECISION;
$_$;

-- Public schema
CREATE OR REPLACE FUNCTION public.avg_int_array(j jsonb) RETURNS double precision
    LANGUAGE sql IMMUTABLE
AS $$
  SELECT CASE
           WHEN j IS NULL OR jsonb_typeof(j) <> 'array' THEN 0
           ELSE (SELECT AVG((elem)::int)::double precision
                 FROM   jsonb_array_elements_text(j) t(elem))
         END
$$;

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

