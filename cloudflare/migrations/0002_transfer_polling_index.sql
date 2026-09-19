CREATE INDEX IF NOT EXISTS transfers_windows_state_created_idx ON transfers(windows_device_id, state, created_at);
