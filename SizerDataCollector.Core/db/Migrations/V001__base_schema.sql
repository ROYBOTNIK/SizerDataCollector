-- Base schema and tables for Opti-Fresh / SizerDataCollector
-- Includes schemas, tables, sequences, indexes, foreign keys, and hypertable creation.
-- Excludes TimescaleDB internal objects and continuous aggregates.

CREATE EXTENSION IF NOT EXISTS timescaledb;

CREATE SCHEMA IF NOT EXISTS oee;

-- Sequences
CREATE SEQUENCE IF NOT EXISTS public.batches_id_seq
    START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

CREATE SEQUENCE IF NOT EXISTS public.machine_settings_id_seq
    AS integer START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

-- Public tables
CREATE TABLE IF NOT EXISTS public.machines (
    serial_no   text PRIMARY KEY,
    name        text NOT NULL,
    inserted_at timestamptz DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.machine_settings (
    id                   integer PRIMARY KEY DEFAULT nextval('public.machine_settings_id_seq'),
    target_machine_speed double precision NOT NULL,
    lane_count           integer NOT NULL,
    target_percentage    double precision NOT NULL,
    recycle_outlet       integer NOT NULL
);

ALTER SEQUENCE public.machine_settings_id_seq OWNED BY public.machine_settings.id;

CREATE TABLE IF NOT EXISTS public.batches (
    batch_id   integer NOT NULL,
    serial_no  text,
    grower_code text,
    start_ts   timestamptz NOT NULL,
    end_ts     timestamptz,
    comments   text,
    id         bigint PRIMARY KEY DEFAULT nextval('public.batches_id_seq'),
    CONSTRAINT batches_run_key UNIQUE (serial_no, batch_id, comments, start_ts)
);

ALTER SEQUENCE public.batches_id_seq OWNED BY public.batches.id;

CREATE TABLE IF NOT EXISTS public.metrics (
    ts              timestamptz NOT NULL,
    serial_no       text NOT NULL,
    batch_id        integer,
    metric          text NOT NULL,
    value_json      jsonb NOT NULL,
    batch_record_id bigint NOT NULL,
    CONSTRAINT metrics_pkey PRIMARY KEY (ts, serial_no, metric)
);

CREATE TABLE IF NOT EXISTS public.calculated_metrics (
    ts              timestamptz NOT NULL,
    serial_no       text NOT NULL,
    batch_id        integer,
    shift_id        text,
    metric          text NOT NULL,
    value           double precision NOT NULL,
    batch_record_id bigint NOT NULL
);

-- OEE schema tables
CREATE TABLE IF NOT EXISTS oee.machine_thresholds (
    serial_no      text PRIMARY KEY,
    min_rpm        numeric NOT NULL,
    min_total_fpm  numeric NOT NULL,
    updated_at     timestamptz DEFAULT now()
);

CREATE TABLE IF NOT EXISTS oee.band_definitions (
    machine_serial_no text NOT NULL,
    effective_date    date NOT NULL,
    band_name         text NOT NULL,
    lower_bound       numeric(5,4) NOT NULL,
    upper_bound       numeric(5,4) NOT NULL,
    created_at        timestamptz DEFAULT now(),
    created_by        text DEFAULT 'system',
    is_active         boolean DEFAULT true,
    CONSTRAINT band_definitions_pkey PRIMARY KEY (machine_serial_no, effective_date, band_name),
    CONSTRAINT band_definitions_check CHECK (upper_bound >= 0 AND upper_bound <= 1 AND upper_bound > lower_bound),
    CONSTRAINT band_definitions_lower_bound_check CHECK (lower_bound >= 0 AND lower_bound <= 1)
);

CREATE TABLE IF NOT EXISTS oee.band_statistics (
    machine_serial_no text NOT NULL,
    calculation_date  date NOT NULL,
    band_name         text NOT NULL,
    avg_availability  numeric(5,4),
    avg_performance   numeric(5,4),
    avg_quality       numeric(5,4),
    avg_oee           numeric(5,4),
    minute_count      integer,
    created_at        timestamptz DEFAULT now(),
    CONSTRAINT band_statistics_pkey PRIMARY KEY (machine_serial_no, calculation_date, band_name)
);

CREATE TABLE IF NOT EXISTS oee.grade_lane_anomalies (
    event_ts      timestamptz NOT NULL,
    serial_no     text NOT NULL,
    batch_record_id integer NOT NULL,
    lane_no       smallint NOT NULL,
    grade_key     text NOT NULL,
    qty           double precision NOT NULL,
    pct           double precision NOT NULL,
    anomaly_score double precision NOT NULL,
    severity      text NOT NULL,
    explanation   jsonb,
    model_version text NOT NULL,
    delivered_to  text NOT NULL
);

CREATE TABLE IF NOT EXISTS oee.shift_calendar (
    break_start timestamptz NOT NULL,
    break_end   timestamptz NOT NULL,
    CONSTRAINT shift_calendar_pkey PRIMARY KEY (break_start, break_end)
);

-- Foreign keys
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'batches_serial_no_fkey' AND conrelid = 'public.batches'::regclass
    ) THEN
        ALTER TABLE public.batches
            ADD CONSTRAINT batches_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'calculated_metrics_batch_record_id_fkey' AND conrelid = 'public.calculated_metrics'::regclass
    ) THEN
        ALTER TABLE public.calculated_metrics
            ADD CONSTRAINT calculated_metrics_batch_record_id_fkey FOREIGN KEY (batch_record_id) REFERENCES public.batches(id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'metrics_batch_record_id_fkey' AND conrelid = 'public.metrics'::regclass
    ) THEN
        ALTER TABLE public.metrics
            ADD CONSTRAINT metrics_batch_record_id_fkey FOREIGN KEY (batch_record_id) REFERENCES public.batches(id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'metrics_serial_no_fkey' AND conrelid = 'public.metrics'::regclass
    ) THEN
        ALTER TABLE public.metrics
            ADD CONSTRAINT metrics_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);
    END IF;
