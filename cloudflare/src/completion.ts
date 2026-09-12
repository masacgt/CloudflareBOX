import { HttpError, type AuthenticatedDevice } from "./auth";
import { abortMultipart } from "./r2";
import type { Env, TransferRow } from "./types";
import { recordUsage } from "./usage";

async function transfer(env: Env, id: string): Promise<TransferRow> {
  const row = await env.DB.prepare("SELECT * FROM transfers WHERE id=?").bind(id).first<TransferRow>();
  if (!row) throw new HttpError(404, "Transfer not found", "transfer_missing");
  return row;
}

export async function confirmPcSave(env: Env, device: AuthenticatedDevice, id: string, body: any): Promise<object> {
  const row = await transfer(env, id);
  if (device.kind !== "windows" || row.windows_device_id !== device.id) throw new HttpError(403, "Windows owner required", "auth_role");
  const actual = String(body?.sha256 ?? "").toLowerCase();
  if (!row.plaintext_sha256 || actual !== row.plaintext_sha256.toLowerCase()) throw new HttpError(409, "File verification failed", "file_verify");
  const now = Math.floor(Date.now() / 1000);
  await env.DB.prepare("UPDATE transfers SET state='DELETE_PENDING',pc_saved_at=?,delete_after=?,updated_at=? WHERE id=?")
    .bind(now, now + 300, now, id).run();
  return { transferId: id, state: "DELETE_PENDING", deleteAfter: now + 300 };
}

async function markCanceling(env: Env, row: TransferRow, now: number, code: string): Promise<object> {
  await env.DB.prepare("UPDATE transfers SET state='CANCELING',canceled_at=COALESCE(canceled_at,?),delete_after=?,error_code=?,updated_at=? WHERE id=?")
    .bind(now, now, code, now, row.id).run();
  return { transferId: row.id, state: "CANCELING" };
}

async function markCanceled(env: Env, row: TransferRow, now: number): Promise<object> {
  await env.DB.prepare("UPDATE transfers SET state='CANCELED',multipart_upload_id=NULL,plaintext_sha256=NULL,delete_after=NULL,error_code=NULL,canceled_at=COALESCE(canceled_at,?),updated_at=? WHERE id=?")
    .bind(now, now, row.id).run();
  return { transferId: row.id, state: "CANCELED" };
}

export async function cancelTransfer(env: Env, device: AuthenticatedDevice, id: string): Promise<object> {
  const row = await transfer(env, id);
  if (device.kind !== "android" || row.android_device_id !== device.id) throw new HttpError(403, "Android owner required", "auth_role");
  if (row.state === "COMPLETE" || row.state === "CANCELED") return { transferId: id, state: row.state };
  const now = Math.floor(Date.now() / 1000);

  const multipartStillOpen = row.r2_ready_at === null && row.multipart_upload_id !== null;
  if (multipartStillOpen) {
    try {
      await abortMultipart(env, row.object_key, row.multipart_upload_id!);
      await recordUsage(env, 1, 0);
      return await markCanceled(env, row, now);
    } catch {
      return await markCanceling(env, row, now, "cancel_abort_retry");
    }
  }

  try {
    await env.FILES.delete(row.object_key);
    await recordUsage(env, 1, 0);
    return await markCanceled(env, row, now);
  } catch {
    return await markCanceling(env, row, now, "cancel_delete_retry");
  }
}
