import { HttpError, type AuthenticatedDevice } from "./auth";
import { abortMultipart, completeMultipart, createMultipart, signGet, signSinglePut, signUploadPart } from "./r2";
import type { Env, TransferRow } from "./types";
import { budgetSnapshot, recordUsage } from "./usage";

const MAX_FILE_BYTES = 10_000_000_000;
const FRAME_OVERHEAD = 29;

function choosePartSize(size: number): number {
  const MiB = 1024 * 1024;
  if (size <= 1_000_000_000) return 64 * MiB;
  if (size <= 5_000_000_000) return 128 * MiB;
  return 256 * MiB;
}

function partCountFor(size: number, partSize: number): number {
  return size === 0 ? 1 : Math.ceil(size / partSize);
}

function transferTtlSeconds(encryptedSize: number): number {
  return Math.max(900, Math.min(86400, Math.ceil(encryptedSize / (1024 * 1024)) * 4 + 900));
}

async function loadTransfer(env: Env, id: string): Promise<TransferRow> {
  const row = await env.DB.prepare("SELECT * FROM transfers WHERE id = ?").bind(id).first<TransferRow>();
  if (!row) throw new HttpError(404, "Transfer not found", "transfer_missing");
  return row;
}

function assertOwner(row: TransferRow, device: AuthenticatedDevice): void {
  if (device.kind === "android" && row.android_device_id !== device.id) throw new HttpError(403, "Transfer belongs to another device", "transfer_owner");
  if (device.kind === "windows" && row.windows_device_id !== device.id) throw new HttpError(403, "Transfer belongs to another device", "transfer_owner");
}

export async function createTransfer(env: Env, device: AuthenticatedDevice, body: any): Promise<object> {
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  const size = Number(body?.sizeBytes);
  if (!Number.isInteger(size) || size < 0 || size > MAX_FILE_BYTES) throw new HttpError(400, "File size outside allowed range", "size_limit");
  if (typeof body?.plaintextSha256 !== "string" || !/^[0-9a-f]{64}$/i.test(body.plaintextSha256)) throw new HttpError(400, "Invalid SHA-256", "transfer_hash");
  if (typeof body?.wrappedKeyB64 !== "string" || typeof body?.metadataNonceB64 !== "string" || typeof body?.metadataCipherB64 !== "string") throw new HttpError(400, "Missing encrypted metadata", "transfer_metadata");
  const windows = await env.DB.prepare("SELECT id FROM devices WHERE kind='windows' AND revoked_at IS NULL ORDER BY created_at DESC LIMIT 1").first<{ id: string }>();
  if (!windows) throw new HttpError(409, "No active Windows device", "windows_missing");
  const partSize = size === 0 ? 0 : choosePartSize(size);
  const partCount = partCountFor(size, partSize);
  const encryptedSize = size + partCount * FRAME_OVERHEAD;
  const budget = await budgetSnapshot(env, encryptedSize, partCount + 2, 2);
  if (!budget.uploadAllowed) throw new HttpError(429, "Free-tier safety limit reached", "free_tier_stop");
  const id = crypto.randomUUID();
  const objectKey = `transfers/${id}.cbx`;
  const uploadId = size === 0 ? null : await createMultipart(env, objectKey);
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("INSERT INTO transfers(id,android_device_id,windows_device_id,object_key,size_bytes,encrypted_size_bytes,part_size_bytes,part_count,plaintext_sha256,wrapped_key_b64,metadata_nonce_b64,metadata_cipher_b64,multipart_upload_id,state,priority,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)")
    .bind(id, device.id, windows.id, objectKey, size, encryptedSize, partSize, partCount, body.plaintextSha256.toLowerCase(), body.wrappedKeyB64, body.metadataNonceB64, body.metadataCipherB64, uploadId, "UPLOADING", Number(body?.priority ?? 1), now, now).run();
  await recordUsage(env, uploadId ? 1 : 0, 0);
  return { id, state: "UPLOADING", sizeBytes: size, encryptedSizeBytes: encryptedSize, partSizeBytes: partSize, partCount, objectKey, multipart: uploadId !== null, budget };
}

