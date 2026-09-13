import { HttpError, type AuthenticatedDevice } from "./auth";
import { randomCode6, sha256Hex } from "./crypto";
import type { DeviceRow, Env } from "./types";

interface PairingRow {
  id: string; code_hash: string; windows_device_id: string; windows_name: string;
  windows_signing_public_key_spki_b64: string; windows_encryption_public_key_spki_b64: string;
  android_device_id: string | null; status: string; expires_at: number;
}

interface PairingApprovalRow {
  id: string; payload_json: string; state: string; expires_at: number;
}

function validKey(value: unknown): value is string {
  return typeof value === "string" && value.length >= 200 && value.length <= 4096 && /^[A-Za-z0-9+/=]+$/.test(value);
}

async function issuePairing(request: Request, env: Env, win: { id: string; name: string; sign: string; enc: string }): Promise<object> {
  const now = Math.floor(Date.now() / 1000);
  const pairingId = crypto.randomUUID();
  const code = randomCode6();
  const expiresAt = now + 300;
  await env.DB.prepare("INSERT INTO pairings(id,code_hash,windows_device_id,windows_name,windows_signing_public_key_spki_b64,windows_encryption_public_key_spki_b64,status,expires_at,created_at) VALUES(?,?,?,?,?,?,?,?,?)")
    .bind(pairingId, await sha256Hex(`${pairingId}:${code}`), win.id, win.name.slice(0, 80), win.sign, win.enc, "pending", expiresAt, now).run();
  const origin = new URL(request.url).origin;
  return { pairingId, windowsDeviceId: win.id, code, expiresAt, qrPayload: { v: 1, apiBase: `${origin}/api/v1`, pairingId, windowsDeviceId: win.id, code, windowsSigningPublicKeySpkiB64: win.sign, windowsEncryptionPublicKeySpkiB64: win.enc } };
}

export async function pairingStart(request: Request, env: Env, body: any): Promise<object> {
  if ((request.headers.get("X-CB-Setup-Token") ?? "") !== env.PAIRING_SETUP_TOKEN) throw new HttpError(401, "Invalid setup token", "setup_token");
  if (!validKey(body?.signingPublicKeySpkiB64) || !validKey(body?.encryptionPublicKeySpkiB64)) throw new HttpError(400, "Invalid public key", "pairing_key");
  const count = await env.DB.prepare("SELECT COUNT(*) AS n FROM devices WHERE kind='windows' AND revoked_at IS NULL").first<{ n: number }>();
  if (Number(count?.n ?? 0) > 0) throw new HttpError(409, "Windows already paired", "windows_exists");
  return issuePairing(request, env, { id: crypto.randomUUID(), name: String(body?.name ?? "Windows PC"), sign: body.signingPublicKeySpkiB64, enc: body.encryptionPublicKeySpkiB64 });
}

export async function pairingStartForWindows(request: Request, env: Env, device: AuthenticatedDevice): Promise<object> {
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  const row = await env.DB.prepare("SELECT * FROM devices WHERE id=? AND kind='windows' AND revoked_at IS NULL").bind(device.id).first<DeviceRow>();
  if (!row?.encryption_public_key_spki_b64) throw new HttpError(409, "Windows key unavailable", "windows_key");
  return issuePairing(request, env, { id: row.id, name: row.name, sign: row.signing_public_key_spki_b64, enc: row.encryption_public_key_spki_b64 });
}

export async function pairingComplete(env: Env, body: any): Promise<object> {
  const pairingId = String(body?.pairingId ?? "");
  const code = String(body?.code ?? "");
  if (!validKey(body?.signingPublicKeySpkiB64)) throw new HttpError(400, "Invalid Android public key", "pairing_key");
  const row = await env.DB.prepare("SELECT * FROM pairings WHERE id=?").bind(pairingId).first<PairingRow>();
  const now = Math.floor(Date.now() / 1000);
  if (!row || row.status !== "pending" || row.expires_at < now) throw new HttpError(410, "Pairing expired", "pairing_expired");
  if ((await sha256Hex(`${pairingId}:${code}`)) !== row.code_hash) throw new HttpError(401, "Pairing code mismatch", "pairing_code");

  const existing = await env.DB.prepare("SELECT * FROM devices WHERE id=?").bind(row.windows_device_id).first<DeviceRow>();
  if (existing?.revoked_at) throw new HttpError(409, "Windows device unavailable", "windows_unavailable");

  const commandId = `pairing:${pairingId}`;
  const current = await env.DB.prepare("SELECT id,payload_json,state,expires_at FROM pc_commands WHERE id=? AND kind='pairing_approval'")
    .bind(commandId).first<PairingApprovalRow>();
  if (current?.state === "pending") {
    const claimed = JSON.parse(current.payload_json) as { androidDeviceId: string; name: string; signingPublicKeySpkiB64: string };
    if (claimed.signingPublicKeySpkiB64 !== body.signingPublicKeySpkiB64) throw new HttpError(409, "Pairing is already claimed by another Android device", "pairing_claimed");
    return { pairingId, status: "awaiting_approval", windowsDeviceId: row.windows_device_id, androidDeviceId: claimed.androidDeviceId };
  }

  const androidDeviceId = crypto.randomUUID();
  const payload = JSON.stringify({
    pairingId,
    androidDeviceId,
    name: String(body?.name ?? "Android").slice(0, 80),
    signingPublicKeySpkiB64: body.signingPublicKeySpkiB64,
  });
  await env.DB.batch([
    env.DB.prepare("UPDATE pairings SET android_device_id=? WHERE id=?").bind(androidDeviceId, pairingId),
    env.DB.prepare("INSERT INTO pc_commands(id,windows_device_id,kind,payload_json,state,created_at,expires_at) VALUES(?,?,?,?,?,?,?)")
      .bind(commandId, row.windows_device_id, "pairing_approval", payload, "pending", now, row.expires_at),
  ]);
  return { pairingId, status: "awaiting_approval", windowsDeviceId: row.windows_device_id, androidDeviceId };
}

