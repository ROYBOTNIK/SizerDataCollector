-- Make grade_to_cat support suffix-based overrides (e.g., short keys like "_EXP DARK")
-- while keeping existing exact-match behavior and pattern fallback.

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
        WHEN v_base ILIKE '%\_RCY%'      THEN 3  -- recycle
        WHEN v_base ILIKE '%\_RECYCLE%'  THEN 3
        WHEN v_base ILIKE '%\_GATE%'     THEN 1  -- gate (per latest mapping)
        WHEN v_base ILIKE '%\_REJ%'      THEN 1
        WHEN v_base ILIKE '%\_TEST%'     THEN 1
        WHEN v_base ILIKE '%\_D/S%'      THEN 1
        WHEN v_base ILIKE '%\_PEDDLER%'  THEN 1
        WHEN v_base ILIKE '%\_EXP%'      THEN 0  -- good
        WHEN v_base ILIKE '%\_DOM%'      THEN 0  -- good
        WHEN v_base ILIKE '%\_CULL%'     THEN 2  -- bad
        WHEN v_base ILIKE '%\_GREEN%'    THEN 1  -- gate per request
        WHEN v_base ILIKE '%\_NAF%'      THEN 3
        ELSE NULL
    END;
END;
$$;

-- Keep single-arg wrappers delegating to serial-aware function.
CREATE OR REPLACE FUNCTION oee.grade_to_cat(p_grade text)
RETURNS integer
LANGUAGE sql
IMMUTABLE
AS $$ SELECT oee.grade_to_cat(NULL, p_grade); $$;

CREATE OR REPLACE FUNCTION public.grade_to_cat(p_grade text)
RETURNS integer
LANGUAGE sql
IMMUTABLE
AS $$ SELECT oee.grade_to_cat(p_grade); $$;

