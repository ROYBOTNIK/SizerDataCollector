-- Deterministic, idempotent seed data (UPSERT style)
-- Source: reference/02_seed_data_20251221_015952.sql

-- oee.machine_thresholds
INSERT INTO oee.machine_thresholds (serial_no, min_rpm, min_total_fpm, updated_at)
VALUES ('140578', 1500, 1000, '2025-05-30 05:44:53.659635+12')
ON CONFLICT (serial_no) DO UPDATE
SET min_rpm = EXCLUDED.min_rpm,
    min_total_fpm = EXCLUDED.min_total_fpm,
    updated_at = EXCLUDED.updated_at;

-- public.machine_settings
INSERT INTO public.machine_settings (id, target_machine_speed, lane_count, target_percentage, recycle_outlet)
VALUES
    (1, 2050, 32, 85, 0),
    (2, 2050, 32, 85, 0)
ON CONFLICT (id) DO UPDATE
SET target_machine_speed = EXCLUDED.target_machine_speed,
    lane_count           = EXCLUDED.lane_count,
    target_percentage    = EXCLUDED.target_percentage,
    recycle_outlet       = EXCLUDED.recycle_outlet;

-- keep sequence aligned with the highest seeded id
SELECT setval('public.machine_settings_id_seq', (SELECT MAX(id) FROM public.machine_settings), true);

-- No seed rows were present in the reference dump for:
--   oee.band_definitions
--   oee.shift_calendar
--   oee.band_statistics
--   oee.grade_lane_anomalies
-- These tables are intentionally left empty by bootstrap.

