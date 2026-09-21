import type { DeviceKind, DeviceRow, Env } from "./types";
import { b64ToBytes, canonicalRequest, sha256Hex } from "./crypto";

export class HttpError extends Error {
  constructor(public status: number, message: string, public code = "error") {
    super(message);
  }
}

export interface AuthenticatedDevice {
  id: string;
  kind: DeviceKind;
  name: string;
}

export async function authenticateDevice(request: Request, env: Env, body: Uint8Array, requiredKind?: DeviceKind, bodyHashOverride?: string): Promise<AuthenticatedDevice> {
  const deviceId = request.headers.get("X-CB-Device-Id") ?? "";
  const timestamp = request.headers.get("X-CB-Timestamp") ?? "";
  const nonce = request.headers.get("X-CB-Nonce") ?? "";
  const signatureB64 = request.headers.get("X-CB-Signature") ?? "";
  if (!deviceId || !timestamp || !nonce || !signatureB64) throw new HttpError(401, "Missing device signature", "auth_missing");

  const ts = Number(timestamp);
  const now = Math.floor(Date.now() / 1000);
  if (!Number.isFinite(ts) || Math.abs(now - ts) > 300) throw new HttpError(401, "Timestamp outside allowed window", "auth_time");
  if (!/^[0-9a-fA-F-]{16,80}$/.test(nonce)) throw new HttpError(401, "Invalid nonce", "auth_nonce");

  const device = await env.DB.prepare("SELECT * FROM devices WHERE id = ?").bind(deviceId).first<DeviceRow>();
  if (!device || device.revoked_at) throw new HttpError(401, "Unknown or revoked device", "auth_device");
  if (requiredKind && device.kind !== requiredKind) throw new HttpError(403, "Wrong device role", "auth_role");

  const url = new URL(request.url);
  const pathAndQuery = url.pathname + url.search;
  const bodyHash = bodyHashOverride ?? await sha256Hex(body);
  if (!/^[0-9a-f]{64}$/i.test(bodyHash)) throw new HttpError(401, "Invalid body hash", "auth_body_hash");
  const message = canonicalRequest(request.method, pathAndQuery, bodyHash, timestamp, nonce);
  const publicKey = await crypto.subtle.importKey(
    "spki",
    b64ToBytes(device.signing_public_key_spki_b64),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["verify"],
  );
  const ok = await crypto.subtle.verify("RSASSA-PKCS1-v1_5", publicKey, b64ToBytes(signatureB64), message);
  if (!ok) throw new HttpError(401, "Invalid signature", "auth_signature");

  const isReadOnlyRequest = request.method === "GET" || request.method === "HEAD";
  if (!isReadOnlyRequest) {
    try {
      await env.DB.prepare("INSERT INTO request_nonces(device_id, nonce, expires_at) VALUES(?,?,?)")
        .bind(deviceId, nonce, now + 600).run();
    } catch {
      throw new HttpError(409, "Nonce already used", "replay");
    }
  }

  if (device.last_seen_at === null || device.last_seen_at < now - 30 * 60) {
    await env.DB.prepare("UPDATE devices SET last_seen_at = ? WHERE id = ?").bind(now, deviceId).run();
  }
  return { id: device.id, kind: device.kind, name: device.name };
}
