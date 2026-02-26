-- OPTI-FRESH Sizer Data Collector: Authoritative Database Schema
-- This file creates all schemas, tables, sequences, indexes, constraints, and hypertables.
-- Run via 'db init'. Safe to re-run on existing databases (uses IF NOT EXISTS throughout).
-- Based on production database (sizer_metrics_staging) as of 2026-02-26.

-- ============================================================
-- Extensions
-- ============================================================

CREATE EXTENSION IF NOT EXISTS timescaledb;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================
-- Schemas
-- ============================================================

CREATE SCHEMA IF NOT EXISTS oee;

-- ============================================================
-- Sequences
-- ============================================================

CREATE SEQUENCE IF NOT EXISTS public.batches_id_seq
    START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

CREATE SEQUENCE IF NOT EXISTS public.machine_settings_id_seq
    AS integer START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

CREATE SEQUENCE IF NOT EXISTS oee.grade_impact_rules_id_seq
    AS integer START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

CREATE SEQUENCE IF NOT EXISTS oee.machine_discovery_snapshots_id_seq
    START WITH 1 INCREMENT BY 1 NO MINVALUE NO MAXVALUE CACHE 1;

-- ============================================================
-- Public schema tables
-- ============================================================

CREATE TABLE IF NOT EXISTS public.machines (
    serial_no   text NOT NULL PRIMARY KEY,
    name        text NOT NULL,
    inserted_at timestamptz DEFAULT now()
);

CREATE TABLE IF NOT EXISTS public.batches (
    id          bigint NOT NULL DEFAULT nextval('public.batches_id_seq') PRIMARY KEY,
    batch_id    integer NOT NULL,
    serial_no   text,
    grower_code text,
    start_ts    timestamptz NOT NULL,
    end_ts      timestamptz,
    comments    text,
    CONSTRAINT batches_run_key UNIQUE (serial_no, batch_id, comments, start_ts)
);