END;
$$;

-- Indexes
CREATE INDEX IF NOT EXISTS grade_lane_anomalies_batch_record_id_idx ON oee.grade_lane_anomalies (batch_record_id);
CREATE INDEX IF NOT EXISTS grade_lane_anomalies_event_ts_idx ON oee.grade_lane_anomalies (event_ts DESC);
CREATE INDEX IF NOT EXISTS grade_lane_anomalies_event_ts_severity_idx ON oee.grade_lane_anomalies (event_ts DESC, severity);

CREATE INDEX IF NOT EXISTS idx_band_definitions_active ON oee.band_definitions (machine_serial_no, is_active, effective_date DESC);
CREATE INDEX IF NOT EXISTS idx_band_statistics_date ON oee.band_statistics (calculation_date DESC);

CREATE INDEX IF NOT EXISTS idx_batches_batch_comment_ts ON public.batches (batch_id, comments, start_ts);
CREATE INDEX IF NOT EXISTS idx_batches_start_ts ON public.batches (start_ts);
CREATE INDEX IF NOT EXISTS batches_id_idx ON public.batches (id);

CREATE INDEX IF NOT EXISTS calculated_metrics_ts_idx ON public.calculated_metrics (ts DESC);
CREATE INDEX IF NOT EXISTS idx_calcmetrics_batchrec_ts ON public.calculated_metrics (batch_record_id, ts);
CREATE INDEX IF NOT EXISTS idx_cm_metric_batch ON public.calculated_metrics (metric, batch_record_id);

CREATE INDEX IF NOT EXISTS idx_metrics_batchrec_ts ON public.metrics (batch_record_id, ts);
CREATE INDEX IF NOT EXISTS metrics_batch_idx ON public.metrics (batch_id);
CREATE INDEX IF NOT EXISTS metrics_batch_record_id_idx ON public.metrics (batch_record_id);
CREATE INDEX IF NOT EXISTS metrics_ts_idx ON public.metrics (ts DESC);

-- Hypertables
SELECT public.create_hypertable('public.metrics', 'ts', if_not_exists => TRUE);
SELECT public.create_hypertable('public.calculated_metrics', 'ts', if_not_exists => TRUE);

