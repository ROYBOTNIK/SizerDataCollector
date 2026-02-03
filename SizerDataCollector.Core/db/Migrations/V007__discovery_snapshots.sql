-- Discovery snapshot storage (append-only)
CREATE SCHEMA IF NOT EXISTS oee;

CREATE TABLE IF NOT EXISTS oee.machine_discovery_snapshots
(
    id            BIGSERIAL PRIMARY KEY,
    serial_no     TEXT        NOT NULL,
    discovered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    source_host   TEXT        NULL,
    source_port   INT         NULL,
    client_kind   TEXT        NOT NULL DEFAULT 'wcf',
    success       BOOLEAN     NOT NULL,
    duration_ms   INT         NULL,
    error_text    TEXT        NULL,
    payload_json  JSONB       NOT NULL,
    summary_json  JSONB       NULL
);

CREATE INDEX IF NOT EXISTS ix_machine_discovery_snapshots_serial_ts
    ON oee.machine_discovery_snapshots (serial_no, discovered_at DESC);

-- Optional GIN index for payload search (skip if not required)
-- CREATE INDEX IF NOT EXISTS ix_machine_discovery_snapshots_payload_gin
--     ON oee.machine_discovery_snapshots USING GIN (payload_json);

-- Note: No FK to public.machines to avoid blocking discovery when the machine row is absent.

