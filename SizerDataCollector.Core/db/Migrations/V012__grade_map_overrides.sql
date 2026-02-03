-- Grade override mapping per machine (serial-aware grade_to_cat)

-- 1) Override table
CREATE TABLE IF NOT EXISTS oee.grade_map
(
    serial_no   text        NOT NULL,
    grade_key   text        NOT NULL,
    desired_cat smallint    NOT NULL,
    is_active   boolean     NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  text        NULL,
    CONSTRAINT grade_map_pkey PRIMARY KEY (serial_no, grade_key)
);

CREATE INDEX IF NOT EXISTS ix_grade_map_serial ON oee.grade_map (serial_no);

-- 2) Serial-aware grade categorization
CREATE OR REPLACE FUNCTION oee.grade_to_cat(p_serial_no text, p_grade text) RETURNS integer
    LANGUAGE sql STABLE
AS $$
SELECT COALESCE(
    (
        SELECT gm.desired_cat
        FROM oee.grade_map gm
        WHERE gm.serial_no = p_serial_no
          AND gm.grade_key = p_grade
          AND gm.is_active = true
        LIMIT 1
    ),
    (
        SELECT CASE
            WHEN p_grade LIKE '%\_Recycle' ESCAPE '\' OR p_grade LIKE '%\_NAF'       THEN 3
            WHEN p_grade LIKE '%\_Test'    ESCAPE '\' OR p_grade LIKE '%\_D/S'
                 OR   p_grade LIKE '%\_Peddler'                                     THEN 1
            WHEN p_grade LIKE '%\_E%' ESCAPE '\'  OR p_grade LIKE '%\_D%'           THEN 0
            WHEN p_grade LIKE '%\_Green'  ESCAPE '\' OR p_grade LIKE '%\_Cull'      THEN 2
            ELSE 2 END
    )
);
$$;

-- 3) Backward-compatible legacy wrapper (no serial; keeps legacy behaviour)
CREATE OR REPLACE FUNCTION oee.grade_to_cat(p_grade text) RETURNS integer
    LANGUAGE sql STABLE
AS $$ SELECT (
    SELECT CASE
        WHEN p_grade LIKE '%\_Recycle' ESCAPE '\' OR p_grade LIKE '%\_NAF'       THEN 3
        WHEN p_grade LIKE '%\_Test'    ESCAPE '\' OR p_grade LIKE '%\_D/S'
             OR   p_grade LIKE '%\_Peddler'                                     THEN 1
        WHEN p_grade LIKE '%\_E%' ESCAPE '\'  OR p_grade LIKE '%\_D%'           THEN 0
        WHEN p_grade LIKE '%\_Green'  ESCAPE '\' OR p_grade LIKE '%\_Cull'      THEN 2
        ELSE 2 END
); $$;

