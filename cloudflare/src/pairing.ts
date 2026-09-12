import { HttpError, type AuthenticatedDevice } from "./auth";
import { randomCode6, sha256Hex } from "./crypto";
import type { DeviceRow, Env } from "./types";

interface PairingRow {
  id: string; code_hash: string; windows_device_id: string; windows_name: string;
  windows_signing_public_key_spki_b64: string; windows_encryption_public_key_spki_b64: string;
  android_device_id: string | null; status: string; expires_at: number;
}

function validKey(value: unknown): value is string {
  return typeof value === "string" && value.length >= 200 && value.length <= 4096 && /^[A-Za-z0-9+/=]+$/.test(value);
}

async function issuePairing(request: Request, env: Env, win: { id: string; name: string; sign: string; enc: string }): Promise<object> {
  const now = Math.floor(Date.now() / 1000);
  const pairingId = crypto.randomUUID();
  const code = randomCode6();
  const expiresAt = now + 600;
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
  const androidDeviceId = crypto.randomUUID();
  const statements = [];
  if (!existing) statements.push(env.DB.prepare("INSERT INTO devices(id,kind,name,signing_public_key_spki_b64,encryption_public_key_spki_b64,created_at) VALUES(?,?,?,?,?,?)").bind(row.windows_device_id, "windows", row.windows_name, row.windows_signing_public_key_spki_b64, row.windows_encryption_public_key_spki_b64, now));
  statements.push(
    env.DB.prepare("INSERT INTO devices(id,kind,name,signing_public_key_spki_b64,encryption_public_key_spki_b64,created_at) VALUES(?,?,?,?,?,?)").bind(androidDeviceId, "android", String(body?.name ?? "Android").slice(0, 80), body.signingPublicKeySpkiB64, null, now),
    env.DB.prepare("UPDATE pairings SET android_device_id=?,status='confirmed' WHERE id=?").bind(androidDeviceId, pairingId),
  );
  await env.DB.batch(statements);
  return { pairingId, status: "confirmed", windowsDeviceId: row.windows_device_id, androidDeviceId };
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
  return { pairingId, status: row.status, windowsDeviceId: row.windows_device_id, androidDeviceId: row.android_device_id };
}
