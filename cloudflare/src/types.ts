export interface Env {
  DB: D1Database;
  FILES: R2Bucket;
  APP_VERSION: string;
  R2_BUCKET_NAME: string;
  R2_ACCOUNT_ID: string;
  R2_ACCESS_KEY_ID: string;
  R2_SECRET_ACCESS_KEY: string;
  PAIRING_SETUP_TOKEN: string;
  FREE_STORAGE_GB_MONTH: string;
  FREE_CLASS_A: string;
  FREE_CLASS_B: string;
  FREE_STOP_RATIO: string;
  ADMIN_EMAIL: string;
}

export type DeviceKind = "android" | "windows";
export type TransferState = "PREPARING" | "UPLOADING" | "PAUSED" | "R2_READY" | "PC_DOWNLOADING" | "VERIFYING" | "DELETE_PENDING" | "CANCELING" | "COMPLETE" | "FAILED" | "CANCELED";

export interface DeviceRow {
  id: string;
  kind: DeviceKind;
  name: string;
  signing_public_key_spki_b64: string;
  encryption_public_key_spki_b64: string | null;
  revoked_at: number | null;
  last_seen_at: number | null;
  created_at: number;
}

export interface TransferRow {
  id: string;
  android_device_id: string;
  windows_device_id: string;
  object_key: string;
  size_bytes: number;
  encrypted_size_bytes: number;
  part_size_bytes: number;
  part_count: number;
  plaintext_sha256: string | null;
  wrapped_key_b64: string;
  metadata_nonce_b64: string;
  metadata_cipher_b64: string;
  multipart_upload_id: string | null;
  state: TransferState;
  priority: number;
  error_code: string | null;
  created_at: number;
  updated_at: number;
  r2_ready_at: number | null;
  pc_saved_at: number | null;
  delete_after: number | null;
  completed_at: number | null;
  canceled_at: number | null;
}
