-- Ensure machine discovery timestamp is present on commissioning status
ALTER TABLE oee.commissioning_status
	ADD COLUMN IF NOT EXISTS machine_discovered_at timestamptz NULL;

