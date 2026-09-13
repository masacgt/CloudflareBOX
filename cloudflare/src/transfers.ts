import { HttpError, type AuthenticatedDevice } from "./auth";
import { sha256Hex } from "./crypto";
import { completeMultipart, createMultipart, uploadMultipartPart } from "./r2";
import type { Env, TransferRow } from "./types";
import { budgetSnapshot, recordUsage } from "./usage";

const MAX_FILE_BYTES = 10_000_000_000;
const PART_SIZE = 8 * 1024 * 1024;
const FRAME_OVERHEAD = 29;

function partCountFor(size: number): number { return size === 0 ? 1 : Math.ceil(size / PART_SIZE); }

async function loadTransfer(env: Env, id: string): Promise<TransferRow> {
  const row = await env.DB.prepare("SELECT * FROM transfers WHERE id=?").bind(id).first<TransferRow>();
  if (!row) throw new HttpError(404, "Transfer not found", "transfer_missing");
  return row;
}

function assertOwner(row: TransferRow, device: AuthenticatedDevice): void {
  if (device.kind === "android" && row.android_device_id !== device.id) throw new HttpError(403, "Transfer belongs to another device", "transfer_owner");
  if (device.kind === "windows" && row.windows_device_id !== device.id) throw new HttpError(403, "Transfer belongs to another device", "transfer_owner");
}

function expectedEncryptedPartSize(row: TransferRow, partNumber: number): number {
  if (row.size_bytes === 0) return FRAME_OVERHEAD;
  const offset = (partNumber - 1) * row.part_size_bytes;
  const plain = Math.max(0, Math.min(row.part_size_bytes, row.size_bytes - offset));
  return plain + FRAME_OVERHEAD;
}

export async function createTransfer(env: Env, device: AuthenticatedDevice, body: any): Promise<object> {
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  const size = Number(body?.sizeBytes);
  if (!Number.isInteger(size) || size < 0 || size > MAX_FILE_BYTES) throw new HttpError(400, "File size outside allowed range", "size_limit");
  if (typeof body?.plaintextSha256 !== "string" || !/^[0-9a-f]{64}$/i.test(body.plaintextSha256)) throw new HttpError(400, "Invalid SHA-256", "transfer_hash");
  if (typeof body?.wrappedKeyB64 !== "string" || typeof body?.metadataNonceB64 !== "string" || typeof body?.metadataCipherB64 !== "string") throw new HttpError(400, "Missing encrypted metadata", "transfer_metadata");
  const windows = await env.DB.prepare("SELECT id FROM devices WHERE kind='windows' AND revoked_at IS NULL ORDER BY created_at DESC LIMIT 1").first<{ id: string }>();
  if (!windows) throw new HttpError(409, "No active Windows device", "windows_missing");

  const partCount = partCountFor(size);
  const partSize = size === 0 ? 0 : PART_SIZE;
  const encryptedSize = size + partCount * FRAME_OVERHEAD;
  const budget = await budgetSnapshot(env, encryptedSize, partCount + 2, 2);
  if (!budget.uploadAllowed) throw new HttpError(429, "Free-tier safety limit reached", "free_tier_stop");

  const id = crypto.randomUUID();
  const objectKey = `transfers/${id}.cbx`;
  const uploadId = size === 0 ? null : await createMultipart(env, objectKey);
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("INSERT INTO transfers(id,android_device_id,windows_device_id,object_key,size_bytes,encrypted_size_bytes,part_size_bytes,part_count,plaintext_sha256,wrapped_key_b64,metadata_nonce_b64,metadata_cipher_b64,multipart_upload_id,state,priority,created_at,updated_at) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)")
    .bind(id, device.id, windows.id, objectKey, size, encryptedSize, partSize, partCount, body.plaintextSha256.toLowerCase(), body.wrappedKeyB64, body.metadataNonceB64, body.metadataCipherB64, uploadId, "UPLOADING", Number(body?.priority ?? 1), now, now).run();
  if (uploadId) await recordUsage(env, 1, 0);
  return { id, state: "UPLOADING", sizeBytes: size, encryptedSizeBytes: encryptedSize, partSizeBytes: partSize, partCount, multipart: uploadId !== null, budget };
}

