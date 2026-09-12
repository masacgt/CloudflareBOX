import { HttpError, type AuthenticatedDevice } from "./auth";
import type { Env } from "./types";

function requireWindows(device: AuthenticatedDevice): void {
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
}

export async function listDevices(env: Env, device: AuthenticatedDevice): Promise<object> {
  requireWindows(device);
  const rows = await env.DB.prepare("SELECT id,kind,name,revoked_at,last_seen_at,created_at FROM devices ORDER BY kind DESC,created_at ASC").all<{
    id: string; kind: string; name: string; revoked_at: number | null; last_seen_at: number | null; created_at: number;
  }>();
  return { devices: rows.results.map((r) => ({ id: r.id, kind: r.kind, name: r.name, revokedAt: r.revoked_at, lastSeenAt: r.last_seen_at, createdAt: r.created_at })) };
}

export async function revokeDevice(env: Env, device: AuthenticatedDevice, targetId: string): Promise<object> {
  requireWindows(device);
  const target = await env.DB.prepare("SELECT id,kind,revoked_at FROM devices WHERE id=?").bind(targetId).first<{ id: string; kind: string; revoked_at: number | null }>();
  if (!target) throw new HttpError(404, "Device not found", "device_missing");
  if (target.kind !== "android") throw new HttpError(409, "Only Android devices can be revoked here", "device_role");
  if (!target.revoked_at) await env.DB.prepare("UPDATE devices SET revoked_at=? WHERE id=?").bind(Math.floor(Date.now() / 1000), targetId).run();
  return { deviceId: targetId, revoked: true };
}

export async function diagnostics(env: Env, device: AuthenticatedDevice): Promise<object> {
  requireWindows(device);
  await env.DB.prepare("SELECT 1").first();
  await env.FILES.list({ limit: 1 });
  const counts = await env.DB.prepare("SELECT kind,COUNT(*) AS n FROM devices WHERE revoked_at IS NULL GROUP BY kind").all<{ kind: string; n: number }>();
  const activeDevices: Record<string, number> = {};
  for (const row of counts.results) activeDevices[row.kind] = Number(row.n);
  return { ok: true, version: env.APP_VERSION, d1: true, r2: true, activeDevices };
}
