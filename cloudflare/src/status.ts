import { HttpError, type AuthenticatedDevice } from "./auth";
import type { Env } from "./types";
import { budgetSnapshot } from "./usage";

export async function deviceStatus(env: Env, device: AuthenticatedDevice): Promise<object> {
  const windows = await env.DB.prepare("SELECT id,name,last_seen_at,revoked_at FROM devices WHERE kind='windows' ORDER BY created_at DESC LIMIT 1").first<any>();
  return { appVersion: env.APP_VERSION, caller: device, windows: windows ?? null, budget: await budgetSnapshot(env) };
}

export async function createStatusProbe(env: Env, device: AuthenticatedDevice): Promise<object> {
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  const windows = await env.DB.prepare("SELECT id FROM devices WHERE kind='windows' AND revoked_at IS NULL ORDER BY created_at DESC LIMIT 1").first<{ id: string }>();
  if (!windows) throw new HttpError(409, "No active Windows device", "windows_missing");
  const id = crypto.randomUUID();
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("INSERT INTO pc_commands(id,windows_device_id,kind,payload_json,state,created_at,expires_at) VALUES(?,?,?,?,?,?,?)")
    .bind(id, windows.id, "STATUS_PROBE", "{}", "pending", now, now + 30).run();
  return { probeId: id, expiresAt: now + 30 };
}

export async function probeStatus(env: Env, device: AuthenticatedDevice, probeId: string): Promise<object> {
  if (device.kind !== "android") throw new HttpError(403, "Android role required", "auth_role");
  const row = await env.DB.prepare("SELECT id,state,created_at,expires_at,acked_at FROM pc_commands WHERE id=? AND kind='STATUS_PROBE'").bind(probeId).first<any>();
  if (!row) throw new HttpError(404, "Probe not found", "probe_missing");
  const now = Math.floor(Date.now() / 1000);
  const state = row.state === "acked" ? "online" : row.expires_at < now ? "offline" : "checking";
  return { probeId, state, ackedAt: row.acked_at ?? null };
}

export async function pollPcCommands(env: Env, device: AuthenticatedDevice): Promise<object> {
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("UPDATE pc_commands SET state='expired' WHERE state='pending' AND expires_at < ?").bind(now).run();
  const rows = await env.DB.prepare("SELECT id,kind,payload_json,created_at,expires_at FROM pc_commands WHERE windows_device_id=? AND state='pending' ORDER BY created_at LIMIT 20")
    .bind(device.id).all<any>();
  return { commands: rows.results.map((r) => ({ id: r.id, kind: r.kind, payload: JSON.parse(r.payload_json), createdAt: r.created_at, expiresAt: r.expires_at })) };
}

export async function ackPcCommand(env: Env, device: AuthenticatedDevice, commandId: string): Promise<object> {
  if (device.kind !== "windows") throw new HttpError(403, "Windows role required", "auth_role");
  const now = Math.floor(Date.now() / 1000);
  const result = await env.DB.prepare("UPDATE pc_commands SET state='acked',acked_at=? WHERE id=? AND windows_device_id=? AND state='pending'")
    .bind(now, commandId, device.id).run();
  if (!result.meta.changes) throw new HttpError(404, "Pending command not found", "command_missing");
  return { commandId, state: "acked", ackedAt: now };
}
