-- V017__minute_quality_view_qv1_old.sql
-- Restores legacy minute quality view required by daily_grade_components_qv1.

CREATE OR REPLACE VIEW public.minute_quality_view_qv1_old AS
WITH lane_grade AS (
    SELECT
        m.ts,
        g.key AS grade_name,
        g.value::double precision AS qty
    FROM public.metrics m
    CROSS JOIN LATERAL jsonb_array_elements(m.value_json) lane(lane_json)
    CROSS JOIN LATERAL jsonb_each_text(lane.lane_json) g(key, value)
    WHERE m.metric = 'lanes_grade_fpm'::text
),
cats AS (
    SELECT
        lane_grade.ts,
        CASE
            WHEN lane_grade.grade_name ~~ '%_Recycle'::text
              OR lane_grade.grade_name ~~ '%_NAF'::text
                THEN 3
            WHEN lane_grade.grade_name ~~ '%_Test'::text
              OR lane_grade.grade_name ~~ '%_D/S'::text
              OR lane_grade.grade_name ~~ '%_Peddler'::text
                THEN 1
            WHEN lane_grade.grade_name ~~ '%_E_'::text
              OR lane_grade.grade_name ~~ '%_D_'::text
                THEN 0
            WHEN lane_grade.grade_name ~~ '%_Green'::text
              OR lane_grade.grade_name ~~ '%_Cull'::text
                THEN 2
            ELSE 2
        END AS cat,
        lane_grade.qty
    FROM lane_grade
)
SELECT
    ts,
    sum(CASE WHEN cat = 0 THEN qty ELSE NULL::double precision END) AS good_qty,
    sum(CASE WHEN cat = 1 THEN qty ELSE NULL::double precision END) AS peddler_qty,
    sum(CASE WHEN cat = 2 THEN qty ELSE NULL::double precision END) AS bad_qty,
    sum(CASE WHEN cat = 3 THEN qty ELSE NULL::double precision END) AS recycle_qty,
    public.calc_quality_ratio_qv1(
        COALESCE(sum(CASE WHEN cat = 0 THEN qty ELSE NULL::double precision END), 0::double precision),
        COALESCE(sum(CASE WHEN cat = 1 THEN qty ELSE NULL::double precision END), 0::double precision),
        COALESCE(sum(CASE WHEN cat = 2 THEN qty ELSE NULL::double precision END), 0::double precision),
        COALESCE(sum(CASE WHEN cat = 3 THEN qty ELSE NULL::double precision END), 0::double precision)
    ) AS quality_ratio
FROM cats
GROUP BY ts;