export async function uploadPart(env: Env, device: AuthenticatedDevice, id: string, partNumber: number, body: Uint8Array, claimedHash: string): Promise<object> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  if (row.state !== "UPLOADING" && row.state !== "PAUSED") throw new HttpError(409, "Transfer is not uploadable", "transfer_state");
  if (!Number.isInteger(partNumber) || partNumber < 1 || partNumber > row.part_count) throw new HttpError(400, "Invalid part number", "part_number");
  const expectedSize = expectedEncryptedPartSize(row, partNumber);
  if (body.byteLength !== expectedSize) throw new HttpError(400, "Encrypted part size mismatch", "part_size");
  const actualHash = await sha256Hex(body);
  if (!/^[0-9a-f]{64}$/i.test(claimedHash) || actualHash !== claimedHash.toLowerCase()) throw new HttpError(409, "Encrypted part hash mismatch", "part_hash");

  let etag: string;
  if (row.size_bytes === 0) {
    const result = await env.FILES.put(row.object_key, body);
    if (!result) throw new HttpError(500, "R2 object write did not return metadata", "r2_write_failed");
    etag = result.httpEtag;
  } else {
    if (!row.multipart_upload_id) throw new HttpError(409, "Multipart upload missing", "multipart_missing");
    etag = (await uploadMultipartPart(env, row.object_key, row.multipart_upload_id, partNumber, body)).etag;
  }
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("INSERT INTO transfer_parts(transfer_id,part_number,etag,encrypted_sha256,size_bytes,completed_at) VALUES(?,?,?,?,?,?) ON CONFLICT(transfer_id,part_number) DO UPDATE SET etag=excluded.etag,encrypted_sha256=excluded.encrypted_sha256,size_bytes=excluded.size_bytes,completed_at=excluded.completed_at")
    .bind(id, partNumber, etag, actualHash, body.byteLength, now).run();
  await recordUsage(env, 1, 0);
  return { transferId: id, partNumber, etag, encryptedSha256: actualHash, sizeBytes: body.byteLength };
}

export async function completeUpload(env: Env, device: AuthenticatedDevice, id: string): Promise<object> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  if (["R2_READY", "PC_DOWNLOADING", "VERIFYING", "DELETE_PENDING", "COMPLETE"].includes(row.state)) {
    return { transferId: id, state: row.state, idempotent: true };
  }
  if (row.state !== "UPLOADING" && row.state !== "PAUSED") throw new HttpError(409, "Transfer cannot be completed from current state", "transfer_state");
  const parts = await env.DB.prepare("SELECT part_number,etag FROM transfer_parts WHERE transfer_id=? ORDER BY part_number").bind(id).all<{ part_number: number; etag: string }>();
  if (parts.results.length !== row.part_count) throw new HttpError(409, "Not all parts are uploaded", "parts_incomplete");
  if (row.size_bytes === 0) {
    if (!(await env.FILES.head(row.object_key))) throw new HttpError(409, "Object is not present", "r2_missing");
    await recordUsage(env, 0, 1);
  } else {
    if (!row.multipart_upload_id) throw new HttpError(409, "Multipart upload missing", "multipart_missing");
    await completeMultipart(env, row.object_key, row.multipart_upload_id, parts.results.map((p) => ({ partNumber: Number(p.part_number), etag: p.etag })));
    await recordUsage(env, 1, 0);
  }
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("UPDATE transfers SET state='R2_READY',r2_ready_at=?,updated_at=? WHERE id=?").bind(now, now, id).run();
  return { transferId: id, state: "R2_READY" };
}

export async function pendingTransfers(env: Env, device: AuthenticatedDevice): Promise<object> {
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  const rows = await env.DB.prepare("SELECT * FROM transfers WHERE windows_device_id=? AND state IN ('R2_READY','PC_DOWNLOADING') ORDER BY priority DESC,created_at ASC LIMIT 50").bind(device.id).all<TransferRow>();
  return { transfers: rows.results.map((r) => ({ id: r.id, state: r.state, sizeBytes: r.size_bytes, encryptedSizeBytes: r.encrypted_size_bytes, partSizeBytes: r.part_size_bytes, partCount: r.part_count, wrappedKeyB64: r.wrapped_key_b64, metadataNonceB64: r.metadata_nonce_b64, metadataCipherB64: r.metadata_cipher_b64, priority: r.priority, createdAt: r.created_at })) };
}

export async function downloadContent(env: Env, device: AuthenticatedDevice, id: string, request: Request): Promise<Response> {
  const row = await loadTransfer(env, id);
  assertOwner(row, device);
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  if (row.state !== "R2_READY" && row.state !== "PC_DOWNLOADING") throw new HttpError(409, "Transfer is not ready", "transfer_state");
  const range = request.headers.get("range");
  let offset = 0;
  let status = 200;
  if (range) {
    const match = /^bytes=(\d+)-$/.exec(range.trim());
    if (!match) throw new HttpError(416, "Unsupported range", "range_invalid");
    offset = Number(match[1]);
    if (!Number.isSafeInteger(offset) || offset < 0 || offset >= row.encrypted_size_bytes) throw new HttpError(416, "Range outside object", "range_invalid");
    status = 206;
  }
  const object = await env.FILES.get(row.object_key, offset > 0 ? { range: { offset } } : undefined);
  if (!object) throw new HttpError(404, "R2 object missing", "r2_missing");
  const headers = new Headers({ "content-type": "application/octet-stream", "cache-control": "no-store", "accept-ranges": "bytes", "content-length": String(row.encrypted_size_bytes - offset), "etag": object.httpEtag });
  if (status === 206) headers.set("content-range", `bytes ${offset}-${row.encrypted_size_bytes - 1}/${row.encrypted_size_bytes}`);
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("UPDATE transfers SET state='PC_DOWNLOADING',updated_at=? WHERE id=?").bind(now, id).run();
  await recordUsage(env, 0, 1);
  return new Response(object.body, { status, headers });
}
