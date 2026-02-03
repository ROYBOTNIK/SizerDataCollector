-- Serial-aware grade-to-category resolution with overrides and lane-stripping fallback.
-- Duplicates V011 logic at a later version to avoid ordering conflicts.

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

    -- 1) Override table first (per serial, active)
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

    -- 2) Strip lane prefix if first token is numeric: "7 Dark Cherry ..._GATE" -> "Dark Cherry ..._GATE"
    IF v_grade ~ '^[0-9]+\s+' THEN
        v_base := regexp_replace(v_grade, '^[0-9]+\s+', '');
    ELSE
        v_base := v_grade;
    END IF;

    -- 3) Pattern fallback (grade suffixes)
    RETURN CASE
        WHEN v_base ILIKE '%\_RCY%'      THEN 3  -- recycle
        WHEN v_base ILIKE '%\_RECYCLE%'  THEN 3
        WHEN v_base ILIKE '%\_GATE%'     THEN 3  -- gate/reject path
        WHEN v_base ILIKE '%\_REJ%'      THEN 3
        WHEN v_base ILIKE '%\_TEST%'     THEN 1
        WHEN v_base ILIKE '%\_D/S%'      THEN 1
        WHEN v_base ILIKE '%\_PEDDLER%'  THEN 1
        WHEN v_base ILIKE '%\_EXP%'      THEN 0  -- export/good
        WHEN v_base ILIKE '%\_DOM%'      THEN 1  -- domestic/peddler
        WHEN v_base ILIKE '%\_CULL%'     THEN 2  -- culls/bad
        WHEN v_base ILIKE '%\_GREEN%'    THEN 2
        WHEN v_base ILIKE '%\_NAF%'      THEN 3
        ELSE NULL
    END;
END;
$$;

-- Backward-compatible single-arg wrappers now delegate to the serial-aware function.
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

