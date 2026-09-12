import { HttpError } from "./auth";
import { randomCode6, sha256Hex } from "./crypto";
import type { Env } from "./types";

interface PairingRow {
  id: string;
  code_hash: string;
  windows_device_id: string;
  windows_name: string;
  windows_signing_public_key_spki_b64: string;
  windows_encryption_public_key_spki_b64: string;
  android_device_id: string | null;
  status: string;
  expires_at: number;
  created_at: number;
}

function validB64Key(value: unknown): value is string {
  return typeof value === "string" && value.length >= 200 && value.length <= 4096 && /^[A-Za-z0-9+/=]+$/.test(value);
}

export async function pairingStart(request: Request, env: Env, body: any): Promise<object> {
  if ((request.headers.get("X-CB-Setup-Token") ?? "") !== env.PAIRING_SETUP_TOKEN) throw new HttpError(401, "Invalid setup token", "setup_token");
  if (!validB64Key(body?.signingPublicKeySpkiB64) || !validB64Key(body?.encryptionPublicKeySpkiB64)) throw new HttpError(400, "Invalid public key", "pairing_key");
  const now = Math.floor(Date.now() / 1000);
  const pairingId = crypto.randomUUID();
  const windowsDeviceId = crypto.randomUUID();
  const code = randomCode6();
  const codeHash = await sha256Hex(`${pairingId}:${code}`);
  const expiresAt = now + 600;
  const windowsName = String(body?.name ?? "Windows PC").slice(0, 80);
  await env.DB.prepare("INSERT INTO pairings(id,code_hash,windows_device_id,windows_name,windows_signing_public_key_spki_b64,windows_encryption_public_key_spki_b64,status,expires_at,created_at) VALUES(?,?,?,?,?,?,?,?,?)")
    .bind(pairingId, codeHash, windowsDeviceId, windowsName, body.signingPublicKeySpkiB64, body.encryptionPublicKeySpkiB64, "pending", expiresAt, now).run();
  const origin = new URL(request.url).origin;
  return {
    pairingId,
    windowsDeviceId,
    code,
    expiresAt,
    qrPayload: {
      v: 1,
      apiBase: `${origin}/api/v1`,
      pairingId,
      windowsDeviceId,
      code,
      windowsSigningPublicKeySpkiB64: body.signingPublicKeySpkiB64,
      windowsEncryptionPublicKeySpkiB64: body.encryptionPublicKeySpkiB64,
    },
  };
}

export async function pairingComplete(env: Env, body: any): Promise<object> {
  const pairingId = String(body?.pairingId ?? "");
  const code = String(body?.code ?? "");
  if (!validB64Key(body?.signingPublicKeySpkiB64)) throw new HttpError(400, "Invalid Android public key", "pairing_key");
  const row = await env.DB.prepare("SELECT * FROM pairings WHERE id = ?").bind(pairingId).first<PairingRow>();
  const now = Math.floor(Date.now() / 1000);
  if (!row || row.status !== "pending" || row.expires_at < now) throw new HttpError(410, "Pairing expired", "pairing_expired");
  if ((await sha256Hex(`${pairingId}:${code}`)) !== row.code_hash) throw new HttpError(401, "Pairing code mismatch", "pairing_code");
  const existing = await env.DB.prepare("SELECT COUNT(*) AS n FROM devices WHERE revoked_at IS NULL").first<{ n: number }>();
  if (Number(existing?.n ?? 0) > 0) throw new HttpError(409, "An active device pair already exists", "pairing_exists");
  const androidDeviceId = crypto.randomUUID();
  const androidName = String(body?.name ?? "Android").slice(0, 80);
  await env.DB.batch([
    env.DB.prepare("INSERT INTO devices(id,kind,name,signing_public_key_spki_b64,encryption_public_key_spki_b64,created_at) VALUES(?,?,?,?,?,?)")
      .bind(row.windows_device_id, "windows", row.windows_name, row.windows_signing_public_key_spki_b64, row.windows_encryption_public_key_spki_b64, now),
    env.DB.prepare("INSERT INTO devices(id,kind,name,signing_public_key_spki_b64,encryption_public_key_spki_b64,created_at) VALUES(?,?,?,?,?,?)")
      .bind(androidDeviceId, "android", androidName, body.signingPublicKeySpkiB64, null, now),
    env.DB.prepare("UPDATE pairings SET android_device_id = ?, status = 'confirmed' WHERE id = ?").bind(androidDeviceId, pairingId),
  ]);
  return { pairingId, status: "confirmed", windowsDeviceId: row.windows_device_id, androidDeviceId };
}

export async function pairingStatus(env: Env, pairingId: string, code: string): Promise<object> {
  const row = await env.DB.prepare("SELECT * FROM pairings WHERE id = ?").bind(pairingId).first<PairingRow>();
  if (!row) throw new HttpError(404, "Pairing not found", "pairing_missing");
  if ((await sha256Hex(`${pairingId}:${code}`)) !== row.code_hash) throw new HttpError(401, "Pairing code mismatch", "pairing_code");
  const now = Math.floor(Date.now() / 1000);
  if (row.expires_at < now && row.status === "pending") {
    await env.DB.prepare("UPDATE pairings SET status = 'expired' WHERE id = ?").bind(pairingId).run();
    return { pairingId, status: "expired" };
  }
  return { pairingId, status: row.status, windowsDeviceId: row.windows_device_id, androidDeviceId: row.android_device_id };
}
