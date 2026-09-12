CREATE TABLE devices (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK(kind IN ('android','windows')),
  name TEXT NOT NULL,
  signing_public_key_spki_b64 TEXT NOT NULL,
  encryption_public_key_spki_b64 TEXT,
  revoked_at INTEGER,
  last_seen_at INTEGER,
  created_at INTEGER NOT NULL
);

CREATE TABLE pairings (
  id TEXT PRIMARY KEY,
  code_hash TEXT NOT NULL,
  windows_device_id TEXT NOT NULL,
  windows_name TEXT NOT NULL,
  windows_signing_public_key_spki_b64 TEXT NOT NULL,
  windows_encryption_public_key_spki_b64 TEXT NOT NULL,
  android_device_id TEXT,
  status TEXT NOT NULL CHECK(status IN ('pending','confirmed','expired')),
  expires_at INTEGER NOT NULL,
  created_at INTEGER NOT NULL
);

CREATE TABLE request_nonces (
  device_id TEXT NOT NULL,
  nonce TEXT NOT NULL,
  expires_at INTEGER NOT NULL,
  PRIMARY KEY(device_id, nonce)
);

CREATE TABLE transfers (
  id TEXT PRIMARY KEY,
  android_device_id TEXT NOT NULL,
  windows_device_id TEXT NOT NULL,
  object_key TEXT NOT NULL UNIQUE,
  size_bytes INTEGER NOT NULL,
  encrypted_size_bytes INTEGER NOT NULL,
  part_size_bytes INTEGER NOT NULL,
  part_count INTEGER NOT NULL,
  plaintext_sha256 TEXT,
  wrapped_key_b64 TEXT NOT NULL,
  metadata_nonce_b64 TEXT NOT NULL,
  metadata_cipher_b64 TEXT NOT NULL,
  multipart_upload_id TEXT,
  state TEXT NOT NULL,
  priority INTEGER NOT NULL DEFAULT 1,
  error_code TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  r2_ready_at INTEGER,
  pc_saved_at INTEGER,
  delete_after INTEGER,
  completed_at INTEGER,
  canceled_at INTEGER
);

CREATE INDEX transfers_state_idx ON transfers(state, updated_at);
CREATE INDEX transfers_android_idx ON transfers(android_device_id, created_at DESC);
CREATE INDEX transfers_windows_idx ON transfers(windows_device_id, created_at DESC);

CREATE TABLE transfer_parts (
  transfer_id TEXT NOT NULL,
  part_number INTEGER NOT NULL,
  etag TEXT NOT NULL,
  encrypted_sha256 TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  completed_at INTEGER NOT NULL,
  PRIMARY KEY(transfer_id, part_number)
);

CREATE TABLE pc_commands (
  id TEXT PRIMARY KEY,
  windows_device_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  state TEXT NOT NULL CHECK(state IN ('pending','acked','expired')),
  created_at INTEGER NOT NULL,
  expires_at INTEGER NOT NULL,
  acked_at INTEGER
);

CREATE TABLE usage_daily (
  day TEXT PRIMARY KEY,
  peak_stored_bytes INTEGER NOT NULL DEFAULT 0,
  class_a_ops INTEGER NOT NULL DEFAULT 0,
  class_b_ops INTEGER NOT NULL DEFAULT 0,
  updated_at INTEGER NOT NULL
);
