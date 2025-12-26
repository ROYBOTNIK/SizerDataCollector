-- Make machine_settings serial-aware and refresh dependent objects
-- Adds serial_no, enforces uniqueness, makes functions serial-aware, and recreates throughput CAGGs.

-- 1) Schema change: add serial_no column (idempotent)
ALTER TABLE public.machine_settings
    ADD COLUMN IF NOT EXISTS serial_no text;

-- 2) Safe backfill: only when exactly one machine exists
DO $$
DECLARE
    machine_count integer;
    only_serial   text;
BEGIN
    SELECT COUNT(*) INTO machine_count FROM public.machines;
    IF machine_count = 1 THEN
        SELECT serial_no INTO only_serial FROM public.machines LIMIT 1;
        UPDATE public.machine_settings
           SET serial_no = only_serial
         WHERE serial_no IS NULL;
    END IF;
END$$;

-- 3) Enforce uniqueness for non-null serials
CREATE UNIQUE INDEX IF NOT EXISTS ux_machine_settings_serial
    ON public.machine_settings(serial_no)
    WHERE serial_no IS NOT NULL;

-- 4) Optional FK to machines (only if table exists and constraint missing)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'machines'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM pg_constraint
            WHERE conname = 'machine_settings_serial_no_fkey'
              AND conrelid = 'public.machine_settings'::regclass
        ) THEN
            ALTER TABLE public.machine_settings
                ADD CONSTRAINT machine_settings_serial_no_fkey
                FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);
        END IF;
    END IF;
END$$;