CREATE TABLE IF NOT EXISTS public.metrics (
    ts              timestamptz NOT NULL,
    serial_no       text NOT NULL,
    batch_id        integer,
    metric          text NOT NULL,
    value_json      jsonb NOT NULL,
    batch_record_id bigint NOT NULL,
    PRIMARY KEY (ts, serial_no, metric)
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

CREATE TABLE IF NOT EXISTS public.machine_settings (
    id                   integer NOT NULL DEFAULT nextval('public.machine_settings_id_seq') PRIMARY KEY,
    target_machine_speed double precision NOT NULL,
    lane_count           integer NOT NULL,
    target_percentage    double precision NOT NULL,
    recycle_outlet       integer NOT NULL,
    serial_no            text
);

-- ============================================================
-- OEE schema tables
-- ============================================================

CREATE TABLE IF NOT EXISTS oee.machine_thresholds (
    serial_no     text NOT NULL PRIMARY KEY,
    min_rpm       numeric NOT NULL,
    min_total_fpm numeric NOT NULL,
    updated_at    timestamptz DEFAULT now()
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
    PRIMARY KEY (machine_serial_no, effective_date, band_name),
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
    PRIMARY KEY (machine_serial_no, calculation_date, band_name)
);

CREATE TABLE IF NOT EXISTS oee.shift_calendar (
    break_start timestamptz NOT NULL,
    break_end   timestamptz NOT NULL,
    PRIMARY KEY (break_start, break_end)
);

CREATE TABLE IF NOT EXISTS oee.commissioning_status (
    serial_no                  text NOT NULL PRIMARY KEY,
    db_bootstrapped_at         timestamptz,
    sizer_connected_at         timestamptz,
    machine_discovered_at      timestamptz,
    grade_mapping_completed_at timestamptz,
    thresholds_set_at          timestamptz,
    ingestion_enabled_at       timestamptz,
    notes                      text,
    updated_at                 timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS oee.grade_map (
    serial_no   text NOT NULL,
    grade_key   text NOT NULL,
    desired_cat smallint NOT NULL,
    is_active   boolean NOT NULL DEFAULT true,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  text,
    PRIMARY KEY (serial_no, grade_key)
);

CREATE TABLE IF NOT EXISTS oee.machine_discovery_snapshots (
    id            bigint NOT NULL DEFAULT nextval('oee.machine_discovery_snapshots_id_seq') PRIMARY KEY,
    serial_no     text NOT NULL,
    discovered_at timestamptz NOT NULL DEFAULT now(),
    source_host   text,
    source_port   integer,
    client_kind   text NOT NULL DEFAULT 'wcf',
    success       boolean NOT NULL,
    duration_ms   integer,
    error_text    text,
    payload_json  jsonb NOT NULL,
    summary_json  jsonb
);

CREATE TABLE IF NOT EXISTS oee.grade_lane_anomalies (
    event_ts        timestamptz NOT NULL,
    serial_no       text NOT NULL,
    batch_record_id integer NOT NULL,
    lane_no         smallint NOT NULL,
    grade_key       text NOT NULL,
    qty             double precision NOT NULL,
    pct             double precision NOT NULL,
    anomaly_score   double precision NOT NULL,
    severity        text NOT NULL,
    explanation     jsonb,
    model_version   text NOT NULL,
    delivered_to    text NOT NULL
);

CREATE TABLE IF NOT EXISTS oee.etl_watermarks (
    key     text NOT NULL PRIMARY KEY,
    last_ts timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS oee.grade_impact_rules (
    id                integer NOT NULL DEFAULT nextval('oee.grade_impact_rules_id_seq') PRIMARY KEY,
    suffix_regex      text NOT NULL,
    impact_multiplier integer NOT NULL,
    description       text,
    enabled           boolean NOT NULL DEFAULT true,
    sort_order        integer NOT NULL DEFAULT 100
);

CREATE TABLE IF NOT EXISTS oee.lane_grade_events (
    ts              timestamptz NOT NULL,
    serial_no       text NOT NULL,
    batch_record_id bigint,
    lane_no         integer NOT NULL,
    grade_key       text NOT NULL,
    qty             double precision NOT NULL,
    PRIMARY KEY (ts, serial_no, lane_no, grade_key)
);

CREATE TABLE IF NOT EXISTS oee.lane_grade_minute (
    minute_ts       timestamptz NOT NULL,
    serial_no       text NOT NULL,
    batch_record_id integer NOT NULL,
    lane_no         bigint NOT NULL,
    grade_key       text NOT NULL,
    grade_name      text NOT NULL,
    qty             double precision NOT NULL,
    PRIMARY KEY (minute_ts, serial_no, batch_record_id, lane_no, grade_key)
);

CREATE TABLE IF NOT EXISTS oee.oee_minute_batch_old (
    minute_ts        timestamptz NOT NULL,
    serial_no        text NOT NULL,
    batch_record_id  bigint NOT NULL,
    availability_ratio double precision NOT NULL,
    throughput_ratio   double precision NOT NULL,
    quality_ratio      double precision NOT NULL,
    oee_score          double precision NOT NULL,
    lot              text,
    variety          text,
    PRIMARY KEY (minute_ts, serial_no, batch_record_id)
);

-- New config tables (not yet in production)
CREATE TABLE IF NOT EXISTS oee.quality_params (
    serial_no    text PRIMARY KEY,
    tgt_good     numeric NOT NULL DEFAULT 0.75,
    tgt_peddler  numeric NOT NULL DEFAULT 0.15,
    tgt_bad      numeric NOT NULL DEFAULT 0.05,
    tgt_recycle  numeric NOT NULL DEFAULT 0.05,
    w_good       numeric NOT NULL DEFAULT 0.40,
    w_peddler    numeric NOT NULL DEFAULT 0.20,
    w_bad        numeric NOT NULL DEFAULT 0.20,
    w_recycle    numeric NOT NULL DEFAULT 0.20,
    sig_k        numeric NOT NULL DEFAULT 4.0,
    updated_at   timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS oee.perf_params (
    serial_no            text PRIMARY KEY,
    min_effective_fpm    numeric NOT NULL DEFAULT 3,
    low_ratio_threshold  numeric NOT NULL DEFAULT 0.5,
    cap_asymptote        numeric NOT NULL DEFAULT 0.2,
    updated_at           timestamptz NOT NULL DEFAULT now()
);

-- ============================================================
-- Sequence ownership
-- ============================================================

ALTER SEQUENCE oee.grade_impact_rules_id_seq OWNED BY oee.grade_impact_rules.id;
ALTER SEQUENCE oee.machine_discovery_snapshots_id_seq OWNED BY oee.machine_discovery_snapshots.id;
ALTER SEQUENCE public.batches_id_seq OWNED BY public.batches.id;
ALTER SEQUENCE public.machine_settings_id_seq OWNED BY public.machine_settings.id;

-- ============================================================
-- Foreign keys
-- ============================================================

DO $$ BEGIN
    ALTER TABLE public.batches ADD CONSTRAINT batches_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE public.metrics ADD CONSTRAINT metrics_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE public.metrics ADD CONSTRAINT metrics_batch_record_id_fkey FOREIGN KEY (batch_record_id) REFERENCES public.batches(id);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

DO $$ BEGIN
    ALTER TABLE public.machine_settings ADD CONSTRAINT machine_settings_serial_no_fkey FOREIGN KEY (serial_no) REFERENCES public.machines(serial_no);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- ============================================================
-- Hypertables (TimescaleDB)
-- ============================================================

SELECT create_hypertable('public.metrics', 'ts', if_not_exists => TRUE);
SELECT create_hypertable('public.calculated_metrics', 'ts', if_not_exists => TRUE);
SELECT create_hypertable('oee.lane_grade_events', 'ts', if_not_exists => TRUE);
SELECT create_hypertable('oee.lane_grade_minute', 'minute_ts', if_not_exists => TRUE);
SELECT create_hypertable('oee.oee_minute_batch_old', 'minute_ts', if_not_exists => TRUE);

-- ============================================================
-- Indexes (on user tables only; chunk indexes are auto-created)
-- ============================================================

CREATE INDEX IF NOT EXISTS metrics_ts_idx ON public.metrics USING btree (ts DESC);
CREATE INDEX IF NOT EXISTS metrics_batch_idx ON public.metrics USING btree (batch_id);
CREATE INDEX IF NOT EXISTS metrics_batch_record_id_idx ON public.metrics USING btree (batch_record_id);
CREATE INDEX IF NOT EXISTS idx_metrics_batchrec_ts ON public.metrics USING btree (batch_record_id, ts);

CREATE INDEX IF NOT EXISTS calculated_metrics_ts_idx ON public.calculated_metrics USING btree (ts DESC);
CREATE INDEX IF NOT EXISTS idx_calcmetrics_batchrec_ts ON public.calculated_metrics USING btree (batch_record_id, ts);
CREATE INDEX IF NOT EXISTS idx_cm_metric_batch ON public.calculated_metrics USING btree (metric, batch_record_id);

CREATE INDEX IF NOT EXISTS batches_id_idx ON public.batches USING btree (id);
CREATE INDEX IF NOT EXISTS idx_batches_batch_comment_ts ON public.batches USING btree (batch_id, comments, start_ts);
CREATE INDEX IF NOT EXISTS idx_batches_start_ts ON public.batches USING btree (start_ts);
CREATE INDEX IF NOT EXISTS ix_batches_lot_variety_ci ON public.batches USING btree (grower_code, lower(comments), start_ts);

CREATE INDEX IF NOT EXISTS idx_band_definitions_active ON oee.band_definitions USING btree (machine_serial_no, is_active, effective_date DESC);
CREATE INDEX IF NOT EXISTS idx_band_statistics_date ON oee.band_statistics USING btree (calculation_date DESC);
CREATE INDEX IF NOT EXISTS ix_grade_map_serial ON oee.grade_map USING btree (serial_no);
CREATE INDEX IF NOT EXISTS ix_machine_discovery_snapshots_serial_ts ON oee.machine_discovery_snapshots USING btree (serial_no, discovered_at DESC);

CREATE INDEX IF NOT EXISTS grade_lane_anomalies_batch_record_id_idx ON oee.grade_lane_anomalies USING btree (batch_record_id);
CREATE INDEX IF NOT EXISTS grade_lane_anomalies_event_ts_idx ON oee.grade_lane_anomalies USING btree (event_ts DESC);
CREATE INDEX IF NOT EXISTS grade_lane_anomalies_event_ts_severity_idx ON oee.grade_lane_anomalies USING btree (event_ts DESC, severity);

CREATE INDEX IF NOT EXISTS lane_grade_events_ts_idx ON oee.lane_grade_events USING btree (ts DESC);
CREATE INDEX IF NOT EXISTS ix_lane_grade_events_batch_ts ON oee.lane_grade_events USING btree (batch_record_id, ts DESC);
CREATE INDEX IF NOT EXISTS ix_lane_grade_events_serial_ts ON oee.lane_grade_events USING btree (serial_no, ts DESC);

CREATE INDEX IF NOT EXISTS lane_grade_minute_minute_ts_idx ON oee.lane_grade_minute USING btree (minute_ts DESC);
CREATE INDEX IF NOT EXISTS ix_lane_grade_minute_batch ON oee.lane_grade_minute USING btree (batch_record_id, minute_ts DESC);

CREATE INDEX IF NOT EXISTS oee_minute_batch_minute_ts_idx ON oee.oee_minute_batch_old USING btree (minute_ts DESC);
CREATE INDEX IF NOT EXISTS ix_oee_minute_batch_batch_time ON oee.oee_minute_batch_old USING btree (batch_record_id, minute_ts DESC);
CREATE INDEX IF NOT EXISTS ix_oee_minute_batch_serial_time ON oee.oee_minute_batch_old USING btree (serial_no, minute_ts DESC);