export async function partUrls(env: Env, device: AuthenticatedDevice, id: string, body: any): Promise<object> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  if (row.state !== "UPLOADING" && row.state !== "PAUSED") throw new HttpError(409, "Transfer is not uploadable", "transfer_state");
  const requested = Array.isArray(body?.partNumbers) ? body.partNumbers.map(Number) : [];
  if (requested.length < 1 || requested.length > 16) throw new HttpError(400, "Request 1-16 part numbers", "parts_request");
  const expiresIn = 3600;
  const urls: Array<{ partNumber: number; url: string }> = [];
  for (const partNumber of requested) {
    if (!Number.isInteger(partNumber) || partNumber < 1 || partNumber > row.part_count) throw new HttpError(400, "Invalid part number", "part_number");
    const url = row.size_bytes === 0
      ? await signSinglePut(env, row.object_key, expiresIn)
      : await signUploadPart(env, row.object_key, row.multipart_upload_id!, partNumber, expiresIn);
    urls.push({ partNumber, url });
  }
  return { transferId: id, expiresIn, urls };
}

export async function reportPart(env: Env, device: AuthenticatedDevice, id: string, body: any): Promise<object> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  const partNumber = Number(body?.partNumber);
  const etag = String(body?.etag ?? "");
  const hash = String(body?.encryptedSha256 ?? "").toLowerCase();
  const size = Number(body?.sizeBytes);
  if (!Number.isInteger(partNumber) || partNumber < 1 || partNumber > row.part_count || !etag || !/^[0-9a-f]{64}$/.test(hash) || !Number.isInteger(size) || size < 0) throw new HttpError(400, "Invalid part report", "part_report");
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("INSERT INTO transfer_parts(transfer_id,part_number,etag,encrypted_sha256,size_bytes,completed_at) VALUES(?,?,?,?,?,?) ON CONFLICT(transfer_id,part_number) DO UPDATE SET etag=excluded.etag,encrypted_sha256=excluded.encrypted_sha256,size_bytes=excluded.size_bytes,completed_at=excluded.completed_at")
    .bind(id, partNumber, etag, hash, size, now).run();
  await recordUsage(env, 1, 0);
  return { transferId: id, partNumber, recorded: true };
}

export async function completeUpload(env: Env, device: AuthenticatedDevice, id: string): Promise<object> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  if (row.state !== "UPLOADING" && row.state !== "PAUSED") throw new HttpError(409, "Transfer cannot be completed from current state", "transfer_state");
  const now = Math.floor(Date.now() / 1000);
  if (row.size_bytes === 0) {
    const object = await env.FILES.head(row.object_key);
    if (!object) throw new HttpError(409, "Zero-byte object is not present", "r2_missing");
  } else {
    const parts = await env.DB.prepare("SELECT part_number,etag FROM transfer_parts WHERE transfer_id=? ORDER BY part_number").bind(id).all<{ part_number: number; etag: string }>();
    if (parts.results.length !== row.part_count) throw new HttpError(409, "Not all parts are reported", "parts_incomplete");
    await completeMultipart(env, row.object_key, row.multipart_upload_id!, parts.results.map((p) => ({ partNumber: Number(p.part_number), etag: p.etag })));
  }
  await env.DB.prepare("UPDATE transfers SET state='R2_READY',r2_ready_at=?,updated_at=? WHERE id=?").bind(now, now, id).run();
  await recordUsage(env, 1, row.size_bytes === 0 ? 1 : 0);
  return { transferId: id, state: "R2_READY" };
}

export async function pendingTransfers(env: Env, device: AuthenticatedDevice): Promise<object> {
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  const rows = await env.DB.prepare("SELECT * FROM transfers WHERE windows_device_id=? AND state IN ('R2_READY','PC_DOWNLOADING') ORDER BY priority DESC,created_at ASC LIMIT 50")
    .bind(device.id).all<TransferRow>();
  return { transfers: rows.results.map((r) => ({
    id: r.id,
    state: r.state,
    sizeBytes: r.size_bytes,
    encryptedSizeBytes: r.encrypted_size_bytes,
    partSizeBytes: r.part_size_bytes,
    partCount: r.part_count,
    wrappedKeyB64: r.wrapped_key_b64,
    metadataNonceB64: r.metadata_nonce_b64,
    metadataCipherB64: r.metadata_cipher_b64,
    priority: r.priority,
    createdAt: r.created_at,
  })) };
}

export async function downloadUrl(env: Env, device: AuthenticatedDevice, id: string): Promise<object> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  if (row.state !== "R2_READY" && row.state !== "PC_DOWNLOADING") throw new HttpError(409, "Transfer is not ready", "transfer_state");
  const expiresIn = transferTtlSeconds(row.encrypted_size_bytes);
  const url = await signGet(env, row.object_key, expiresIn);
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("UPDATE transfers SET state='PC_DOWNLOADING',updated_at=? WHERE id=?").bind(now, id).run();
  await recordUsage(env, 0, 1);
  return { transferId: id, url, expiresIn };
}
