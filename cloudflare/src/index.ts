import { adminPage } from "./admin";
import { authenticateDevice, HttpError } from "./auth";
import { cancelTransfer, confirmPcSave } from "./completion";
import { diagnostics, listDevices, revokeDevice } from "./devices";
import { dailyMaintenance, scheduledMaintenance } from "./maintenance";
import { pairingApprove, pairingComplete, pairingStart, pairingStartForWindows, pairingStatus } from "./pairing";
import { ackPcCommand, createStatusProbe, deviceStatus, pollPcCommands, probeStatus } from "./status";
import { completeUpload, createTransfer, downloadContent, getTransferStatus, pendingTransfers, uploadPart } from "./transfers";
import type { Env } from "./types";

function json(data: unknown, status = 200): Response {
  return Response.json(data, { status, headers: { "cache-control": "no-store" } });
}

function decodeJson(body: Uint8Array): any {
  if (!body.length) return {};
  try { return JSON.parse(new TextDecoder().decode(body)); }
  catch { throw new HttpError(400, "Invalid JSON", "json_invalid"); }
}

async function requestBody(request: Request): Promise<Uint8Array> {
  if (request.method === "GET" || request.method === "HEAD") return new Uint8Array();
  return new Uint8Array(await request.arrayBuffer());
}

async function handleFetch(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url);
  if (url.pathname === "/health") return json({ ok: true, version: env.APP_VERSION });
  if (url.pathname === "/admin" || url.pathname.startsWith("/admin/")) return adminPage(request, env);

  const body = await requestBody(request);
  if (url.pathname === "/api/v1/pairing/start" && request.method === "POST") return json(await pairingStart(request, env, decodeJson(body)), 201);
  if (url.pathname === "/api/v1/pairing/complete" && request.method === "POST") return json(await pairingComplete(env, decodeJson(body)), 202);
  if (url.pathname === "/api/v1/pairing/approve" && request.method === "POST") return json(await pairingApprove(request, env, decodeJson(body)));
  if (url.pathname === "/api/v1/pairing/status" && request.method === "GET") return json(await pairingStatus(env, url.searchParams.get("id") ?? "", url.searchParams.get("code") ?? ""));

  const partMatch = url.pathname.match(/^\/api\/v1\/transfers\/([^/]+)\/parts\/(\d+)$/);
  const bodyHashOverride = request.method === "PUT"
    ? request.headers.get("X-CB-Part-Sha256") ?? undefined
    : undefined;
  const device = await authenticateDevice(request, env, body, undefined, bodyHashOverride);
  if (partMatch && request.method === "PUT") {
    return json(await uploadPart(env, device, partMatch[1], Number(partMatch[2]), body, request.headers.get("X-CB-Part-Sha256") ?? ""));
  }
  const statusMatch = url.pathname.match(/^\/api\/v1\/transfers\/([^/]+)\/status$/);
  if (statusMatch && request.method === "GET") return json(await getTransferStatus(env, device, statusMatch[1]));

  const contentMatch = url.pathname.match(/^\/api\/v1\/transfers\/([^/]+)\/content$/);
  if (contentMatch && request.method === "GET") return await downloadContent(env, device, contentMatch[1], request);

  const data = decodeJson(body);
  if (url.pathname === "/api/v1/status" && request.method === "GET") return json(await deviceStatus(env, device));
  if (url.pathname === "/api/v1/status/probe" && request.method === "POST") return json(await createStatusProbe(env, device), 201);
  const probeMatch = url.pathname.match(/^\/api\/v1\/status\/probe\/([^/]+)$/);
  if (probeMatch && request.method === "GET") return json(await probeStatus(env, device, probeMatch[1]));
  if (url.pathname === "/api/v1/pc/commands/poll" && request.method === "POST") return json(await pollPcCommands(env, device));
  const ackMatch = url.pathname.match(/^\/api\/v1\/pc\/commands\/([^/]+)\/ack$/);
  if (ackMatch && request.method === "POST") return json(await ackPcCommand(env, device, ackMatch[1]));

  if (url.pathname === "/api/v1/diagnostics" && request.method === "GET") return json(await diagnostics(env, device));
  if (url.pathname === "/api/v1/devices" && request.method === "GET") return json(await listDevices(env, device));
  if (url.pathname === "/api/v1/devices/pairing" && request.method === "POST") return json(await pairingStartForWindows(request, env, device), 201);
  const revokeMatch = url.pathname.match(/^\/api\/v1\/devices\/([^/]+)\/revoke$/);
  if (revokeMatch && request.method === "POST") return json(await revokeDevice(env, device, revokeMatch[1]));

  if (url.pathname === "/api/v1/transfers" && request.method === "POST") return json(await createTransfer(env, device, data), 201);
  if (url.pathname === "/api/v1/transfers/pending" && request.method === "GET") return json(await pendingTransfers(env, device));
  const completeMatch = url.pathname.match(/^\/api\/v1\/transfers\/([^/]+)\/complete-upload$/);
  if (completeMatch && request.method === "POST") return json(await completeUpload(env, device, completeMatch[1]));
  const pcCompleteMatch = url.pathname.match(/^\/api\/v1\/transfers\/([^/]+)\/pc-complete$/);
  if (pcCompleteMatch && request.method === "POST") return json(await confirmPcSave(env, device, pcCompleteMatch[1], data));
  const cancelMatch = url.pathname.match(/^\/api\/v1\/transfers\/([^/]+)\/cancel$/);
  if (cancelMatch && request.method === "POST") return json(await cancelTransfer(env, device, cancelMatch[1]));
  throw new HttpError(404, "Not found", "not_found");
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    try { return await handleFetch(request, env); }
    catch (error) {
      if (error instanceof HttpError) return json({ error: error.code, message: error.message }, error.status);
      console.error("unhandled", error);
      return json({ error: "internal", message: "Internal server error" }, 500);
    }
  },

  async scheduled(controller: ScheduledController, env: Env, ctx: ExecutionContext): Promise<void> {
    ctx.waitUntil((async () => {
      await scheduledMaintenance(env);
      if (controller.cron === "17 3 * * *") await dailyMaintenance(env);
    })());
  },
} satisfies ExportedHandler<Env>;