export async function pairingApprove(request: Request, env: Env, body: any): Promise<object> {
  if ((request.headers.get("X-CB-Setup-Token") ?? "") !== env.PAIRING_SETUP_TOKEN) throw new HttpError(401, "Invalid setup token", "setup_token");
  const pairingId = String(body?.pairingId ?? "");
  const code = String(body?.code ?? "");
  const row = await env.DB.prepare("SELECT * FROM pairings WHERE id=?").bind(pairingId).first<PairingRow>();
  const now = Math.floor(Date.now() / 1000);
  if (!row) throw new HttpError(404, "Pairing not found", "pairing_missing");
  if ((await sha256Hex(`${pairingId}:${code}`)) !== row.code_hash) throw new HttpError(401, "Pairing code mismatch", "pairing_code");
  if (row.status === "confirmed") return { pairingId, status: "confirmed", windowsDeviceId: row.windows_device_id, androidDeviceId: row.android_device_id };
  if (row.status !== "pending" || row.expires_at < now) throw new HttpError(410, "Pairing expired", "pairing_expired");

  const commandId = `pairing:${pairingId}`;
  const approval = await env.DB.prepare("SELECT id,payload_json,state,expires_at FROM pc_commands WHERE id=? AND windows_device_id=? AND kind='pairing_approval'")
    .bind(commandId, row.windows_device_id).first<PairingApprovalRow>();
  if (!approval || approval.state !== "pending" || approval.expires_at < now) throw new HttpError(409, "Scan the QR code on Android before approving", "pairing_approval_pending");
  const payload = JSON.parse(approval.payload_json) as { androidDeviceId: string; name: string; signingPublicKeySpkiB64: string };
  if (!validKey(payload.signingPublicKeySpkiB64)) throw new HttpError(400, "Invalid Android public key", "pairing_key");

  const existing = await env.DB.prepare("SELECT * FROM devices WHERE id=?").bind(row.windows_device_id).first<DeviceRow>();
  if (existing?.revoked_at) throw new HttpError(409, "Windows device unavailable", "windows_unavailable");
  const statements = [];
  if (!existing) statements.push(env.DB.prepare("INSERT INTO devices(id,kind,name,signing_public_key_spki_b64,encryption_public_key_spki_b64,created_at) VALUES(?,?,?,?,?,?)")
    .bind(row.windows_device_id, "windows", row.windows_name, row.windows_signing_public_key_spki_b64, row.windows_encryption_public_key_spki_b64, now));
  statements.push(
    env.DB.prepare("INSERT INTO devices(id,kind,name,signing_public_key_spki_b64,encryption_public_key_spki_b64,created_at) VALUES(?,?,?,?,?,?)")
      .bind(payload.androidDeviceId, "android", payload.name, payload.signingPublicKeySpkiB64, null, now),
    env.DB.prepare("UPDATE pairings SET status='confirmed' WHERE id=?").bind(pairingId),
    env.DB.prepare("UPDATE pc_commands SET state='acked',acked_at=? WHERE id=?").bind(now, commandId),
  );
  await env.DB.batch(statements);
  return { pairingId, status: "confirmed", windowsDeviceId: row.windows_device_id, androidDeviceId: payload.androidDeviceId };
}

export async function pairingStatus(env: Env, pairingId: string, code: string): Promise<object> {
  const row = await env.DB.prepare("SELECT * FROM pairings WHERE id=?").bind(pairingId).first<PairingRow>();
  if (!row) throw new HttpError(404, "Pairing not found", "pairing_missing");
  if ((await sha256Hex(`${pairingId}:${code}`)) !== row.code_hash) throw new HttpError(401, "Pairing code mismatch", "pairing_code");
  const now = Math.floor(Date.now() / 1000);
  if (row.expires_at < now && row.status === "pending") {
    await env.DB.prepare("UPDATE pairings SET status='expired' WHERE id=?").bind(pairingId).run();
    return { pairingId, status: "expired" };
  }
  if (row.status === "pending") {
    const approval = await env.DB.prepare("SELECT id,payload_json,state,expires_at FROM pc_commands WHERE id=? AND kind='pairing_approval'")
      .bind(`pairing:${pairingId}`).first<PairingApprovalRow>();
    if (approval?.state === "pending" && approval.expires_at >= now) {
      const payload = JSON.parse(approval.payload_json) as { name?: string };
      return { pairingId, status: "awaiting_approval", windowsDeviceId: row.windows_device_id, androidDeviceId: row.android_device_id, androidName: payload.name ?? "Android" };
    }
  }
  return { pairingId, status: row.status, windowsDeviceId: row.windows_device_id, androidDeviceId: row.android_device_id };
}
