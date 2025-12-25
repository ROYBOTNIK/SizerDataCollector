-- Commissioning status persistence (idempotent)

CREATE TABLE IF NOT EXISTS oee.commissioning_status (
    serial_no text PRIMARY KEY,
    db_bootstrapped_at timestamptz NULL,
    sizer_connected_at timestamptz NULL,
    machine_discovered_at timestamptz NULL,
    grade_mapping_completed_at timestamptz NULL,
    thresholds_set_at timestamptz NULL,
    ingestion_enabled_at timestamptz NULL,
    notes text NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

